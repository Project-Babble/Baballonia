using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services.Inference.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Services.Inference.Platforms;

/// <summary>
/// Manages what Captures are allowed to run on what platforms, as well as their Urls, etc.
/// </summary>
public abstract class PlatformConnector(string source, ILogger logger)
{
    protected ILogger Logger { get; } = logger;

    public string Source { get; private set; } = source;

    /// <summary>
    /// A Platform may have many Capture sources, but only one may ever be active at a time.
    /// This represents the current (and a valid) Capture source for this Platform
    /// </summary>
    public Capture? Capture { get; private set; }

    /// <summary>
    /// Dynamic collection of Capture types, their identifying strings as well as prefix/suffix controls
    /// Add (or remove) from this collection to support platform specific connectors at runtime
    /// Or support weird hardware setups
    /// </summary>
    protected Dictionary<Capture, Type> Captures;

    /// <summary>
    /// Initializes a Platform Connector
    /// </summary>
    public virtual bool Initialize(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;

        this.Source = source;

        try
        {
            Logger.LogDebug("PlatformConnector.Initialize called with source: '{Source}'", Source);
            Logger.LogDebug("Available captures count: {CaptureCount}", Captures?.Count ?? 0);

            if (Captures == null)
            {
                Logger.LogError("Captures dictionary is null - platform connector not properly initialized");
                throw new InvalidOperationException("Captures dictionary is null");
            }

            foreach (var capture in Captures
                         .Where(capture => capture.Key.CanConnect(source)))
            {
                Logger.LogDebug("Attempting to create {CaptureTypeName} with logger support", capture.Value.Name);
                Capture = (Capture)Activator.CreateInstance(capture.Value, source, logger)!;
                Logger.LogInformation("Changed capture source to {CaptureTypeName} with url {Source}.", capture.Value.Name, source);
                break;
            }

            if (Capture is null)
            {
                Logger.LogError("No matching capture type found for Source: '{Source}'", source);
                return false;
            }

            Capture.StartCapture();
            Logger.LogInformation($"Starting {Capture.GetType().Name} capture source...");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception in PlatformConnector.Initialize: {ExceptionType}: {ExceptionMessage}", e.GetType().Name, e.Message);
            return false;
        }
    }

    /// <summary>
    /// Shuts down the current Capture source
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public void Terminate()
    {
        if (Capture is null)
        {
            // Nothing to terminate
            return;
        }

        Logger.LogInformation("Stopping capture source...");
        Capture.StopCapture();
    }
}
