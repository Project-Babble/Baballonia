using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Baballonia;

public class BabbleVrc : ExtTrackingModule
{
    private const string Host = "127.0.0.1";
    private const int Port = 8888;
    /// <summary>BFR Link control port (UDP): lets a Babble Frame headset find this module on the network.</summary>
    private const int LinkPort = 6767;

    /// <summary>How long Initialize waits for Baballonia (or a linked headset) to say what to track.</summary>
    private static readonly TimeSpan RequestWait = TimeSpan.FromSeconds(10);
    /// <summary>What to track when nothing says otherwise within <see cref="RequestWait"/>.</summary>
    private static readonly (bool Eye, bool Face) Fallback = (false, true);

    /// <summary>
    /// A reinitialization tears this module's process down, so the request that caused it is handed to the next
    /// Initialize through this file, which reads and deletes it. Older ones are leftovers from a crash and ignored.
    /// </summary>
    private const string PendingRequestFile = "BabblePendingInit.txt";
    private static readonly TimeSpan PendingRequestMaxAge = TimeSpan.FromMinutes(2);

    private BabbleOsc babbleOSC;
    private BabbleLink? babbleLink;
    private bool needsEye;
    private bool needsExpression;

    private readonly object requestLock = new();
    private readonly ManualResetEventSlim requestReceived = new();
    private (bool Eye, bool Face)? startupRequest;   // the latest request while Initialize waits
    private (bool Eye, bool Face)? initRequest;      // the request Initialize used; null while it waits
    private bool reinitRequested;

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        // Listen first: the request saying what to claim arrives over these.
        try
        {
            babbleLink = new BabbleLink(Logger, LinkPort);
        }
        catch (Exception e)
        {
            Logger.LogWarning($"BFR Link is off: cannot listen on UDP {LinkPort} ({e.Message})");
        }
        babbleOSC = new BabbleOsc(Logger, Host, Port, babbleLink);
        babbleOSC.InitRequested += (eye, face) => OnRequest(eye, face, "Baballonia");
        if (babbleLink != null)
        {
            var osc = babbleOSC;
            babbleLink.ValueReceived += (address, value) => osc.Apply(address, value, fromLink: true);
            babbleLink.SessionEnded += osc.ResetLinkValues;
            babbleLink.FeaturesReceived += (eye, face) => OnRequest(eye, face, "BFR Link headset");
            babbleLink.Mode = () => (needsEye, needsExpression, reinitRequested);
        }

        var (wantEye, wantFace) = TakePendingRequest() ?? WaitForRequest();

        // Claim what was asked for and no other module has. Set before any value is applied, so the receivers only
        // write what this module claims.
        needsEye = eyeAvailable && wantEye;
        needsExpression = expressionAvailable && wantFace;
        babbleOSC.SetClaims(needsEye, needsExpression);

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

        ModuleInformation = new ModuleMetadata
        {
            Name = "Project Babble Module v4.0.0",
            StaticImages = list
        };

        return (needsEye, needsExpression);
    }

    /// <summary>Waits up to <see cref="RequestWait"/> for a request, then settles on it (or the fallback). A request
    /// arriving after this goes through a reinitialization.</summary>
    private (bool Eye, bool Face) WaitForRequest()
    {
        Logger.LogInformation($"Waiting up to {RequestWait.TotalSeconds:0} s for Baballonia to request eye and/or face tracking...");
        requestReceived.Wait(RequestWait);
        lock (requestLock)
        {
            if (startupRequest == null)
                Logger.LogInformation("No request received; falling back to face tracking only.");
            initRequest = startupRequest ?? Fallback;
            return initRequest.Value;
        }
    }

    /// <summary>A request for eye and face tracking, from the Baballonia app or a linked headset.</summary>
    private void OnRequest(bool eye, bool face, string source)
    {
        lock (requestLock)
        {
            if (initRequest == null)
            {
                // Initialize is still waiting: it takes this one directly.
                startupRequest = (eye, face);
                requestReceived.Set();
                return;
            }
            // Already in effect (or on its way): nothing to do. A headset repeats its state on every ping.
            if (!reinitRequested && initRequest == (eye, face))
                return;
            if (!SavePendingRequest(eye, face))
                return;
            if (reinitRequested)
                return;   // the reinitialization underway picks up the newer request from the file
            reinitRequested = true;
        }
        Logger.LogInformation($"{source} requested eye tracking {(eye ? "on" : "off")}, face tracking {(face ? "on" : "off")}. Reinitializing modules.");
        RequestReinitialize();
    }

    private static string PendingRequestPath =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, PendingRequestFile);

    private bool SavePendingRequest(bool eye, bool face)
    {
        try
        {
            File.WriteAllText(PendingRequestPath, string.Create(CultureInfo.InvariantCulture, $"{eye},{face}"));
            return true;
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Cannot pass the tracking request on to the reinitialized module ({e.Message}); ignoring it.");
            return false;
        }
    }

    /// <summary>The request that caused this reinitialization, if any. Read once: the file is deleted.</summary>
    private (bool Eye, bool Face)? TakePendingRequest()
    {
        var path = PendingRequestPath;
        try
        {
            if (!File.Exists(path))
                return null;
            var fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < PendingRequestMaxAge;
            var parts = File.ReadAllText(path).Split(',');
            File.Delete(path);
            if (!fresh || parts.Length != 2 || !bool.TryParse(parts[0], out var eye) || !bool.TryParse(parts[1], out var face))
                return null;
            lock (requestLock)
                initRequest = (eye, face);
            Logger.LogInformation($"Reinitialized on request: eye tracking {(eye ? "on" : "off")}, face tracking {(face ? "on" : "off")}.");
            return (eye, face);
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Cannot read the pending tracking request ({e.Message}).");
            return null;
        }
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
