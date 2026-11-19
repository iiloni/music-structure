namespace MusicStructure;

record ChordEvidence(double[] Notes, double[] Bass, int Observations, int Supported);

// A separate pitch estimate for chord labels. Repeat and key detection use the existing chroma.
sealed class ChordAnalysis(int rate, int fftSize)
{
    const int FirstBin = 5, LastBin = 170;
    readonly double[] peaks = new double[LastBin + 2], amplitudes = new double[LastBin + 2];
    readonly double[] notes = new double[12], noteSum = new double[12], bassSum = new double[12];
    int observations, supported;
    static readonly double[] Partials = [0, 0, .60, .33, .22, .17, .13];
    const double ThirdWeight = .95, FifthWeight = .90, SeventhWeight = .78;
    static readonly double TriadNorm = Math.Sqrt(1 + ThirdWeight * ThirdWeight + FifthWeight * FifthWeight);
    static readonly double SeventhNorm = Math.Sqrt(TriadNorm * TriadNorm + SeventhWeight * SeventhWeight);
    static readonly (int Third, int Seventh, string Suffix)[] Types =
        [(4, 0, ""), (3, 0, "m"), (4, 10, "7"), (4, 11, "maj7"), (3, 10, "m7")];

    public void Observe(double[] power)
    {
        double maximum = 0;
        Array.Clear(amplitudes);
        for (int bin = FirstBin; bin <= LastBin; bin++)
        {
            if (power[bin] <= power[bin - 1] || power[bin] < power[bin + 1]) continue;
            double left = Math.Log(Math.Max(1e-12, power[bin - 1]));
            double center = Math.Log(Math.Max(1e-12, power[bin]));
            double right = Math.Log(Math.Max(1e-12, power[bin + 1]));
            double curvature = left - 2 * center + right;
            double offset = curvature < -1e-9 ? .5 * (left - right) / curvature : 0;
            peaks[bin] = bin + Math.Clamp(offset, -.5, .5);
            amplitudes[bin] = Math.Sqrt(power[bin]);
            maximum = Math.Max(maximum, amplitudes[bin]);
        }
        observations++;
        if (maximum <= 0) return;

        int bass = -1;
        uint independentPitches = 0;
        Array.Clear(notes);
        for (int bin = FirstBin; bin <= LastBin; bin++)
        {
            if (amplitudes[bin] < maximum * .06) continue;
            double hz = peaks[bin] * rate / fftSize;
            double midi = 69 + 12 * Math.Log2(hz / 440);
            int nearest = (int)Math.Round(midi);
            double detuning = Math.Abs(midi - nearest);
            if (detuning > .44) continue;
            int pitch = (nearest % 12 + 12) % 12;
            if (hz < 165 && bass < 0 && amplitudes[bin] >= maximum * .20) bass = pitch;
            if (hz < 130) continue;

            // A strong fundamental explains some of its upper partials. Retain any excess
            // so an independent note at the same frequency can still influence the chord.
            double expected = 0;
            for (int harmonic = 2; harmonic < Partials.Length; harmonic++)
            {
                double fundamental = peaks[bin] / harmonic;
                int near = (int)Math.Round(fundamental);
                for (int match = Math.Max(FirstBin, near - 1); match <= near + 1; match++)
                {
                    if (amplitudes[match] < maximum * .06 || Math.Abs(peaks[match] - fundamental) > .30) continue;
                    expected = Math.Max(expected, amplitudes[match] * Partials[harmonic]);
                }
            }
            double independent = (amplitudes[bin] - expected) / maximum;
            if (independent >= .06) independentPitches |= 1u << pitch;
            // When there is independent evidence of polyphony, retain weaker notes that
            // coincide with a bass partial instead of deleting them from the chord.
            double evidence = Math.Max(independent, .35 * amplitudes[bin] / maximum);
            if (evidence >= .045) notes[pitch] += Math.Sqrt(evidence) * (1 - detuning);
        }
        double sum = notes.Sum();
        if (sum > 0 && System.Numerics.BitOperations.PopCount(independentPitches) >= (bass >= 0 ? 2 : 3))
        {
            supported++;
            for (int i = 0; i < 12; i++) noteSum[i] += notes[i] / sum;
        }
        if (bass >= 0) bassSum[bass]++;
    }

    public ChordEvidence Take()
    {
        var evidence = new ChordEvidence((double[])noteSum.Clone(), (double[])bassSum.Clone(), observations, supported);
        Reset();
        return evidence;
    }

    public void Reset()
    {
        Array.Clear(noteSum);
        Array.Clear(bassSum);
        observations = supported = 0;
    }

    public static PitchEstimate? Match(IEnumerable<ChordEvidence> window, string[] names)
    {
        var notes = new double[12];
        var bass = new double[12];
        int observations = 0, supported = 0;
        foreach (var evidence in window)
        {
            observations += evidence.Observations;
            supported += evidence.Supported;
            for (int i = 0; i < 12; i++)
            {
                notes[i] += evidence.Notes[i];
                bass[i] += evidence.Bass[i];
            }
        }
        if (supported < .55 * observations) return null;
        double noteTotal = notes.Sum(), bassTotal = bass.Sum();
        if (noteTotal <= 0) return null;
        for (int i = 0; i < 12; i++)
        {
            bass[i] /= Math.Max(1, bassTotal);
            notes[i] = notes[i] / noteTotal + .12 * bass[i];
        }

        double norm = Math.Sqrt(notes.Sum(value => value * value));
        double best = 0, next = 0, bestFit = 0;
        string? label = null;
        for (int root = 0; root < 12; root++)
        {
            foreach (var (third, seventh, suffix) in Types)
            {
                double a = notes[root], b = notes[(root + third) % 12], c = notes[(root + 7) % 12];
                double d = seventh == 0 ? 0 : notes[(root + seventh) % 12];
                if (Math.Min(a, Math.Min(b, c)) < .095 ||
                    (seventh != 0 && d < Math.Max(.115, .55 * Math.Min(a, Math.Min(b, c))))) continue;
                if (a + b + c + d < .70) continue; // Enough evidence must be accounted for by the chord.
                double fit = (a + ThirdWeight * b + FifthWeight * c + SeventhWeight * d) /
                    (norm * (seventh == 0 ? TriadNorm : SeventhNorm));
                double rank = fit + .12 * bass[root] - (seventh == 0 ? 0 : .025);
                if (rank > best)
                {
                    next = best;
                    best = rank;
                    bestFit = fit;
                    label = names[root] + suffix;
                }
                else next = Math.Max(next, rank);
            }
        }

        if (label is null || bestFit < .70 || best - next < .016) return null;
        return new PitchEstimate(label, Math.Min(1, bestFit), bestFit < .84 || best - next < .055);
    }
}
