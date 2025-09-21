using System;
using System.IO;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class InferenceFactory
{
    private readonly ILogger<InferenceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILocalSettingsService _localSettings;

    public InferenceFactory(ILogger<InferenceFactory> logger, ILocalSettingsService localSettings,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _localSettings = localSettings;
        _loggerFactory = loggerFactory;
    }

    public DefaultInferenceRunner Create(string modelPath)
    {
        var useGpu = _localSettings.ReadSetting<bool>("AppSettings_UseGPU", false);
        _loggerFactory.CreateLogger<DefaultInferenceRunner>();

        var inference = new DefaultInferenceRunner(_loggerFactory);

        inference.Setup(modelPath, useGpu);
        _logger.LogDebug("Loaded model {filename} with hash {ModelHash}", Path.GetFileName(modelPath), Utils.GenerateMD5(modelPath));

        return inference;
    }
}
