namespace MusicStructure;

sealed class MusicnnInput
{
    const int SampleRate = 16000, Window = 512, Hop = 256, Steps = 187, Bands = 96, Radius = 24;
    const int Numerator = 441, Phases = 160; // 44.1 kHz / 16 kHz
    const int Samples = Window + (Steps - 1) * Hop;
    public const int PcmFrames = (Samples * Numerator + Phases - 1) / Phases;
    const double TargetRms = .1;
    readonly double[][] filters = MakeFilters();
    readonly (int Start, double[] Weights)[] mel = MakeMelBank();
    readonly double[] hann = Enumerable.Range(0, Window).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (Window - 1))).ToArray();
    readonly double[] real = new double[Window], imaginary = new double[Window], power = new double[Window / 2 + 1];
    readonly double[] cos = Enumerable.Range(0, Window / 2).Select(i => Math.Cos(-2 * Math.PI * i / Window)).ToArray();
    readonly double[] sin = Enumerable.Range(0, Window / 2).Select(i => Math.Sin(-2 * Math.PI * i / Window)).ToArray();
    readonly float[] resampled = new float[Samples], modelInput = new float[Steps * Bands];

    public float[] Prepare(float[] mono)
    {
        for (int i = 0; i < Samples; i++)
        {
            int position = i * Numerator, middle = position / Phases;
            var kernel = filters[position % Phases];
            double value = 0;
            // Zero extend the FIR at the patch boundaries. No future input is read.
            int first = Math.Max(-Radius, -middle), last = Math.Min(Radius, mono.Length - 1 - middle);
            for (int k = first; k <= last; k++) value += mono[middle + k] * kernel[k + Radius];
            resampled[i] = (float)value;
        }

        // Condition level before the model transform. A whole-patch gain factor leaves
        // gain steps inside the window, so use the level of each short spectrum instead.
        for (int step = 0; step < Steps; step++)
        {
            var frame = resampled.AsSpan(step * Hop, Window);
            double energy = 0;
            for (int i = 0; i < frame.Length; i++) energy += (double)frame[i] * frame[i];
            double gain = Math.Clamp(TargetRms / Math.Max(1e-9, Math.Sqrt(energy / Window)), .25, 4);
            Transform(frame, modelInput.AsSpan(step * Bands, Bands), gain);
        }
        return modelInput;
    }

    internal void Transform(ReadOnlySpan<float> input, Span<float> output, double gain = 1)
    {
        for (int i = 0, reverse = 0; i < Window; i++)
        {
            real[reverse] = input[i] * hann[i] * gain;
            imaginary[reverse] = 0;
            int bit = Window >> 1;
            while (bit > 0 && (reverse & bit) != 0)
            {
                reverse ^= bit;
                bit >>= 1;
            }
            reverse ^= bit;
        }

        for (int size = 2; size <= Window; size *= 2)
        for (int offset = 0; offset < Window; offset += size)
        for (int i = 0; i < size / 2; i++)
        {
            int a = offset + i, b = a + size / 2, root = i * (Window / size);
            double re = real[b] * cos[root] - imaginary[b] * sin[root];
            double im = real[b] * sin[root] + imaginary[b] * cos[root];
            real[b] = real[a] - re;
            imaginary[b] = imaginary[a] - im;
            real[a] += re;
            imaginary[a] += im;
        }
        for (int i = 0; i < power.Length; i++) power[i] = real[i] * real[i] + imaginary[i] * imaginary[i];

        for (int band = 0; band < Bands; band++)
        {
            var (start, weights) = mel[band];
            double energy = 0;
            for (int k = 0; k < weights.Length; k++) energy += power[start + k] * weights[k];
            output[band] = (float)Math.Log10(1 + 10000 * energy);
        }
    }

    static double[][] MakeFilters()
    {
        const double bandwidth = 2 * 7100.0 / Packet.Rate;
        return Enumerable.Range(0, Phases).Select(phase =>
        {
            var weights = new double[2 * Radius + 1];
            for (int k = -Radius; k <= Radius; k++)
            {
                double offset = k - (double)phase / Phases;
                double angle = Math.PI * bandwidth * offset;
                double sinc = Math.Abs(angle) < 1e-12 ? 1 : Math.Sin(angle) / angle;
                weights[k + Radius] = bandwidth * sinc * (.54 + .46 * Math.Cos(Math.PI * k / Radius));
            }
            double sum = weights.Sum();
            for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
            return weights;
        }).ToArray();
    }

    static (int, double[])[] MakeMelBank()
    {
        const double linear = 200.0 / 3, crossover = 1000.0 / linear;
        double logStep = Math.Log(6.4) / 27;
        double maximum = crossover + Math.Log((SampleRate / 2.0) / 1000) / logStep;
        var edges = Enumerable.Range(0, Bands + 2).Select(i =>
        {
            double mel = i * maximum / (Bands + 1);
            return mel < crossover ? mel * linear : 1000 * Math.Exp((mel - crossover) * logStep);
        }).ToArray();

        const double binHz = (double)SampleRate / Window;
        return Enumerable.Range(0, Bands).Select(band =>
        {
            double low = edges[band], middle = edges[band + 1], high = edges[band + 2];
            int start = (int)Math.Ceiling(low / binHz), end = Math.Min(Window / 2, (int)Math.Floor(high / binHz));
            var weights = new double[end - start + 1];
            for (int bin = start; bin <= end; bin++)
            {
                double hz = bin * binHz;
                double triangle = Math.Min((hz - low) / (middle - low), (high - hz) / (high - middle));
                weights[bin - start] = 2 * triangle / (high - low);
            }
            return (start, weights);
        }).ToArray();
    }
}
