namespace MusicStructure;

record PersistenceCue(int Id, double Time, double ReferenceStart, double ReferenceEnd, double DueAt,
    string Spectrum, string Embedding, double? ConfirmedAt, string? Reason, double SpectrumDistance, double EmbeddingDistance);
record PersistenceReport(double HorizonSeconds, int Comparisons, double ProcessingMs, PersistenceCue[] Cues);

sealed class Persistence
{
    public const double Horizon = 5;
    const int ReferenceHops = 2;
    readonly Queue<Feature> frames = new();
    readonly Queue<(MlPoint Point, float[] Embedding)> pending = new();
    readonly Queue<Comparison> recent = new();
    readonly Queue<PersistenceCue> decisions = new();
    Comparison? previous;
    ActiveCue? active;
    int comparisons, nextId;
    double computeMs;

    record Comparison(MlPoint Point, double[] Spectrum, float[] Embedding, double SpectrumStep, double EmbeddingStep);
    sealed record ActiveCue(PersistenceCue Cue, Comparison[] Reference, double SpectrumScale, double EmbeddingScale)
    {
        public List<(double A, double B)> Followup { get; } = [];
    }

    public bool Feature(Feature feature)
    {
        if (frames.Count >= 80) frames.Dequeue();
        frames.Enqueue(feature);
        return Drain();
    }

    public void Embedding(MlPoint point, float[] embedding)
    {
        if (pending.Count >= 4) pending.Dequeue();
        pending.Enqueue((point, embedding));
        Drain();
    }

    bool Drain()
    {
        if (frames.Count == 0 || pending.Count == 0) return false;
        double started = Clock.Milliseconds;
        bool processed = false;
        while (pending.TryPeek(out var next) && frames.Last().End >= next.Point.End - .250001)
        {
            pending.Dequeue();
            processed = true;
            // Use only completed spectral frames from the same trailing patch. The latest
            // partial 250 ms bin has not ended at the candidate time and is excluded.
            var window = frames.Where(f => f.Start >= next.Point.Start - .001 && f.End <= next.Point.End + .001).ToArray();
            if (window.Length < 10 || window.Any(f => !f.Valid || !f.Audible || f.Segment != window[0].Segment)
                || window[^1].Index - window[0].Index != window.Length - 1)
            {
                Interrupt(next.Point.End, "Insufficient comparable audio.");
                continue;
            }
            var spectrum = new double[window[0].Mel.Length];
            foreach (var frame in window)
                for (int i = 0; i < spectrum.Length; i++) spectrum[i] += frame.Mel[i] / window.Length;
            Compare(next.Point, spectrum, next.Embedding);
        }
        if (processed) computeMs += Clock.Milliseconds - started;
        return processed;
    }

    void Compare(MlPoint point, double[] spectrum, float[] embedding)
    {
        if (previous is { } prior && (point.Run != prior.Point.Run || point.End - prior.Point.End > 1.01))
            Interrupt(point.End, "The analysis window was interrupted.", clearPending: false);
        double a = previous is null ? 0 : SpectralDistance(spectrum, previous.Spectrum);
        double b = previous is null ? 0 : EmbeddingDistance(embedding, previous.Embedding);
        var current = new Comparison(point, spectrum, embedding, a, b);
        comparisons++;

        if (active is { } cue)
        {
            var (distanceA, distanceB) = Distances(current, cue.Reference, cue.SpectrumScale, cue.EmbeddingScale);
            cue.Followup.Add((distanceA, distanceB));
            if (point.End >= cue.Cue.DueAt - .001)
            {
                string aLabel = Decide(cue.Followup.Select(x => x.A).ToArray(), cue.Cue.SpectrumDistance);
                string bLabel = Decide(cue.Followup.Select(x => x.B).ToArray(), cue.Cue.EmbeddingDistance);
                Retain(cue.Cue with { Spectrum = aLabel, Embedding = bLabel, ConfirmedAt = point.End });
                active = null;
                recent.Clear();
            }
        }
        else if (recent.Count >= ReferenceHops)
        {
            double scaleA = Scale(recent.Select(point => point.SpectrumStep), 1.5);
            double scaleB = Scale(recent.Select(point => point.EmbeddingStep), MlTexture.Threshold);
            // Either representation may nominate the shared timestamp. Freeze both references
            // and scales at that time so neither decision gets later baseline information.
            if (a >= scaleA || b >= scaleB)
            {
                var reference = recent.TakeLast(ReferenceHops).ToArray();
                var (distanceA, distanceB) = Distances(current, reference, scaleA, scaleB);
                var candidate = new PersistenceCue(++nextId, point.End, reference[0].Point.End,
                    reference[^1].Point.End, point.End + Horizon, "provisional", "provisional",
                    null, null, distanceA, distanceB);
                active = new ActiveCue(candidate, reference, scaleA, scaleB);
            }
        }

        previous = current;
        if (recent.Count >= 8) recent.Dequeue();
        recent.Enqueue(current);
    }

    static string Decide(double[] followup, double initial)
    {
        if (followup.Length < 5) return "uncertain";
        double last = followup[^1], before = followup[^2];
        if (last <= .70 && before <= .70 && Math.Max(initial, followup.Max()) >= 1) return "brief";
        if (last >= 1 && before >= 1) return "lasting";
        return "uncertain";
    }

    static double Scale(IEnumerable<double> values, double floor)
    {
        var sorted = values.Order().ToArray();
        return Math.Max(floor, 3 * sorted[sorted.Length / 2]);
    }

    static (double A, double B) Distances(Comparison point, Comparison[] reference, double a, double b) =>
        (reference.Average(p => SpectralDistance(point.Spectrum, p.Spectrum)) / a,
         reference.Average(p => EmbeddingDistance(point.Embedding, p.Embedding)) / b);

    static double SpectralDistance(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) { double delta = a[i] - b[i]; sum += delta * delta; }
        return Math.Sqrt(sum / a.Length);
    }

    internal static double EmbeddingDistance(float[] a, float[] b)
    {
        double similarity = 0;
        for (int i = 0; i < a.Length; i++) similarity += (double)a[i] * b[i];
        return Math.Clamp(1 - similarity, 0, 2);
    }

    void Retain(PersistenceCue cue)
    {
        if (decisions.Count >= 120) decisions.Dequeue();
        decisions.Enqueue(cue);
    }

    public void Interrupt(double time, string reason, bool clearPending = true)
    {
        if (active is { } cue)
            Retain(cue.Cue with { Spectrum = "uncertain", Embedding = "uncertain", ConfirmedAt = Math.Max(time, cue.Cue.Time), Reason = reason });
        if (clearPending) pending.Clear();
        active = null;
        previous = null;
        recent.Clear();
    }

    public void Clear()
    {
        Interrupt(0, "New listen.");
        frames.Clear();
        decisions.Clear();
        nextId = comparisons = 0;
        computeMs = 0;
    }

    public PersistenceReport Read(double from)
    {
        var values = decisions.Where(cue => cue.Time >= from);
        return new PersistenceReport(Horizon, comparisons, computeMs,
            active is { } cue ? values.Append(cue.Cue).ToArray() : values.ToArray());
    }
}
