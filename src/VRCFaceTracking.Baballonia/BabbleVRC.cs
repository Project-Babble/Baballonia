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

    // We need to call GetBabbleConfig ahead of Initialize
    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        config = BabbleConfig.GetBabbleConfig();
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
        babbleOSC = new BabbleOsc(Logger, config.Host, config.Port, babbleLink);
        if (babbleLink != null)
        {
            var osc = babbleOSC;
            babbleLink.ValueReceived += (address, value) => osc.Apply(address, value, fromLink: true);
            babbleLink.SessionEnded += osc.ResetLinkValues;
        }

        List<Stream> list = new List<Stream>();
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        if (eyeAvailable && config.IsEyeSupported)
        {
            Logger.LogInformation("Baballonia will use Eye Tracking.");
            Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("VRCFaceTracking.Baballonia.BabbleEyeLogo.png")!;
            list.Add(manifestResourceStream);
            needsEye = true;
        }
        if (expressionAvailable && config.IsFaceSupported)
        {
            Logger.LogInformation("Baballonia will use Face Tracking.");
            Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("VRCFaceTracking.Baballonia.BabbleFaceLogo.png")!;
            list.Add(manifestResourceStream);
            needsExpression = true;
        }

        executingAssembly.GetManifestResourceNames();

        ModuleInformation = new ModuleMetadata
        {
            Name = "Project Babble Module v3.1.0",
            StaticImages = list
        };

        return (needsEye, needsExpression);
    }

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
