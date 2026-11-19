namespace MusicStructure;

record Mark(int Id, string Kind, double Start, double End, string Detail, double? OtherStart = null,
    double? OtherEnd = null, double? Score = null, double? DeltaHz = null, double? DetectedAt = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Family { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Passage[]? Occurrences { get; init; }
}

sealed class Structure(Action<Mark> publish, Func<int> nextId)
{
    const int Window = 8, Phrase = 32;
    const double TimbreThreshold = 15.5;
    readonly List<Feature> history = new(2400);
    readonly Queue<(Feature Point, double Novelty, double Delta)> contour = new();
    double lastBoundary = double.NegativeInfinity;
    int ticks;
    Mark? activeRepeat;

    public void Reset()
    {
        history.Clear();
        contour.Clear();
        activeRepeat = null;
        lastBoundary = double.NegativeInfinity;
        ticks = 0;
    }

    public void Add(Feature feature)
    {
        if (history.Count == 2400) history.RemoveAt(0);
        history.Add(feature);
        Timbre();
        if (++ticks % 4 == 0) Repeat();
    }

    void Timbre()
    {
        if (history.Count < 2 * Window) return;
        int pivot = history.Count - Window;
        Feature middle = history[pivot];
        double novelty = 0, delta = 0;
        if (history.GetRange(pivot - Window, 2 * Window).All(f => f.Audible))
        {
            for (int band = 0; band < middle.Mel.Length; band++)
            {
                double difference = 0;
                for (int k = 0; k < Window; k++)
                    difference += history[pivot + k].Mel[band] - history[pivot - Window + k].Mel[band];
                novelty += Math.Pow(difference / Window, 2);
            }
            novelty = Math.Sqrt(novelty / middle.Mel.Length);
            for (int k = 0; k < Window; k++)
                delta += (history[pivot + k].CentroidHz - history[pivot - Window + k].CentroidHz) / Window;
        }
        contour.Enqueue((middle, novelty, delta));
        if (contour.Count < 7) return;
        var peak = contour.ElementAt(3);
        if (peak.Point.Start - lastBoundary >= 3 && peak.Novelty >= TimbreThreshold &&
            peak.Novelty >= contour.Max(x => x.Novelty) &&
            contour.Count(x => x.Novelty >= TimbreThreshold * .8) >= 4)
        {
            lastBoundary = peak.Point.Start;
            string change = peak.Delta > 100 ? "Sustained rise in brightness" :
                peak.Delta < -100 ? "Sustained fall in brightness" : "Sustained change in spectral shape";
            publish(new Mark(nextId(), "timbre", lastBoundary, lastBoundary, change,
                Score: peak.Novelty, DeltaHz: peak.Delta, DetectedAt: history[^1].End));
        }
        contour.Dequeue();
    }

    void Repeat()
    {
        int now = history.Count - Phrase;
        if (now < Phrase) return;

        if (activeRepeat is { OtherEnd: double returnEnd } pair)
        {
            int tail = history.Count - 4;
            double offset = pair.OtherStart!.Value - pair.Start;
            int earlier = tail - (int)Math.Round(offset * 4);
            if (Math.Abs(history[tail].Start - returnEnd) < .01 &&
                pair.End + 1 <= pair.OtherStart.Value + .001 &&
                earlier >= 0 && Score(earlier, tail, 4) >= .91)
            {
                activeRepeat = pair with { End = pair.End + 1, OtherEnd = history[^1].End };
                publish(activeRepeat);
                return;
            }
            // The new query must clear the previous occurrence before it can seed a second link.
            if (history[now].Start < returnEnd - .01) return;
            activeRepeat = null;
        }

        if (!HasMovement(now)) return;
        int best = -1;
        double bestScore = .91;
        for (int candidate = 0; candidate + Phrase <= now; candidate += 2)
        {
            if (!history[candidate].Audible || !history[candidate + Phrase - 1].Audible) continue;
            double score = Score(candidate, now, Phrase);
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        if (best < 0 || !HasMovement(best)) return;

        // Check shifted alternatives and distinct nearby alignments. Small shifts are the same peak.
        const int shift = 6;
        for (int candidate = best - 16; candidate <= best + 16; candidate += 2)
        {
            if (candidate < 0 || candidate + Phrase > now || Math.Abs(candidate - best) < shift) continue;
            if (bestScore - Score(candidate, now, Phrase) < .055) return;
        }
        activeRepeat = new Mark(nextId(), "repeat", history[best].Start, history[best + Phrase - 1].End,
            "Similar chord movement", history[now].Start, history[^1].End, bestScore, DetectedAt: history[^1].End);
        publish(activeRepeat);
    }

    bool HasMovement(int start)
    {
        var mean = new double[12];
        for (int k = start; k < start + Phrase; k++)
        {
            if (!history[k].Audible) return false;
            for (int p = 0; p < mean.Length; p++) mean[p] += history[k].Chroma[p] / Phrase;
        }
        if (mean.Sum(x => x * x) > .88) return false;
        // Reject very short repeating fragments by testing their own shorter periods.
        for (int lag = 4; lag <= 12; lag++)
            if (Score(start, start + lag, Phrase - lag) >= .94) return false;
        return true;
    }

    double Score(int a, int b, int length)
    {
        double total = 0;
        int aligned = 0;
        for (int k = 0; k < length; k++)
        {
            Feature first = history[a + k], second = history[b + k];
            if (!first.Audible || !second.Audible) return 0;
            double similarity = ChromaSimilarity(first, second);
            if (similarity >= .82) aligned++;
            total += similarity;
        }
        return aligned >= .8 * length ? Math.Min(1, total / length) : 0;
    }
    internal static double ChromaSimilarity(Feature first, Feature second)
    {
        double similarity = 0;
        for (int p = 0; p < 12; p++) similarity += first.Chroma[p] * second.Chroma[p];
        return similarity;
    }

}
