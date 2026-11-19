namespace MusicStructure;

static class Fixtures
{
    public static readonly string[] Names = ["return", "gain", "gap", "lanes"];
    public const int BlockFrames = Packet.Rate / 20;
    static readonly int[][] Return =
    [
        [60, 64, 67], [57, 60, 64], [62, 65, 69], [55, 59, 62],
        [64, 67, 71], [60, 65, 69], [59, 62, 66], [58, 62, 65]
    ];
    static readonly int[][] Bridge =
    [
        [54, 58, 61], [56, 59, 63], [52, 56, 59], [61, 65, 68],
        [56, 60, 63], [63, 66, 70], [57, 61, 64], [53, 57, 60]
    ];

    static readonly int[][] CProgression =
    [
        [60, 64, 67], [65, 69, 72], [67, 71, 74],
        [60, 64, 67], [57, 60, 64], [60, 64, 67]
    ];
    static readonly int[][] EFlatProgression =
    [
        [63, 67, 70], [68, 72, 75], [70, 74, 77],
        [63, 67, 70], [60, 63, 67], [63, 67, 70]
    ];

    public static IEnumerable<Packet> Packets(string name)
    {
        if (name == "lanes") return LaneStudy();
        return ExistingFixture(name);
    }

    static IEnumerable<Packet> ExistingFixture(string name)
    {
        int length = name switch { "return" => 48, "gain" => 20, "gap" => 36, _ => throw new ArgumentException("Unknown fixture.") };
        for (int block = 0; block < length * 20; block++)
        {
            double elapsed = block / 20.0;
            if (name == "gap" && elapsed >= 14 && elapsed < 18)
            {
                if (block % 5 == 0) yield return new Packet(block, elapsed * 1000 + 250, [], Interruption: "Simulated device interruption");
                continue;
            }

            bool bright = name == "return" && elapsed >= 16 && elapsed < 32;
            int chord = (int)(elapsed / 2) % Return.Length;
            int[] notes = name switch
            {
                "gain" => Return[0],
                "return" when bright => Bridge[chord],
                // The second half returns to the first harmony after a gap, an intentional guard test.
                "gap" when elapsed >= 18 => Return[(int)((elapsed - 18) / 2) % Return.Length],
                "gap" when elapsed >= 12 => Bridge[chord],
                _ => Return[chord]
            };
            double level = name == "gain" ? elapsed < 10 ? .18 : .72 : .44;
            if (name == "gap" && elapsed >= 10 && elapsed < 12) level = 0;

            short[] stereo = new short[BlockFrames * Packet.Channels];
            var frequencies = notes.Select(note => 440 * Math.Pow(2, (note - 69) / 12.0)).ToArray();
            for (int sample = 0; sample < BlockFrames; sample++)
            {
                double time = elapsed + (double)sample / Packet.Rate;
                double signal = 0;
                foreach (double hz in frequencies)
                {
                    double phase = 2 * Math.PI * hz * time;
                    signal += Math.Sin(phase) + .12 * Math.Sin(2 * phase);
                    if (bright) signal += .55 * Math.Sin(7 * phase) + .55 * Math.Sin(12 * phase) + .34 * Math.Sin(19 * phase);
                }
                short value = (short)Math.Clamp(signal * level * 32767 / 5, short.MinValue, short.MaxValue);
                stereo[sample * 2] = stereo[sample * 2 + 1] = value;
            }
            yield return new Packet(block, elapsed * 1000, stereo);
        }
    }

    static IEnumerable<Packet> LaneStudy()
    {
        for (int block = 0; block < 60 * 20; block++)
        {
            double elapsed = block / 20.0;
            if (elapsed >= 52 && elapsed < 56)
            {
                if (block % 5 == 0)
                    yield return new Packet(block, elapsed * 1000 + 250, [], Interruption: "Simulated device interruption");
                continue;
            }

            var stereo = new short[BlockFrames * Packet.Channels];
            if (elapsed >= 48 && elapsed < 52)
            {
                yield return new Packet(block, elapsed * 1000, stereo);
                continue;
            }

            double sectionStart = elapsed < 24 ? 0 : 24;
            double period = sectionStart == 0 ? .5 : 2.0 / 3;
            int[][] progression = sectionStart == 0 ? CProgression : EFlatProgression;
            int[] notes = elapsed >= 56 ? [69] : progression[(int)((elapsed - sectionStart) / 4)];
            var frequencies = notes.Select(note => 440 * Math.Pow(2, (note - 69) / 12.0)).ToArray();
            for (int sample = 0; sample < BlockFrames; sample++)
            {
                double time = elapsed + (double)sample / Packet.Rate;
                double signal = 0;
                foreach (double hz in frequencies)
                    signal += .12 * Math.Sin(2 * Math.PI * hz * time);

                double beat = (time - sectionStart) % period;
                if (elapsed < 48 && beat < .08)
                    signal += .32 * Math.Exp(-beat * 48) * Math.Sin(2 * Math.PI * 72 * beat);

                double accent = time - (sectionStart == 0 ? 10.12 : 38.12);
                if (accent is >= 0 and < .2)
                {
                    double phase = 2 * Math.PI * (850 * accent + 21_000 * accent * accent);
                    signal += .55 * Math.Sin(phase) * Math.Sin(Math.PI * accent / .2);
                }
                short value = (short)Math.Clamp(signal * 32767, short.MinValue, short.MaxValue);
                stereo[sample * 2] = stereo[sample * 2 + 1] = value;
            }
            yield return new Packet(block, elapsed * 1000, stereo);
        }
    }

}
