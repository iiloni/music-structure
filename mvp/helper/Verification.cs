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
                    case "refinement":
                        CheckRefinement(listen, fingerprint);
                        break;
                    case "chords":
                        CheckChords(listen);
                        break;
                    case "lanes":
                        CheckLanes(listen);
                        break;
                    case "gain":
                        Require(listen.Marks.Count == 0 && listen.Frames.Last().RmsDb - listen.Frames.First().RmsDb > 10,
                            "Gain step produced a musical event or no measured level change.");
                        Require(listen.Frames.All(f => f.Lanes!.Bpm is null && f.Lanes.Key is null),
                            "A single held chord and gain step established tempo or key.");
                        Require(Refinement.Compute(listen.Frames.ToArray()).Marks.Length == 0,
                            "Refinement marked the gain step or held chord.");
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
            var delayed = new Listen(0, "fixture", "timing probe");
            foreach (var packet in Fixtures.Packets("gain").Take(6))
                delayed.Add(packet with { PacketReadMs = packet.StartMs + 2_050 }, true);
            var timedFrame = delayed.Frames.Single();
            Require(timedFrame.ClockEndMs == 250 && timedFrame.PacketReadMs == 2_300,
                "Packet read time failed to preserve the simulated capture delay.");
            Console.WriteLine("PASS: deterministic fixture controls, packet sequence reset, timing probe and refinement.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }

    static void CheckChords(Listen listen)
    {
        string[] expected = ["C", "Am7", "Fmaj7", "G7", "D", "Bm"];
        int correct = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int start = i * 4;
            var frames = listen.Frames.Where(frame => frame.Start >= start && frame.End <= start + 4).ToArray();
            correct += frames.Count(frame => frame.Lanes!.Chord?.Label == expected[i]);
            Require(frames.Where(frame => frame.Start >= start + .75)
                    .All(frame => frame.Lanes!.Chord?.Label == expected[i]),
                $"Expected stable {expected[i]} after the chord transition at {start}s.");
        }

        Require(listen.Frames.Where(frame => frame.Start >= 24).All(frame => frame.Lanes!.Chord is null),
            "Silence, the lone bass note or the ambiguous cluster received a chord label.");
        Console.WriteLine($"  {correct}/96 chord-section frames correct, including changes; 48/48 control frames unknown.");
    }

    static void CheckRefinement(Listen listen, string original)
    {
        var frames = listen.Frames.ToArray();
        var refined = Refinement.Compute(frames);
        Require(JsonSerializer.Serialize(refined, Wire.Json) ==
            JsonSerializer.Serialize(Refinement.Compute(frames), Wire.Json), "Refinement was nondeterministic.");
        Require(Fingerprint(listen) == original, "Refinement modified the original listen.");
        var families = refined.Marks.Where(mark => mark.Kind == "repeat").ToArray();
        Require(families is [{ Family: "A", Occurrences: { Length: 3 } }], "Expected one family with three occurrences.");
        var occurrences = families.Single().Occurrences!;
        foreach (var (span, start, end) in occurrences.Zip(new[] { (0.0, 12.0), (22.5, 34.5), (46.5, 58.5) },
            (span, expected) => (span, expected.Item1, expected.Item2)))
        {
            Require(Math.Abs(span.Start - start) <= .5 && Math.Abs(span.End - end) <= .5,
                "A refined repeat endpoint missed the planted passage by more than 0.5 s.");
        }
        Require(occurrences.All(span => span.End <= 58.5) &&
            listen.Marks.Any(mark => mark is { Kind: "gap", Start: 58.5, End: 62.5 }),
            "Refinement used the planted repeat after the gap.");
        var boundaries = refined.Marks.Where(mark => mark.Kind == "timbre").Select(mark => mark.Start).ToArray();
        Require(boundaries.SequenceEqual(new[] { 12.0, 22.5, 34.5, 46.5 }),
            "Refinement did not find the four planted texture boundaries.");
        Require(listen.Marks.Count(mark => mark.Kind == "timbre") < boundaries.Length,
            "The refinement fixture needs a boundary missed by the original pass.");
        Console.WriteLine("  refined A: " + string.Join(", ", occurrences.Select(span => $"{span.Start:F2}–{span.End:F2}s")));
        Console.WriteLine("  refined texture boundaries: " + string.Join(", ", boundaries.Select(time => $"{time:F2}s")));
    }

    static void CheckLanes(Listen listen)
    {
        var events = listen.Marks.Where(mark => mark.Kind == "event").ToArray();
        Require(events is [{ Start: 10, End: 10.5 }, { Start: 38, End: 38.5 }],
            "Each short accent should produce one event, without marking the regular beats.");
        Require(listen.Frames.Where(f => f.Start >= 7 && f.End <= 23)
            .All(f => f.Lanes!.Bpm is >= 118 and <= 122), "A short accent perturbed the established 120 BPM pulse.");
        foreach (var (start, end, pace, key) in new[] { (14, 23, 120, "C major"), (34, 44, 90, "E♭ major") })
        {
            var frames = listen.Frames.Where(f => f.Start >= start && f.End <= end).ToArray();
            Require(frames.Count(f => f.Lanes!.Bpm is double bpm && Math.Abs(pace - bpm) <= 2) >= frames.Length * .8,
                $"Expected a stable {pace} BPM at {start}–{end}s.");
            Require(frames.Count(f => f.Lanes!.Key?.Label == key) >= frames.Length * .75,
                $"Expected {key} tonal evidence at {start}–{end}s.");
            var beats = frames.SelectMany(f => f.Lanes!.Beats).ToArray();
            Require(beats.Length >= (end - start) * pace / 60 * .65, $"Missing beat marks at {pace} BPM.");
            Require(beats.Count(beat => Math.Abs(Math.IEEERemainder(beat, 60.0 / pace)) < .08) >= beats.Length * .85,
                $"Beat phase did not agree with the {pace} BPM pulse.");
            Console.WriteLine($"  {start}–{end}s: {pace} BPM, {key}, {beats.Length} beat marks.");
        }
        foreach (var (start, chord) in new[] { (2, "C"), (6, "F"), (10, "G"), (18, "Am"),
                     (26, "E♭"), (30, "A♭"), (34, "B♭"), (42, "Cm") })
        {
            Require(listen.Frames.Single(f => f.Start == start).Lanes!.Chord?.Label == chord,
                $"Expected chord {chord} at {start}s.");
        }

        Require(listen.Marks.Any(mark => mark is { Kind: "gap", Start: >= 51.99, End: >= 55.99 and <= 56.01 }) &&
            listen.Frames.Where(f => f.Start >= 48).All(f =>
                f.Lanes is { Chord: null, Key: null, Bpm: null } && f.Lanes.Beats.Length == 0),
            "Estimates should be unknown during silence and after a gap into a single note.");
    }

    static string Fingerprint(Listen listen) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { listen.Frames, listen.Marks }, Wire.Json)));

    static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
