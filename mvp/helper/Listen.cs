namespace MusicStructure;

record Counters(long Packets, long Frames, long QueueDroppedPackets, long QueueDroppedFrames,
    long Discontinuities, long TimestampErrors, double MissingSeconds, double MaxGapSeconds,
    long GapsOver250Ms, long Features, double MeanFeatureComputeMs);
record Batch(int Version, string AnalysisVersion, string SessionId, long Sequence, string State,
    string Status, string? Mode, string? Source, double Elapsed, double HistoryStart, double? InputDb,
    Counters Counters, Feature[] Frames, Mark[]? Marks, bool Snapshot, Source[]? Sources, bool WindowsCapture);

sealed class Listen(double startMs, string mode, string source)
{
    public const string AnalysisVersion = "score-2";
    public const double HistorySeconds = 600;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Mode { get; } = mode;
    public string Source { get; set; } = source;
    public double StartedMs { get; } = startMs;
    public double Elapsed { get; private set; }
    public long MarkRevision { get; private set; }
    public Queue<Feature> Frames { get; } = new();
    public IReadOnlyCollection<Mark> Marks => marks.Values;
    public double HistoryStart => Math.Max(0, Elapsed - HistorySeconds);
    public double? InputDb { get; private set; }
    public long DroppedPackets, DroppedFrames;
    public (double StartMs, double EndMs)? LastDropped { private get; set; }
    public long Revision { get; private set; }

    readonly AudioFeatures features = new();
    readonly Dictionary<int, Mark> marks = new();
    Structure? structure;
    AdditionalLanes? additional;
    Mark? openGap;
    int segment, markId;
    double? packetEndMs, packetStampEndMs;
    double segmentStart, coverageEnd, committedMissing, maxClosedGap, featureComputeMs;
    long? packetSequence;
    long packets, pcmFrames, discontinuities, timestampErrors, featureCount, closedLargeGaps;

    public Counters Counters
    {
        get
        {
            double pendingGap = openGap is null ? 0 : openGap.End - openGap.Start;
            return new(packets, pcmFrames, DroppedPackets, DroppedFrames, discontinuities, timestampErrors,
                committedMissing + pendingGap, Math.Max(maxClosedGap, pendingGap),
                closedLargeGaps + (pendingGap > .25 ? 1 : 0), featureCount,
                featureComputeMs / Math.Max(1, featureCount));
        }
    }

    public void Add(Packet packet, bool includePaintClock)
    {
        if (packet.StartMs < StartedMs) return;
        if (packet.IsGap)
        {
            Gap(packet.StartMs, packet.Interruption!);
            return;
        }

        var previousSequence = packetSequence;
        bool lost = previousSequence.HasValue && packet.Sequence != previousSequence + 1;
        bool timestampError = (packet.Flags & 4) != 0;
        bool flagged = (packet.Flags & 1) != 0 && previousSequence.HasValue;
        bool skipped = packetStampEndMs.HasValue && Math.Abs(packet.StartMs - packetStampEndMs.Value) > 30;
        if (timestampError) timestampErrors++;
        if (lost || flagged || skipped)
        {
            discontinuities++;
            string reason = lost ? "Missing capture packets" : flagged ? "WASAPI discontinuity" : "Capture clock discontinuity";
            Gap(packet.StartMs, reason);
        }

        // Stay sample-contiguous within a segment even if WASAPI packet timestamps jitter.
        double audioStart = packetEndMs ?? Math.Max(StartedMs + Elapsed * 1000, packet.StartMs);
        if (!packetEndMs.HasValue)
        {
            segmentStart = Seconds(audioStart);
            if (openGap is null && segmentStart - coverageEnd > .25) Gap(audioStart, "Waiting for first audio packet");
            CloseGap(segmentStart);
        }
        packetSequence = packet.Sequence;
        double durationMs = packet.Frames * 1000.0 / Packet.Rate;
        packetEndMs = audioStart + durationMs;
        packetStampEndMs = packet.StartMs + durationMs;
        Elapsed = Seconds(packetEndMs.Value);
        packets++;
        pcmFrames += packet.Frames;
        structure ??= new Structure(Publish, () => ++markId);
        additional ??= new AdditionalLanes(Publish, () => ++markId);
        double computeStart = Clock.Milliseconds;
        long before = featureCount;
        features.Add(packet.Samples, (start, end, rms, centroid, audible, mel, chroma) =>
        {
            double sessionEnd = segmentStart + end;
            var frame = new Feature(++featureCount, segmentStart + start, sessionEnd,
                includePaintClock && !timestampError
                    ? packet.StartMs + (StartedMs + sessionEnd * 1000 - audioStart) : null,
                segment, rms, centroid, true, audible, mel, chroma)
            {
                PacketReadMs = includePaintClock ? packet.PacketReadMs : null
            };
            frame = frame with { Lanes = additional.Add(frame, segmentStart) };
            Frames.Enqueue(frame);
            InputDb = rms;
            coverageEnd = sessionEnd;
            structure.Add(frame);
        }, additional.ObservePulse);
        // Account for all pipeline work, even for packets that do not themselves emit a feature.
        featureComputeMs += Clock.Milliseconds - computeStart;
        if (featureCount > before)
        {
            Revision++;
            Trim();
        }
    }

    public void Gap(double clockMs, string reason)
    {
        double end = Math.Max(Elapsed, Seconds(clockMs));
        Elapsed = end;
        if (openGap is null)
        {
            openGap = new Mark(++markId, "gap", coverageEnd, end, reason);
            features.Reset();
            structure?.Reset();
            additional?.Reset();
            segment++;
        }
        else openGap = openGap with { End = end };
        packetEndMs = packetStampEndMs = null;
        packetSequence = null;
        InputDb = null;
        Publish(openGap);
        Revision++;
        Trim();
    }

    public void Finish()
    {
        if (LastDropped is { } last && last.StartMs >= StartedMs && Seconds(last.StartMs) >= Elapsed - 0.001)
        {
            discontinuities++;
            Gap(last.EndMs, "Capture queue overrun");
        }
    }

    void CloseGap(double end)
    {
        if (openGap is null) return;
        var gap = openGap with { End = end };
        double duration = Math.Max(0, gap.End - gap.Start);
        committedMissing += duration;
        maxClosedGap = Math.Max(maxClosedGap, duration);
        if (duration > .25) closedLargeGaps++;
        Publish(gap);
        openGap = null;
    }

    double Seconds(double absoluteMs) => Math.Max(0, (absoluteMs - StartedMs) / 1000);

    void Publish(Mark mark)
    {
        marks[mark.Id] = mark;
        MarkRevision++;
    }

    void Trim()
    {
        while (Frames.TryPeek(out var first) && first.End <= HistoryStart) Frames.Dequeue();
        if (marks.Count == 0) return;
        foreach (int id in marks.Values.Where(m => (m.OtherEnd ?? m.End) < HistoryStart).Select(m => m.Id).ToArray())
        {
            marks.Remove(id);
            MarkRevision++;
        }
    }
}
