namespace MusicStructure;

static class Fixtures
{
    public static readonly string[] Names = ["return", "gain", "gap"];
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

    public static IEnumerable<Packet> Packets(string name)
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
}
