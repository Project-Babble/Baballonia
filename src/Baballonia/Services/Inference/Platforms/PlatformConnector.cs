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
public abstract class PlatformConnector
{
    protected ILogger Logger { get; }
    protected ILocalSettingsService LocalSettingsService { get; }

    public string Source { get; private set; }

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
    public Dictionary<Capture, Type> Captures;

    public PlatformConnector(string source, ILogger logger, ILocalSettingsService localSettingsService)
    {
        Source = source;
        Logger = logger;
        LocalSettingsService = localSettingsService;
    }

    /// <summary>
    /// Initializes a Platform Connector
    /// </summary>
    public virtual bool Initialize(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;

        this.Source = source;

        try
        {
            foreach (var capture in Captures
                         .Where(capture => capture.Key.CanConnect(source)))
            {
                Capture = (Capture)Activator.CreateInstance(capture.Value, source)!;
                Logger.LogInformation($"Changed capture source to {capture.Value.Name} with url {source}.");
                break;
            }

            if (Capture is null) return false;

            Capture.StartCapture();
            Logger.LogInformation($"Starting {Capture.GetType().Name} capture source...");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
            throw;
        }

        return false;
    }


    /// <summary>
    /// Shuts down the current Capture source
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public virtual void Terminate()
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
