using System.Reflection;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Baballonia;

public class BabbleVrc : ExtTrackingModule
{
    private BabbleOsc babbleOSC;
    private BabbleLink? babbleLink;
    private Config config;
    private bool needsEye;
    private bool needsExpression;
    private bool eyeOffered;          // VRCFaceTracking offered eye tracking at initialization.
    private bool expressionOffered;   // VRCFaceTracking offered expression tracking at initialization.

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        config = BabbleConfig.GetBabbleConfig();
        // Claim what the config allows and no other module has. A linked headset keeps the config in step with its
        // eye and mouth tracking. Decided before the receivers start, so they only write what this module claims.
        eyeOffered = eyeAvailable;
        expressionOffered = expressionAvailable;
        needsEye = eyeAvailable && config.IsEyeSupported;
        needsExpression = expressionAvailable && config.IsFaceSupported;

        if (config.LinkEnabled)
        {
            try
            {
                babbleLink = new BabbleLink(Logger, config.LinkPort);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"BFR Link is off: cannot listen on UDP {config.LinkPort} ({e.Message})");
            }
        }
        babbleOSC = new BabbleOsc(Logger, config.Host, config.Port, needsEye, needsExpression, babbleLink);
        if (babbleLink != null)
        {
            var osc = babbleOSC;
            babbleLink.ValueReceived += (address, value) => osc.Apply(address, value, fromLink: true);
            babbleLink.SessionEnded += osc.ResetLinkValues;
            babbleLink.FeaturesReceived += OnHeadsetFeatures;
            babbleLink.Mode = () => (needsEye, needsExpression, RestartNeeded());
        }

        List<Stream> list = new List<Stream>();
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        if (needsEye)
        {
            Logger.LogInformation("Baballonia will use Eye Tracking.");
            Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("VRCFaceTracking.Baballonia.BabbleEyeLogo.png")!;
            list.Add(manifestResourceStream);
        }
        if (needsExpression)
        {
            Logger.LogInformation("Baballonia will use Face Tracking.");
            Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("VRCFaceTracking.Baballonia.BabbleFaceLogo.png")!;
            list.Add(manifestResourceStream);
        }

        executingAssembly.GetManifestResourceNames();

        ModuleInformation = new ModuleMetadata
        {
            Name = "Project Babble Module v4.0.0",
            StaticImages = list
        };

        return (needsEye, needsExpression);
    }

    /// <summary>Saves the linked headset's eye and face tracking state as the claims for the next initialization.</summary>
    private void OnHeadsetFeatures(bool eye, bool face)
    {
        if (config.IsEyeSupported == eye && config.IsFaceSupported == face)
            return;
        config.IsEyeSupported = eye;
        config.IsFaceSupported = face;
        try
        {
            BabbleConfig.SaveBabbleConfig();
        }
        catch (Exception e)
        {
            Logger.LogWarning($"BFR Link: cannot save BabbleConfig.json ({e.Message})");
        }
        var state = $"eye tracking {(eye ? "on" : "off")}, face tracking {(face ? "on" : "off")}";
        if (RestartNeeded())
            Logger.LogWarning($"BFR Link: the headset has {state}. Restart VRCFaceTracking to claim this.");
        else
            Logger.LogInformation($"BFR Link: the headset has {state}.");
    }

    /// <summary>True if initializing again with the saved config would claim something different. Only what
    /// VRCFaceTracking offered counts, because a restart cannot take tracking from another module.</summary>
    private bool RestartNeeded() =>
        (config.IsEyeSupported && eyeOffered) != needsEye ||
        (config.IsFaceSupported && expressionOffered) != needsExpression;

    public override void Teardown()
    {
        babbleLink?.Teardown();
        babbleOSC.Teardown();
    }

    public override void Update()
    {
        Thread.Sleep(10);
    }
}
