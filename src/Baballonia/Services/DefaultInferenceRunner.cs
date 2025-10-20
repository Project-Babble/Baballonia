using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Services;

public class DefaultInferenceRunner(ILoggerFactory loggerFactory) : IInferenceRunner
{
    private ILogger _logger;
    private string _inputName;
    private InferenceSession _session;
    public DenseTensor<float> InputTensor;
    public Size InputSize { get; private set; }


    /// <summary>
    /// Loads/reloads the ONNX model and setups the environment
    /// </summary>
    public void Setup(string modelPath, bool useGpu = true)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"{modelPath} does not exist");

        _logger = loggerFactory.CreateLogger(this.GetType().Name + "." + Path.GetFileName(modelPath));

        SessionOptions sessionOptions = SetupSessionOptions();
        if (useGpu)
            ConfigurePlatformSpecificGpu(sessionOptions, modelPath);
        else
            sessionOptions.AppendExecutionProvider_CPU();

        _session = new InferenceSession(modelPath, sessionOptions);
        _inputName = _session.InputMetadata.Keys.First();
        var dimensions = _session.InputMetadata.Values.First().Dimensions;
        InputSize = new Size(dimensions[2], dimensions[3]);

        InputTensor = new DenseTensor<float>([1, dimensions[1], dimensions[2], dimensions[3]]);

        _logger.LogInformation("{} initialization finished", modelPath);
    }

    /// <summary>
    /// Per-platform hardware accel. detection/activation
    /// </summary>
    /// <param name="sessionOptions"></param>
    /// <param name="modelName"></param>
    private void ConfigurePlatformSpecificGpu(SessionOptions sessionOptions, string modelName)
    {
        // "The Android Neural Networks API (NNAPI) is an Android C API designed for
        // running computationally intensive operations for machine learning on Android devices."
        // It was added in Android 8.1 and will be deprecated in Android 15
        if (OperatingSystem.IsAndroid() &&
            OperatingSystem.IsAndroidVersionAtLeast(8, 1) && // At least 8.1
            !OperatingSystem.IsAndroidVersionAtLeast(15)) // At most 15
        {
            sessionOptions.AppendExecutionProvider_Nnapi();
            _logger.LogInformation("Initialized ExecutionProvider: nnAPI for {ModelName}", modelName);
            return;
        }

        if (OperatingSystem.IsIOS() ||
            OperatingSystem.IsMacCatalyst() ||
            OperatingSystem.IsMacOS() ||
            OperatingSystem.IsWatchOS() ||
            OperatingSystem.IsTvOS())
        {
            sessionOptions.AppendExecutionProvider_CoreML();
            _logger.LogInformation("Initialized ExecutionProvider: CoreML for {ModelName}", modelName);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // If DirectML is supported on the user's system, try using it first.
            // This has support for both AMD and Nvidia GPUs, and uses less memory in my testing
            try
            {
                sessionOptions.AppendExecutionProvider_DML();
                _logger.LogInformation("Initialized ExecutionProvider: DirectML for {ModelName}", modelName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure Gpu.");
                _logger.LogWarning("Failed to create DML Execution Provider on Windows. Falling back to CUDA...");
            }
        }

        // If the user's system does not support DirectML (for whatever reason,
        // it's shipped with Windows 10, version 1903(10.0; Build 18362)+
        // Fallback on good ol' CUDA
        try
        {
            sessionOptions.AppendExecutionProvider_CUDA();
            _logger.LogInformation("Initialized ExecutionProvider: CUDA for {ModelName}", modelName);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Gpu.");
            _logger.LogWarning("Failed to create CUDA Execution Provider.");
        }

        // And, if CUDA fails (or we have an AMD card)
        // Try one more time with ROCm
        try
        {
            sessionOptions.AppendExecutionProvider_ROCm();
            _logger.LogInformation("Initialized ExecutionProvider: ROCm for {ModelName}", modelName);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure ROCm.");
            _logger.LogWarning("Failed to create ROCm Execution Provider.");
        }

        _logger.LogWarning("No GPU acceleration will be applied.");
        sessionOptions.AppendExecutionProvider_CPU();
    }

    /// <summary>
    /// Make our SessionOptions *fancy*
    /// </summary>
    /// <returns></returns>
    private SessionOptions SetupSessionOptions()
    {
        // Random environment variable(s) to speed up webcam opening on the MSMF backend.
        // https://github.com/opencv/opencv/issues/17687
        Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
        Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");

        // Setup inference backend
        var sessionOptions = new SessionOptions();
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        // ~3% savings worth ~6ms avg latency. Not noticeable at 60fps?
        sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        sessionOptions.EnableMemoryPattern = true;
        return sessionOptions;
    }

    /// <summary>
    /// Runs inference on current InputTensor
    /// </summary>
    /// <returns></returns>
    public float[] Run()
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, InputTensor)
        };

        using var results = _session.Run(inputs);

        List<float> arKitExpressions = [];
        foreach (var result in results)
        {
            arKitExpressions.AddRange(result.AsEnumerable<float>().ToArray());
        }

        return arKitExpressions.ToArray();
    }

    public DenseTensor<float> GetInputTensor()
    {
        return InputTensor;
    }
}
