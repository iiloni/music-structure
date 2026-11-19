namespace MusicStructure;

static class MlStudy
{
    public const double Fill = 8, Swap = 16, Gain = 26, Layer = 36, Quiet = 45, Resume = 49,
        BeforeGap = 58, Gap = 60, AfterGap = 63, Duration = 72;
    static readonly double[] Fundamental = [220, 261.625565, 329.627557];
    // Different harmonic envelopes with the same amplitude-weighted harmonic centroid.
    static readonly (int Harmonic, double Weight)[] First = [(1, 1), (4, .7), (7, .6)];
    static readonly (int Harmonic, double Weight)[] Second = [(1, .5), (2, .9), (5, .65), (8, .3497596154)];

    public static IEnumerable<Packet> Packets()
    {
        double firstNorm = Math.Sqrt(First.Sum(partial => partial.Weight * partial.Weight));
        double secondNorm = Math.Sqrt(Second.Sum(partial => partial.Weight * partial.Weight));
        for (int block = 0; block < Duration * 20; block++)
        {
            double elapsed = block / 20.0;
            if (elapsed >= Gap && elapsed < AfterGap)
            {
                if (block % 5 == 0)
                    yield return new Packet(block, Math.Min(AfterGap, elapsed + .25) * 1000, [],
                        Interruption: "Simulated capture gap");
                continue;
            }

            var stereo = new short[Fixtures.BlockFrames * Packet.Channels];
            if (elapsed < Quiet || elapsed >= Resume)
            {
                for (int sample = 0; sample < Fixtures.BlockFrames; sample++)
                {
                    double time = elapsed + (double)sample / Packet.Rate;
                    double fill = Math.Clamp(Math.Min(time - Fill, Fill + 1.2 - time) / .04, 0, 1);
                    double blend = time < Resume ? Math.Max(fill, Math.Clamp((time - Swap) / .04, 0, 1)) :
                        Math.Clamp((time - BeforeGap) / .04, 0, 1);
                    double gain = time >= Gain && time < Quiet ? .45 : 1;
                    double layer = time < Quiet ? Math.Clamp((time - Layer) / (Quiet - Layer), 0, 1) : 0;
                    double signal = 0;
                    foreach (double hz in Fundamental)
                    {
                        double phase = 2 * Math.PI * hz * time;
                        double first = 0, second = 0;
                        foreach (var (harmonic, weight) in First) first += weight * Math.Sin(harmonic * phase);
                        foreach (var (harmonic, weight) in Second) second += weight * Math.Sin(harmonic * phase);
                        signal += (1 - blend) * first / firstNorm + blend * second / secondNorm;
                        signal += layer * .8 * Math.Sin(3 * phase + .5 * hz);
                    }
                    short value = (short)Math.Clamp(signal * gain * 32767 * .083, short.MinValue, short.MaxValue);
                    stereo[sample * 2] = stereo[sample * 2 + 1] = value;
                }
            }
            yield return new Packet(block, elapsed * 1000, stereo);
        }
    }
}
