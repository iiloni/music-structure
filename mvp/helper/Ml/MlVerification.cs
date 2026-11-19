namespace MusicStructure;

static class MlVerification
{
    public static int Run(string modelPath)
    {
        try
        {
            VerifyPreprocessing();
            Require(Session.AllowsMl("spotify", null) && !Session.AllowsMl("replay", "ml") &&
                Session.AllowsMl("local", null) && Session.AllowsMl("fixture", "ml"), "Unexpected ML source authorization.");
            double loadStart = Clock.Milliseconds;
            using var model = new Musicnn(modelPath);
            Console.WriteLine($"MusiCNN CPU session initialized in {Clock.Milliseconds - loadStart:F1} ms.");
            var points = new List<MlPoint>();
            var diagnostics = new HashSet<string>();
            var persistence = new Persistence();
            double currentTime = 0;
            var analysis = new MlTexture(model, (state, _, point, embedding) =>
            {
                diagnostics.Add(state);
                if (point is not null && embedding is not null)
                {
                    points.Add(point);
                    persistence.Embedding(point, embedding);
                }
                else if (state == "silence") persistence.Interrupt(currentTime, "Silence.");
            });
            var legacy = new Listen(0, "replay", "ml");
            int generation = 0;
            long index = 0;
            bool gapOpen = false;
            foreach (var packet in MlStudy.Packets())
            {
                currentTime = packet.StartMs / 1000;
                legacy.Add(packet, false);
                if (packet.IsGap)
                {
                    if (!gapOpen)
                    {
                        generation++;
                        persistence.Interrupt(MlStudy.Gap, "Capture gap.");
                    }
                    gapOpen = true;
                    continue;
                }
                gapOpen = false;
                if (legacy.Frames.LastOrDefault() is { } frame && frame.Index != index)
                {
                    index = frame.Index;
                    persistence.Feature(frame);
                }
                analysis.Add(new MlPacket(packet, currentTime, generation));
            }
            persistence.Interrupt(MlStudy.Duration, "Capture stopped.");

            var first = legacy.Frames.Where(frame => frame.Start >= 2 && frame.End <= 7.5).ToArray();
            var second = legacy.Frames.Where(frame => frame.Start >= 21 && frame.End <= 25).ToArray();
            double rmsDifference = second.Average(f => f.RmsDb) - first.Average(f => f.RmsDb);
            double centroidDifference = 100 * (second.Average(f => f.CentroidHz) / first.Average(f => f.CentroidHz) - 1);
            Console.WriteLine($"Instrument swap controls: RMS Δ {rmsDifference:F3} dB, centroid Δ {centroidDifference:F3}%.");
            Require(Math.Abs(rmsDifference) <= .5 && Math.Abs(centroidDifference) <= 5, "The swap changed level or brightness materially.");
            Require(diagnostics.Contains("warmup") && diagnostics.Contains("silence") && diagnostics.Contains("ready"), "Missing runtime states.");
            Require(points.Count(point => point.Novelty is null) == 3, "Baseline must restart after silence and the gap.");
            Require(points.All(point => point.End <= MlStudy.Gap || point.Start >= MlStudy.AfterGap - .001), "A model window crossed the capture gap.");
            Require(points.All(point => point.End <= MlStudy.Quiet + .05 || point.Start >= MlStudy.Resume - .001), "A model window crossed sustained silence.");

            foreach (var point in points)
                Console.WriteLine($"  {point.Start:F3}–{point.End:F3}s novelty={point.Novelty?.ToString("F5") ?? "warmup"} input={point.InputMs:F2}ms inference={point.InferenceMs:F2}ms");
            ReportCue("short fill", MlStudy.Fill, MlStudy.Swap, points);
            ReportCue("instrument swap", MlStudy.Swap, MlStudy.Gain, points);
            ReportCue("gain-only step", MlStudy.Gain, MlStudy.Layer, points);
            ReportCue("gradual layering", MlStudy.Layer, MlStudy.Quiet, points);
            var review = persistence.Read(0);
            CheckPersistence(points, review);
            foreach (var cue in review.Cues)
                Console.WriteLine($"A/B {cue.Time:F3}s -> {cue.ConfirmedAt:F3}s: A {cue.Spectrum}, B {cue.Embedding}; normalized onset {cue.SpectrumDistance:F2}/{cue.EmbeddingDistance:F2}. {cue.Reason}");
            Console.WriteLine($"A/B extra work: {review.ProcessingMs:F3} ms over {review.Comparisons} paired hops.");
            var steady = points.Skip(1).ToArray();
            Console.WriteLine($"Accelerated fixture, excluding first patch: input p50/p95 {Percentile(steady.Select(p => p.InputMs), .5):F1}/{Percentile(steady.Select(p => p.InputMs), .95):F1} ms; CPU inference p50/p95 {Percentile(steady.Select(p => p.InferenceMs), .5):F1}/{Percentile(steady.Select(p => p.InferenceMs), .95):F1} ms.");
            VerifyBackpressure(modelPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
    }

    static void VerifyBackpressure(string modelPath)
    {
        var pipeline = new MlPipeline(modelPath);
        short[] shortSilence = new short[Packet.Rate / 10 * Packet.Channels];
        for (int k = 0; k < 10_000; k++)
            pipeline.Offer(new Packet(k, k * 100.0, shortSilence), k * .1);
        pipeline.Finish(1000).GetAwaiter().GetResult();
        long revision = -1;
        var result = pipeline.Read(ref revision, true, 0)!;
        Require(result.DroppedPackets > 0 && result.Points.Length == 0, "The ML queue did not discard an accelerated burst.");
        Console.WriteLine($"Backlog stress: dropped {result.DroppedPackets} packets; no embedding from silence.");
    }

    static void CheckPersistence(List<MlPoint> points, PersistenceReport review)
    {
        Require(review.Cues.Length == 4, "Expected four common candidates, including the gain transient and interrupted onset.");
        var expected = new[] { (MlStudy.Fill, "brief", "brief"), (MlStudy.Swap, "lasting", "lasting"),
            (MlStudy.Gain, "uncertain", "brief"), (MlStudy.BeforeGap, "uncertain", "uncertain") };
        for (int i = 0; i < expected.Length; i++)
        {
            var cue = review.Cues[i];
            var (onset, a, b) = expected[i];
            Require(cue.Time - onset is >= 0 and <= 2 && cue.Spectrum == a && cue.Embedding == b,
                $"Shared cue at {onset}s gave {cue.Spectrum}/{cue.Embedding}.");
            Require(cue.ReferenceEnd < cue.Time && cue.ReferenceStart < cue.ReferenceEnd,
                "A confirmation reference included a later observation.");
            if (i < expected.Length - 1)
                Require(Math.Abs(cue.ConfirmedAt!.Value - cue.Time - Persistence.Horizon) < .001,
                    "A verdict missed the common confirmation horizon.");
        }
        var gapCue = review.Cues[^1];
        Require(gapCue.ConfirmedAt is <= MlStudy.Gap && gapCue.Reason == "Capture gap.",
            "The comparison confirmed across a capture gap.");
        var layer = points.Where(p => p.End > MlStudy.Layer && p.End < MlStudy.Quiet).Max(p => p.Novelty);
        var steady = points.Where(p => p.End > 4 && p.End < MlStudy.Fill).Max(p => p.Novelty);
        Require(layer >= 3 * steady && layer < MlTexture.Threshold, "The gradual layer control did not yield a small visible response.");
    }

    static void VerifyPreprocessing()
    {
        // Reference samples from Essentia 2.1 TensorflowInputMusiCNN on the following 512-sample input.
        var input = Enumerable.Range(0, 512).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * 440 * i / 16000) +
            .07 * Math.Cos(2 * Math.PI * 1700 * i / 16000))).ToArray();
        var bins = new float[96];
        new MusicnnInput().Transform(input, bins);
        foreach (var (bin, expected) in new[] { (4, .00054833), (12, 4.5842395), (13, 5.293284),
                     (14, 4.8789387), (15, 3.6944027), (16, .9151254), (46, 1.9890931), (50, .3991689) })
            Require(Math.Abs(bins[bin] - expected) < .0015,
                $"MusiCNN preprocessing at mel bin {bin}: expected {expected}, got {bins[bin]}.");
    }

    static void ReportCue(string label, double start, double end, List<MlPoint> points)
    {
        var slice = points.Where(point => point.End > start && point.End < end && point.Novelty.HasValue).ToArray();
        var hit = slice.FirstOrDefault(point => point.Novelty >= MlTexture.Threshold);
        var peak = slice.MaxBy(point => point.Novelty);
        Console.WriteLine($"{label}: first above threshold {(hit is null ? "none" : $"{hit.End - start:F3}s after cue")}; peak {peak?.Novelty:F5}.");
    }

    static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1];
    }

    static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }
}
