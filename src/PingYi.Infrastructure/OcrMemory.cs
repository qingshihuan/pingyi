using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PingYi.Core;

namespace PingYi.Infrastructure;

public static class OcrMemory
{
    public static SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = RuntimePolicy.OcrThreadCount(Environment.ProcessorCount),
            InterOpNumThreads = 1
        };
        try
        {
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    /// <summary>Borrow dense output memory only while its ONNX output owner is alive.</summary>
    public static ReadOnlySpan<float> ReadValues(Tensor<float> tensor) =>
        tensor is DenseTensor<float> dense && !dense.IsReversedStride ? dense.Buffer.Span : tensor.ToArray();
}
