using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json;
using OpenCvSharp;

namespace Baballonia.Services;

public class DefaultInferenceRunner(ILoggerFactory loggerFactory) : IInferenceRunner
{
    public Size InputSize { get; private set; }
    public int OutputSize { get; private set; }
    public DenseTensor<float> InputTensor;
    private ILogger _logger;
    private string _inputName;
    private InferenceSession _session;
    private string[] _outputExpressionNames;
    private bool _isOldEyeModel;


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

        InitializeModelMetadata();

        _logger.LogInformation("{} initialization finished", modelPath);
    }

    /// <summary>
    /// Reads and caches model metadata once during initialization
    /// </summary>
    private void InitializeModelMetadata()
    {
        _isOldEyeModel = _session.ModelMetadata.CustomMetadataMap.Count() == 0;

        if (!_isOldEyeModel)
        {
            var metadataJson = _session.ModelMetadata.CustomMetadataMap["blendshape_names"];
            _outputExpressionNames = JsonConvert.DeserializeObject<string[]>(metadataJson)!;
        }
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

        float[] output = OrganizeOutputShapes(results);
        OutputSize = output.Length;
        return output;
    }

    // Dictionary mapping eye output names to their indices
    private static readonly Dictionary<string, int> OutputIndexMap = new()
    {
        { "leftEyePitch", 0 },
        { "leftEyeYaw", 1 },
        { "leftEyeLid", 2 },
        { "leftEyeWiden", 3 },
        { "leftBrow", 4 },
        { "rightEyePitch", 5 },
        { "rightEyeYaw", 6 },
        { "rightEyeLid", 7 },
        { "rightEyeWiden", 8 },
        { "rightBrow", 9 }
    };

    // Guarantee an expression's index by using named parameter values
    private float[] OrganizeOutputShapes(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        // Is this model the face model?
        var candidate = results[0].AsEnumerable<float>().ToArray();
        if (candidate.Length == Utils.FaceRawExpressions)
        {
            return candidate;
        }

        // Else, is this an older eye model?
        if (_isOldEyeModel)
        {
            return [];
        }

        // Else, order eye information using cached metadata
        // Start by flattening new eye ONNX output
        List<Tuple<string, float>> arKitExpressions = [];
        int counter = 0;
        foreach (var result in results)
        {
            var exps = result.AsEnumerable<float>().ToArray();
            foreach (var exp in exps)
            {
                arKitExpressions.Add(new Tuple<string, float>(_outputExpressionNames[counter], exp));
                counter++;
            }
        }

        float[] output = new float[arKitExpressions.Count];
        foreach (var expression in arKitExpressions)
        {
            var name = expression.Item1;
            var value = expression.Item2;

            if (OutputIndexMap.TryGetValue(name, out var index))
            {
                output[index] = value;
            }
        }

        return output;
    }

    public DenseTensor<float> GetInputTensor()
    {
        return InputTensor;
    }
}
