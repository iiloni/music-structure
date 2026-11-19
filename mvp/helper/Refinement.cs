namespace MusicStructure;

record Passage(double Start, double End);
record RefinedMap(string Version, double Start, double End, Mark[] Marks);

static class Refinement
{
    const int Phrase = 32, Window = 8, MatchLimit = 512;
    const double SimilarFrame = .84, SimilarPhrase = .91;

    record Slice(int Start, int End)
    {
        public int Length => End - Start;
    }

    record Match(Slice First, Slice Second, double Fit);
    record Boundary(double Time, double Novelty, double Raw, double Delta);

    public static RefinedMap Compute(Feature[] frames, CancellationToken stop = default)
    {
        var marks = new List<Mark>();
        var novelty = new List<Boundary>();
        var scale = TimbreScale(frames);
        int start = 0, family = 0;
        for (int end = 1; end <= frames.Length; end++)
        {
            if (end < frames.Length && frames[end].Segment == frames[end - 1].Segment &&
                frames[end].Index == frames[end - 1].Index + 1 &&
                Math.Abs(frames[end].Start - frames[end - 1].End) < .001) continue;
            stop.ThrowIfCancellationRequested();
            var continuous = frames[start..end];
            FindBoundaries(continuous, scale, novelty);
            var matches = FindMatches(continuous, stop);
            foreach (var (occurrences, fit) in Group(matches))
            {
                var spans = occurrences.Select(span => new Passage(continuous[span.Start].Start, continuous[span.End - 1].End)).ToArray();
                var label = FamilyName(family++);
                marks.Add(new Mark(-marks.Count - 1, "repeat", spans[0].Start, spans[0].End,
                    $"Repeat family {label}", spans[1].Start, spans[1].End, fit)
                {
                    Family = label,
                    Occurrences = spans
                });
            }
            start = end;
        }

        var values = novelty.Select(point => point.Novelty).Order().ToArray();
        if (values.Length > 0)
        {
            double median = values[values.Length / 2];
            double deviation = values.Select(value => Math.Abs(value - median)).Order().ElementAt(values.Length / 2);
            double threshold = Math.Max(.65, median + 3 * deviation);
            double latest = double.NegativeInfinity;
            for (int i = 3; i < novelty.Count - 3; i++)
            {
                var point = novelty[i];
                if (point.Time - latest < 3 || point.Novelty < threshold || point.Raw < 9) continue;
                var neighbors = novelty.GetRange(i - 3, 7);
                if (neighbors.Any(next => Math.Abs(next.Time - point.Time) > .751 || next.Novelty > point.Novelty) ||
                    neighbors.Count(next => next.Novelty >= threshold * .8) < 4) continue;
                latest = point.Time;
                string detail = point.Delta > 100 ? "Sustained rise in brightness" :
                    point.Delta < -100 ? "Sustained fall in brightness" : "Sustained change in spectral shape";
                marks.Add(new Mark(-marks.Count - 1, "timbre", point.Time, point.Time, detail,
                    Score: point.Raw, DeltaHz: point.Delta));
            }
        }

        return new RefinedMap("refine-1", frames.FirstOrDefault()?.Start ?? 0,
            frames.LastOrDefault()?.End ?? 0, marks.OrderBy(mark => mark.Start).ToArray());
    }

    static double[] TimbreScale(Feature[] frames)
    {
        var audible = frames.Where(frame => frame.Valid && frame.Audible).ToArray();
        if (audible.Length == 0) return [];
        var scale = new double[audible[0].Mel.Length];
        for (int band = 0; band < scale.Length; band++)
        {
            var values = audible.Select(frame => frame.Mel[band]).Order().ToArray();
            scale[band] = Math.Clamp(values[(values.Length - 1) * 3 / 4] - values[(values.Length - 1) / 4], 6, 18);
        }
        return scale;
    }

    static void FindBoundaries(Feature[] frames, double[] scale, List<Boundary> output)
    {
        for (int pivot = Window; pivot <= frames.Length - Window; pivot++)
        {
            double normalized = 0, raw = 0, delta = 0;
            bool valid = true;
            for (int k = 0; k < Window; k++)
            {
                if (!Usable(frames[pivot + k]) || !Usable(frames[pivot - Window + k])) valid = false;
                delta += (frames[pivot + k].CentroidHz - frames[pivot - Window + k].CentroidHz) / Window;
            }
            if (!valid) continue;
            for (int band = 0; band < scale.Length; band++)
            {
                double difference = 0;
                for (int k = 0; k < Window; k++)
                    difference += (frames[pivot + k].Mel[band] - frames[pivot - Window + k].Mel[band]) / Window;
                raw += difference * difference;
                double scaled = difference / scale[band];
                normalized += scaled * scaled;
            }
            output.Add(new Boundary(frames[pivot].Start, Math.Sqrt(normalized / scale.Length),
                Math.Sqrt(raw / scale.Length), delta));
        }
    }

    static Match[] FindMatches(Feature[] frames, CancellationToken stop)
    {
        // Scan each diagonal without retaining an all-pairs matrix. Keep the strongest candidates.
        var matches = new PriorityQueue<Match, (double Strength, int First, int Second)>();
        for (int lag = Phrase; lag <= frames.Length - Phrase; lag++)
        {
            stop.ThrowIfCancellationRequested();
            int begin = -1, last = -1, count = 0;
            double strength = 0, pending = 0;
            for (int i = 0; i < frames.Length - lag; i++)
            {
                bool usable = Usable(frames[i]) && Usable(frames[i + lag]);
                double fit = usable ? Structure.ChromaSimilarity(frames[i], frames[i + lag]) : 0;
                if (fit >= SimilarFrame)
                {
                    if (begin < 0) begin = i;
                    last = i;
                    strength += fit + pending;
                    pending = 0;
                    count++;
                }
                else if (usable && begin >= 0 && i - last <= 2) pending += fit;
                else FinishMatch();
            }
            FinishMatch();

            void FinishMatch()
            {
                int length = last - begin + 1;
                if (begin >= 0 && length >= Phrase && length <= lag &&
                    count >= length * .8 && strength / length >= SimilarPhrase &&
                    HasMovement(frames, begin) && HasMovement(frames, begin + lag))
                {
                    double fit = Math.Min(1, strength / length);
                    var match = new Match(new Slice(begin, last + 1), new Slice(begin + lag, last + lag + 1), fit);
                    matches.Enqueue(match, (length * Math.Pow(fit, 4), -begin, -begin - lag));
                    if (matches.Count > MatchLimit) matches.Dequeue();
                }
                begin = last = -1;
                count = 0;
                strength = pending = 0;
            }
        }

        // A shifted candidate with nearly the same strength is an ambiguous alignment.
        return matches.UnorderedItems.Select(item => item.Element)
            .Where(match => HasContrast(frames, match))
            .OrderByDescending(match => match.First.Length * Math.Pow(match.Fit, 4))
            .ThenBy(match => match.First.Start).ThenBy(match => match.Second.Start).ToArray();
    }

    static bool HasContrast(Feature[] frames, Match match)
    {
        for (int shift = -12; shift <= 12; shift += 6)
        {
            if (shift == 0) continue;
            int alternate = match.Second.Start + shift;
            if (alternate < match.First.End || alternate + Phrase > frames.Length) continue;
            if (match.Fit - MeanFit(frames, match.First.Start, alternate, Phrase) < .045) return false;
        }
        return true;
    }

    static bool HasMovement(Feature[] frames, int start)
    {
        Span<double> mean = stackalloc double[12];
        mean.Clear();
        for (int k = start; k < start + Phrase; k++)
        {
            if (!Usable(frames[k])) return false;
            for (int p = 0; p < 12; p++) mean[p] += frames[k].Chroma[p] / Phrase;
        }
        double stationary = 0;
        foreach (double pitch in mean) stationary += pitch * pitch;
        if (stationary > .88) return false;
        for (int lag = 4; lag <= 12; lag++)
            if (MeanFit(frames, start, start + lag, Phrase - lag) >= .94) return false;
        return true;
    }

    static double MeanFit(Feature[] frames, int first, int second, int length)
    {
        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            if (!Usable(frames[first + i]) || !Usable(frames[second + i])) return 0;
            sum += Structure.ChromaSimilarity(frames[first + i], frames[second + i]);
        }
        return sum / length;
    }

    static bool Usable(Feature frame) => frame.Valid && frame.Audible;

    static IEnumerable<(Slice[] Occurrences, double Fit)> Group(Match[] matches)
    {
        var claimed = new List<Slice>();
        foreach (var seed in matches)
        {
            if (claimed.Any(span => Conflicts(span, seed.First) || Conflicts(span, seed.Second))) continue;
            var occurrences = new List<Slice> { seed.First, seed.Second };
            double fit = seed.Fit;
            foreach (var match in matches)
            {
                Slice? addition = null;
                if (occurrences.Any(span => SamePassage(span, match.First))) addition = match.Second;
                else if (occurrences.Any(span => SamePassage(span, match.Second))) addition = match.First;
                if (addition is not { } candidate ||
                    occurrences.Any(span => Overlap(span, candidate) > 0) ||
                    claimed.Any(span => Conflicts(span, candidate))) continue;
                occurrences.Add(candidate);
                fit = Math.Min(fit, match.Fit);
            }
            claimed.AddRange(occurrences);
            yield return (occurrences.OrderBy(span => span.Start).ToArray(), fit);
        }
    }

    static bool SamePassage(Slice a, Slice b) => Overlap(a, b) >= .75 * Math.Max(a.Length, b.Length);
    static bool Conflicts(Slice a, Slice b) => Overlap(a, b) > .25 * Math.Min(a.Length, b.Length);
    static int Overlap(Slice a, Slice b) => Math.Max(0, Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start));

    static string FamilyName(int index)
    {
        string label = "";
        do
        {
            label = (char)('A' + index % 26) + label;
            index = index / 26 - 1;
        } while (index >= 0);
        return label;
    }
}
