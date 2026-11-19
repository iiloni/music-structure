namespace MusicStructure;

record PitchEstimate(string Label, double Fit);
record LaneFrame(PitchEstimate? Chord, PitchEstimate? Key, double? Bpm, double? TempoFit, double[] Onsets, double[] Beats);

sealed class AdditionalLanes(Action<Mark> publish, Func<int> nextId)
{
    static readonly string[] PitchNames = ["C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];
    static readonly double[][] Chords =
    [
        [1, 0, 0, 0, .9, 0, 0, .9, 0, 0, 0, 0],
        [1, 0, 0, .9, 0, 0, 0, .9, 0, 0, 0, 0]
    ];
    static readonly double[][] Keys =
    [
        [1, 0, .43, 0, .68, .46, 0, .8, 0, .48, 0, .32],
        [1, 0, .40, .68, 0, .47, 0, .8, .45, 0, .29, .25]
    ];
    const int KeyFrames = 48, EventBaseline = 6;
    readonly Queue<Feature> recent = new();
    readonly PulseTempo tempo = new();
    PitchEstimate? chord, key;
    string? chordCandidate, keyCandidate;
    int chordRun, keyRun;
    double lastEvent = double.NegativeInfinity;

    public void Reset()
    {
        recent.Clear();
        tempo.Reset();
        chord = key = null;
        chordCandidate = keyCandidate = null;
        chordRun = keyRun = 0;
        lastEvent = double.NegativeInfinity;
    }

    public void ObservePulse(double time, double flux) => tempo.Observe(time, flux);

    public LaneFrame Add(Feature frame, double segmentStart)
    {
        recent.Enqueue(frame);
        if (recent.Count > KeyFrames) recent.Dequeue();
        var history = recent.ToArray();

        var chordFit = frame.Audible ? Match(history.TakeLast(2), Chords, false, .72, .075) : null;
        Sustain(chordFit, 2, ref chord, ref chordCandidate, ref chordRun);
        if (!frame.Audible)
        {
            chord = null;
            chordCandidate = null;
            chordRun = 0;
        }

        if (history.Length == KeyFrames)
        {
            var keyFit = Match(history, Keys, true, .68, .028);
            Sustain(keyFit, 4, ref key, ref keyCandidate, ref keyRun);
        }

        DetectEvent(history, frame.End);
        var rhythm = tempo.Read(frame.End - segmentStart, frame.Audible);
        return new LaneFrame(chord, frame.Audible ? key : null, rhythm.Bpm, rhythm.Fit,
            rhythm.Onsets.Select(time => time + segmentStart).ToArray(),
            rhythm.Beats.Select(time => time + segmentStart).ToArray());
    }

    static void Sustain(PitchEstimate? estimate, int required, ref PitchEstimate? current, ref string? candidate, ref int run)
    {
        if (estimate?.Label != candidate)
        {
            candidate = estimate?.Label;
            run = 0;
        }
        if (++run >= required) current = estimate;
    }

    static PitchEstimate? Match(IEnumerable<Feature> window, double[][] profiles, bool isKey, double minimumFit, double margin)
    {
        var mean = new double[12];
        int total = 0, audible = 0;
        foreach (var frame in window)
        {
            total++;
            if (!frame.Audible) continue;
            audible++;
            for (int i = 0; i < 12; i++) mean[i] += frame.Chroma[i];
        }
        double norm = Math.Sqrt(mean.Sum(v => v * v));
        if (audible < total * .75 || norm <= 0) return null;
        if (isKey && mean.Count(v => v >= .16 * norm) < 5) return null;

        double best = 0, runnerUp = 0;
        int bestRoot = 0, bestMode = 0;
        for (int mode = 0; mode < 2; mode++)
        {
            var profile = profiles[mode];
            double scale = norm * Math.Sqrt(profile.Sum(v => v * v));
            for (int root = 0; root < 12; root++)
            {
                double dot = 0;
                for (int i = 0; i < 12; i++) dot += mean[(i + root) % 12] * profile[i];
                double fit = dot / scale;
                if (fit > best)
                {
                    runnerUp = best;
                    best = fit;
                    bestRoot = root;
                    bestMode = mode;
                }
                else runnerUp = Math.Max(fit, runnerUp);
            }
        }

        if (best < minimumFit || best - runnerUp < margin) return null;
        string suffix = isKey ? (bestMode == 0 ? " major" : " minor") : (bestMode == 0 ? "" : "m");
        return new PitchEstimate(PitchNames[bestRoot] + suffix, Math.Min(best, 1));
    }

    void DetectEvent(Feature[] frames, double detectedAt)
    {
        // Compare short excursions against a stable baseline and require a return.
        // Consider the widest candidate first so adjacent high bins form one event.
        for (int count = 3; count >= 1; count--)
        {
            int start = frames.Length - 2 - count;
            if (start < EventBaseline || frames[start].Start - lastEvent < 1.5) continue;
            var baseline = frames[(start - EventBaseline)..start];
            if (baseline.Any(frame => !frame.Audible) || !frames[^1].Audible || !frames[^2].Audible) continue;
            var mean = new double[baseline[0].Mel.Length];
            foreach (var reference in baseline)
                for (int k = 0; k < mean.Length; k++) mean[k] += reference.Mel[k] / EventBaseline;
            double Distance(Feature point)
            {
                double sum = 0;
                for (int k = 0; k < mean.Length; k++) sum += Math.Pow(point.Mel[k] - mean[k], 2);
                return Math.Sqrt(sum / mean.Length);
            }

            double referenceMax = baseline.Max(Distance);
            if (referenceMax > 6 || Distance(frames[^1]) > Math.Max(4, referenceMax * 1.5) ||
                Distance(frames[^2]) > Math.Max(4, referenceMax * 1.5)) continue;
            double peak = 0;
            int changed = 0;
            for (int i = start; i < start + count; i++)
            {
                if (!frames[i].Audible) break;
                double distance = Distance(frames[i]);
                if (distance >= Math.Max(3.5, referenceMax * 1.8)) changed++;
                peak = Math.Max(peak, distance);
            }
            if (changed != count || peak < Math.Max(5, referenceMax * 2.4)) continue;
            lastEvent = frames[start].Start;
            publish(new Mark(nextId(), "event", lastEvent, frames[start + count - 1].End,
                "Brief change in spectral shape", Score: peak, DetectedAt: detectedAt));
            return;
        }
    }
}
