using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MusicStructure;

sealed class Musicnn : IDisposable
{
    public const string FileName = "msd-musicnn-1.onnx";
    public const string Hash = "49668ffec47e52e94b96f45930bb46a28a1368d4bdfb5c05378fa834aca616e1";
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, FileName);
    readonly InferenceSession session;

    public Musicnn(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"Install {FileName} beside the helper to enable ML.");
        using var file = File.OpenRead(path);
        if (file.Length > 4_000_000)
            throw new InvalidOperationException("MusiCNN weights do not match the pinned model. Re-run Install-MLModel.ps1.");
        var weights = new byte[(int)file.Length];
        file.ReadExactly(weights);
        if (!Convert.ToHexStringLower(SHA256.HashData(weights)).Equals(Hash, StringComparison.Ordinal))
            throw new InvalidOperationException("MusiCNN weights do not match the pinned model. Re-run Install-MLModel.ps1.");
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        session = new InferenceSession(weights, options);
    }

    public float[] Embed(float[] mel)
    {
        var input = NamedOnnxValue.CreateFromTensor("melspectrogram", new DenseTensor<float>(mel, [1, 187, 96]));
        using var output = session.Run([input], ["embeddings"]);
        var values = output.Single().AsTensor<float>().ToArray();
        double norm = Math.Sqrt(values.Sum(x => (double)x * x));
        if (values.Length != 200 || !double.IsFinite(norm) || norm <= 0)
            throw new InvalidOperationException("MusiCNN produced an invalid embedding.");
        for (int i = 0; i < values.Length; i++) values[i] = (float)(values[i] / norm);
        return values;
    }

    public void Dispose() => session.Dispose();
}
