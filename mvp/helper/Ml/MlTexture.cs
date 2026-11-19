using System.Threading.Channels;

namespace MusicStructure;

record MlPoint(double Start, double End, int Run, double? Novelty, double InputMs, double InferenceMs, double? AfterWindowMs);
record MlSnapshot(string State, string Detail, double Threshold, long DroppedPackets, MlPoint[] Points, PersistenceReport Persistence);
record MlPacket(Packet Packet, double Start, int Generation);

sealed class MlTexture(Musicnn model, Action<string, string, MlPoint?, float[]?> publish)
{
    public const double Threshold = .015;
    const int HopFrames = Packet.Rate, QuietReset = Packet.Rate / 4;
    readonly MusicnnInput input = new();
    readonly float[] ring = new float[MusicnnInput.PcmFrames], patch = new float[MusicnnInput.PcmFrames];
    float[]? previous;
    long? sequence;
    int generation = -1, run, count, next, hop, quiet;

    public void Add(MlPacket item)
    {
        var packet = item.Packet;
        if (item.Generation != generation || sequence.HasValue && sequence + 1 != packet.Sequence)
        {
            generation = item.Generation;
            Reset();
            publish("warmup", "Collecting a fresh three-second window.", null, null);
        }
        sequence = packet.Sequence;

        double energy = 0;
        for (int i = 0; i < packet.Samples.Length; i += 2)
        {
            double value = (packet.Samples[i] + packet.Samples[i + 1]) / 65536.0;
            energy += value * value;
        }
        if (energy <= .000001585 * packet.Frames) // -58 dBFS.
        {
            quiet += packet.Frames;
            if (quiet >= QuietReset)
            {
                if (count > 0) Reset();
                quiet = QuietReset;
                publish("silence", "Waiting for audible local audio.", null, null);
                return;
            }
        }
        else
        {
            if (quiet >= QuietReset) publish("warmup", "Collecting a fresh three-second window.", null, null);
            quiet = 0;
        }

        for (int frame = 0; frame < packet.Frames; frame++)
        {
            ring[next] = (packet.Samples[frame * 2] + packet.Samples[frame * 2 + 1]) / 65536f;
            if (++next == ring.Length) next = 0;
            if (count < ring.Length)
            {
                if (++count < ring.Length) continue;
            }
            else if (++hop < HopFrames) continue;
            hop = 0;
            if (quiet != 0) continue; // Do not publish from a hop that ends in silence.

            double offset = (frame + 1.0) / Packet.Rate;
            double end = item.Start + offset;
            double? clockEnd = packet.PacketReadMs is not null && (packet.Flags & 4) == 0
                ? packet.StartMs + offset * 1000 : null;
            Analyze(end, clockEnd);
        }
    }

    void Analyze(double end, double? clockEnd)
    {
        double startAt = Clock.Milliseconds;
        ring.AsSpan(next).CopyTo(patch);
        ring.AsSpan(0, next).CopyTo(patch.AsSpan(ring.Length - next));
        var features = input.Prepare(patch);
        double prepared = Clock.Milliseconds;
        float[] current = model.Embed(features);
        double finished = Clock.Milliseconds;
        double? novelty = previous is null ? null : Persistence.EmbeddingDistance(current, previous);
        previous = current;
        publish(novelty is null ? "warmup" : "ready",
            novelty is null ? "First embedding ready. Waiting for the next hop." : "MusiCNN compares successive trailing windows.",
            new MlPoint(end - (double)ring.Length / Packet.Rate, end, run, novelty, prepared - startAt,
                finished - prepared, clockEnd is double stamp ? finished - stamp : null), current);
    }

    void Reset()
    {
        previous = null;
        count = next = hop = quiet = 0;
        run++;
    }
}

sealed class MlPipeline
{
    readonly object gate = new();
    readonly Queue<MlPoint> points = new();
    readonly Persistence persistence = new();
    readonly Channel<MlPacket> input;
    readonly Task worker;
    int generation;
    long revision, dropped;
    string state = "loading", detail = "Loading the local MusiCNN model.";
    bool unavailable;

    public MlPipeline(string modelPath)
    {
        input = Channel.CreateBounded<MlPacket>(new BoundedChannelOptions(40)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
        }, _ => Interlocked.Increment(ref dropped));
        worker = Task.Run(() => Work(modelPath));
    }

    public void Offer(Packet packet, double start)
    {
        if (!Volatile.Read(ref unavailable)) input.Writer.TryWrite(new MlPacket(packet, start, generation));
    }

    public void OfferFeature(Feature feature)
    {
        lock (gate)
        {
            if (unavailable) return;
            // Draining here feeds only retained feature vectors. Inference always runs on the ML worker.
            if (persistence.Feature(feature)) revision++;
        }
    }

    public void Reset(double time = 0, bool newListen = false)
    {
        lock (gate)
        {
            generation++;
            if (newListen)
            {
                points.Clear();
                persistence.Clear();
                Interlocked.Exchange(ref dropped, 0);
            }
            else persistence.Interrupt(time, "Capture gap.");
            if (!unavailable)
            {
                state = "warmup";
                detail = "Collecting a fresh three-second window.";
            }
            revision++;
        }
    }

    public MlSnapshot? Read(ref long seen, bool snapshot, double from)
    {
        lock (gate)
        {
            if (!snapshot && seen == revision) return null;
            seen = revision;
            return new MlSnapshot(state, detail, MlTexture.Threshold, Interlocked.Read(ref dropped),
                points.Where(point => point.End >= from).ToArray(), persistence.Read(from));
        }
    }

    public async Task Finish(double time)
    {
        input.Writer.TryComplete();
        await worker;
        lock (gate)
        {
            if (unavailable) return;
            persistence.Interrupt(time, "Capture stopped before the confirmation deadline.");
            state = "stopped";
            detail = "Last ML results are ready to review.";
            revision++;
        }
    }

    async Task Work(string path)
    {
        double workingTime = 0;
        try
        {
            using var model = new Musicnn(path);
            int workingGeneration = -1;
            var analysis = new MlTexture(model, (next, message, point, embedding) =>
            {
                lock (gate)
                {
                    if (generation != workingGeneration) return;
                    if (point is not null && embedding is not null)
                    {
                        persistence.Embedding(point, embedding);
                        if (points.Count >= 601) points.Dequeue();
                        points.Enqueue(point);
                    }
                    if (state == next && detail == message && point is null) return;
                    if (next == "silence" || next == "warmup" && point is null)
                        persistence.Interrupt(workingTime, next == "silence" ? "Silence." : "The analysis window was interrupted.");
                    state = next;
                    detail = message;
                    revision++;
                }
            });

            lock (gate)
            {
                state = "warmup";
                detail = "Collecting a fresh three-second window.";
                revision++;
            }
            await foreach (var work in input.Reader.ReadAllAsync())
            {
                if (work.Generation != Volatile.Read(ref generation)) continue;
                workingGeneration = work.Generation;
                workingTime = work.Start;
                analysis.Add(work);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ML texture lane unavailable: {ex}");
            lock (gate)
            {
                Volatile.Write(ref unavailable, true);
                state = "unavailable";
                persistence.Interrupt(workingTime, "ML became unavailable.");
                detail = ex is InvalidOperationException ? ex.Message : "Could not initialize or run MusiCNN. Check the helper console.";
                revision++;
            }
            input.Writer.TryComplete();
            while (input.Reader.TryRead(out _)) { }
        }
    }
}
