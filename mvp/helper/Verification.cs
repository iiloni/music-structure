using System.Security.Cryptography;
using System.Text.Json;

namespace MusicStructure;

static class Verification
{
    public static int Run()
    {
        try
        {
            foreach (string fixture in Fixtures.Names)
            {
                var listen = Session.AnalyzeFixture(fixture);
                var fingerprint = Fingerprint(listen);
                Require(fingerprint == Fingerprint(Session.AnalyzeFixture(fixture)), $"Nondeterministic {fixture} replay.");
                Console.WriteLine($"{fixture}: {listen.Frames.Count} frames, {listen.Elapsed:F2}s, SHA256 {fingerprint}");
                foreach (var mark in listen.Marks)
                    Console.WriteLine($"  {mark.Kind}: {mark.Start:F2}–{mark.End:F2} (novelty {mark.Score:F2}, ΔHz {mark.DeltaHz:F0})" +
                        (mark.OtherStart is double other ? $" and {other:F2}–{mark.OtherEnd:F2}, score {mark.Score:F3}" : ""));
                switch (fixture)
                {
                    case "return":
                        var repeats = listen.Marks.Where(m => m.Kind == "repeat").ToArray();
                        Require(repeats is [{ Start: <= 3, End: >= 8, OtherStart: >= 31 and <= 35, OtherEnd: >= 40,
                            DetectedAt: <= 47 }], "Return control needs one correct A–A link.");
                        Require(listen.Marks.Count(m => m.Kind == "timbre") == 2 &&
                            listen.Marks.Any(m => m is { Kind: "timbre", Start: >= 14 and <= 18, DeltaHz: > 0 }) &&
                            listen.Marks.Any(m => m is { Kind: "timbre", Start: >= 30 and <= 34, DeltaHz: < 0 }),
                            "Return control needs the two texture changes.");
                        break;
                    case "gain":
                        Require(listen.Marks.Count == 0 && listen.Frames.Last().RmsDb - listen.Frames.First().RmsDb > 10,
                            "Gain step produced a musical event or no measured level change.");
                        break;
                    case "gap":
                        Require(listen.Marks.ToArray() is [{ Kind: "gap", Start: >= 13.99, End: >= 17.99 and <= 18.01 }],
                            "Gap control needs one explicit gap and no structural events.");
                        Require(listen.Frames.Any(f => !f.Audible && f.Valid) &&
                            listen.Frames.All(f => f.End <= 14.01 || f.Start >= 17.99),
                            "The silence and missing coverage must be distinct.");
                        break;
                }
            }

            var skipped = new Listen(0, "replay", "sequence break");
            foreach (var packet in Fixtures.Packets("gain").Take(50).Where(p => p.Sequence != 25)) skipped.Add(packet, false);
            Require(skipped.Counters.Discontinuities == 1 && skipped.Marks.ToArray() is [{ Kind: "gap" }] &&
                skipped.Frames.Select(f => f.Segment).Distinct().Count() == 2, "Sequence break did not split analysis.");
            Console.WriteLine("PASS: deterministic fixture controls and packet sequence reset.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }

    static string Fingerprint(Listen listen) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { listen.Frames, listen.Marks }, Wire.Json)));

    static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
