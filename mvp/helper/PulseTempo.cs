namespace MusicStructure;

sealed class PulseTempo
{
    // The spectrum advances in 512-sample steps (~23 ms). Keep only eight seconds.
    const double HopSeconds = 512.0 / AudioFeatures.Rate;
    const int Capacity = (int)(8 / HopSeconds), MinLag = 13, MaxLag = 48;
    readonly List<double> spectrumFlux = new(Capacity);
    readonly Queue<double> onsets = new();
    readonly Queue<double> recentOnsets = new();
    double before, previous, lastOnset = double.NegativeInfinity, baseline, nextEstimate = 1;
    double? bpm, fit, beatAnchor, pendingBpm;

    public void Reset()
    {
        spectrumFlux.Clear();
        onsets.Clear();
        recentOnsets.Clear();
        before = previous = baseline = 0;
        lastOnset = double.NegativeInfinity;
        nextEstimate = 1;
        bpm = fit = beatAnchor = pendingBpm = null;
    }

    public void Observe(double time, double value)
    {
        double peakTime = time - HopSeconds;
        if (previous > before && previous >= value && previous >= Math.Max(.025, baseline * 2.4)
            && peakTime - lastOnset >= .15)
        {
            lastOnset = peakTime;
            onsets.Enqueue(peakTime);
            recentOnsets.Enqueue(peakTime);
        }
        before = previous;
        previous = value;
        baseline += .035 * (value - baseline);
        if (spectrumFlux.Count == Capacity) spectrumFlux.RemoveAt(0);
        spectrumFlux.Add(value);
        while (recentOnsets.TryPeek(out var onset) && onset < time - 8) recentOnsets.Dequeue();
    }

    public (double? Bpm, double? Fit, double[] Onsets, double[] Beats) Read(double end, bool audible)
    {
        if (end + .001 >= nextEstimate)
        {
            nextEstimate = end + 1;
            Estimate(end);
        }
        var batch = new List<double>();
        while (onsets.TryPeek(out double onset) && onset <= end) batch.Add(onsets.Dequeue());
        bool continuingPulse = audible && end - lastOnset < 2;
        double[] beats = continuingPulse && bpm is double pace && beatAnchor is double anchor
            ? batch.Where(onset => OnBeat(onset, anchor, 60 / pace)).ToArray() : [];
        return (continuingPulse ? bpm : null, continuingPulse ? fit : null, batch.ToArray(), beats);
    }

    void Estimate(double end)
    {
        if (spectrumFlux.Count < Capacity / 2 || recentOnsets.Count < 5 || end - lastOnset >= 2)
        {
            bpm = fit = beatAnchor = pendingBpm = null;
            return;
        }

        // Limit large accents so a chord change or isolated hit does not outweigh the pulse.
        var sorted = spectrumFlux.ToArray();
        Array.Sort(sorted);
        double ceiling = sorted[(int)(sorted.Length * .93)];
        if (ceiling < .01)
        {
            bpm = fit = beatAnchor = pendingBpm = null;
            return;
        }
        var envelope = spectrumFlux.Select(value => Math.Clamp(value / ceiling - .10, 0, 1)).ToArray();
        Span<double> scores = stackalloc double[MaxLag + 2];
        for (int lag = MinLag - 1; lag <= MaxLag + 1; lag++)
        {
            double dot = 0, a = 0, b = 0;
            for (int i = lag; i < envelope.Length; i++)
            {
                double left = envelope[i - lag], right = envelope[i];
                dot += left * right;
                a += left * left;
                b += right * right;
            }
            scores[lag] = dot / Math.Sqrt(Math.Max(1e-12, a * b));
        }

        int best = 0;
        double bestRank = 0;
        for (int lag = MinLag; lag <= MaxLag; lag++)
        {
            if (scores[lag] < .44 || scores[lag] < scores[lag - 1] || scores[lag] < scores[lag + 1]) continue;
            double rank = scores[lag] - (lag - MinLag) * .004;
            if (rank <= bestRank) continue;
            best = lag;
            bestRank = rank;
        }
        if (best == 0)
        {
            bpm = fit = beatAnchor = pendingBpm = null;
            return;
        }

        // Prefer the shorter period when both it and its double explain the pulse.
        int half = best / 2;
        if (half >= MinLag)
        {
            int faster = scores[half] >= scores[half + 1] ? half : half + 1;
            if (scores[faster] >= Math.Max(.44, .79 * scores[best])) best = faster;
        }

        double curvature = scores[best - 1] - 2 * scores[best] + scores[best + 1];
        double offset = curvature < -1e-9 ? .5 * (scores[best - 1] - scores[best + 1]) / curvature : 0;
        double estimate = 60 / ((best + Math.Clamp(offset, -.5, .5)) * HopSeconds);
        // An isolated accent may briefly favor another octave of the pulse.
        // Require two successive estimates before moving an established tempo.
        if (bpm is double established && Math.Abs(established - estimate) >= 8 &&
            (pendingBpm is not double pending || Math.Abs(pending - estimate) >= 4))
        {
            pendingBpm = estimate;
            return;
        }

        pendingBpm = null;
        bpm = bpm is double previous && Math.Abs(previous - estimate) < 8 ? .7 * previous + .3 * estimate : estimate;
        fit = Math.Min(1, scores[best]);

        // Mark an observed onset as a beat only if several recent onsets support its phase.
        double period = 60 / bpm.Value;
        var phases = recentOnsets.Where(time => time >= end - 4).ToArray();
        beatAnchor = null;
        int support = 3;
        foreach (double phase in phases)
        {
            int matching = phases.Count(time => OnBeat(time, phase, period));
            if (matching <= support) continue;
            support = matching;
            beatAnchor = phase;
        }
    }

    static bool OnBeat(double time, double phase, double period) =>
        Math.Abs(Math.IEEERemainder(time - phase, period)) <= .14 * period;
}
