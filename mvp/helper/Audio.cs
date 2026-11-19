using System.Diagnostics;

namespace MusicStructure;

static class Clock
{
    public static double Milliseconds => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);
}

record Packet(long Sequence, double StartMs, short[] Samples, uint Flags = 0, string? Interruption = null, double? PacketReadMs = null)
{
    public const int Rate = 44100;
    public const int Channels = 2;
    public int Frames => Samples.Length / Channels;
    public bool IsGap => Interruption is not null;
}

record Feature(long Index, double Start, double End, double? ClockEndMs, int Segment,
    double RmsDb, double CentroidHz, bool Valid, bool Audible, double[] Mel, double[] Chroma)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? PacketReadMs { get; init; }
}

sealed class AudioFeatures
{
    public const int Rate = 22050;
    const int Size = 2048, Hop = 512, Bands = 24, Taps = 31;
    const double SilenceDb = -58;
    readonly double[] lowpass = new double[Taps], input = new double[Taps], ring = new double[Size];
    readonly double[] real = new double[Size], imag = new double[Size], hann = new double[Size];
    readonly double[] cos = new double[Size / 2], sin = new double[Size / 2];
    readonly int[] reverse = new int[Size], melBin = new int[Size / 2], pitch = new int[Size / 2];
    readonly double[] melFraction = new double[Size / 2], pitchFraction = new double[Size / 2];
    readonly double[] mel = new double[Bands], chroma = new double[12], powers = new double[Size / 2];
    readonly double[] bandSum = new double[Bands], pitchSum = new double[12];
    int sourceCount, samples, observations, audible, batch = 1;
    double sumPower, sumCentroid;

    public AudioFeatures()
    {
        for (int k = 0; k < Taps; k++)
        {
            int d = k - Taps / 2;
            lowpass[k] = (d == 0 ? .46 : Math.Sin(Math.PI * .46 * d) / (Math.PI * d))
                * (.54 - .46 * Math.Cos(2 * Math.PI * k / (Taps - 1)));
        }
        double norm = lowpass.Sum();
        for (int k = 0; k < Taps; k++) lowpass[k] /= norm;

        for (int k = 0; k < Size; k++)
        {
            hann[k] = .5 - .5 * Math.Cos(2 * Math.PI * k / (Size - 1));
            reverse[k] = (reverse[k / 2] / 2) + (k % 2) * (Size / 2);
        }
        for (int k = 1; k < Size / 2; k++)
        {
            double hz = (double)k * Rate / Size;
            double band = (Bands + 1) * Math.Log(1 + hz / 700) / Math.Log(1 + (Rate / 2.0) / 700);
            melBin[k] = (int)band;
            melFraction[k] = band - melBin[k];
            double note = 69 + 12 * Math.Log2(hz / 440);
            int nearest = (int)Math.Floor(note);
            pitch[k] = (nearest % 12 + 12) % 12;
            pitchFraction[k] = note - nearest;
        }
        for (int k = 0; k < Size / 2; k++)
        {
            cos[k] = Math.Cos(-2 * Math.PI * k / Size);
            sin[k] = Math.Sin(-2 * Math.PI * k / Size);
        }
    }

    public void Reset()
    {
        Array.Clear(input);
        sourceCount = samples = 0;
        batch = 1;
        ClearBatch();
    }

    void ClearBatch()
    {
        Array.Clear(bandSum);
        Array.Clear(pitchSum);
        observations = audible = 0;
        sumPower = sumCentroid = 0;
    }

    public void Add(short[] stereo, Action<double, double, double, double, bool, double[], double[]> emit)
    {
        for (int i = 0; i < stereo.Length; i += Packet.Channels)
        {
            // Equal L/R weights, followed by a low-pass FIR and decimation by two.
            input[sourceCount % Taps] = (stereo[i] * .5 + stereo[i + 1] * .5) / 32768;
            sourceCount++;
            if (sourceCount % 2 != 0) continue;

            double value = 0;
            int at = sourceCount % Taps;
            for (int k = 0; k < Taps; k++)
            {
                if (--at < 0) at = Taps - 1;
                value += input[at] * lowpass[k];
            }
            ring[samples % Size] = value;
            samples++;
            if (samples >= Size && (samples - Size) % Hop == 0) Observe();

            if (samples < ((long)batch * Rate + 3) / 4) continue;
            bool hasMusic = audible > 0 && audible * 2 >= observations;
            double rmsDb = 10 * Math.Log10(Math.Max(1e-12, sumPower / Math.Max(1, observations)));
            double[] shape = bandSum.Select(x => x / Math.Max(1, audible)).ToArray();
            double[] harmony = (double[])pitchSum.Clone();
            Normalize(harmony);
            emit((batch - 1) / 4.0, batch / 4.0, rmsDb, sumCentroid / Math.Max(1, audible), hasMusic, shape, harmony);
            batch++;
            ClearBatch();
        }
    }

    void Observe()
    {
        double energy = 0;
        for (int k = 0; k < Size; k++)
        {
            double value = ring[(samples + k) % Size];
            energy += value * value;
            real[reverse[k]] = value * hann[k];
            imag[reverse[k]] = 0;
        }
        energy /= Size;
        observations++;
        sumPower += energy;
        if (energy < Math.Pow(10, SilenceDb / 10)) return;
        audible++;

        for (int length = 2; length <= Size; length *= 2)
        {
            int half = length / 2;
            for (int offset = 0; offset < Size; offset += length)
            for (int k = 0; k < half; k++)
            {
                int a = offset + k, b = a + half, root = k * (Size / length);
                double re = real[b] * cos[root] - imag[b] * sin[root];
                double im = real[b] * sin[root] + imag[b] * cos[root];
                real[b] = real[a] - re;
                imag[b] = imag[a] - im;
                real[a] += re;
                imag[a] += im;
            }
        }

        Array.Clear(mel);
        Array.Clear(chroma);
        double mass = 0, moment = 0, totalPower = 0;
        for (int k = 1; k < Size / 2; k++)
        {
            double power = real[k] * real[k] + imag[k] * imag[k];
            powers[k] = power;
            totalPower += power;
            double amplitude = Math.Sqrt(power);
            mass += amplitude;
            moment += k * amplitude;
            int band = melBin[k];
            if (band >= 1) mel[band - 1] += (1 - melFraction[k]) * power;
            if (band < Bands) mel[band] += melFraction[k] * power;
        }
        sumCentroid += moment / Math.Max(mass, 1e-12) * Rate / Size;
        for (int k = 0; k < Bands; k++)
            bandSum[k] += 10 * Math.Log10(Math.Max(1e-4, mel[k] / Math.Max(totalPower, 1e-12)));

        // Spectral peaks in the lower registers reduce leakage and upper harmonic bias.
        for (int k = 6; k <= 140; k++)
        {
            if (powers[k] <= powers[k - 1] || powers[k] < powers[k + 1]) continue;
            double weight = powers[k] / Math.Sqrt(k);
            chroma[pitch[k]] += weight * (1 - pitchFraction[k]);
            chroma[(pitch[k] + 1) % 12] += weight * pitchFraction[k];
        }
        Normalize(chroma);
        for (int k = 0; k < 12; k++) pitchSum[k] += chroma[k];
    }

    static void Normalize(double[] values)
    {
        double norm = Math.Sqrt(values.Sum(x => x * x));
        if (norm <= 0) return;
        for (int k = 0; k < values.Length; k++) values[k] /= norm;
    }
}
