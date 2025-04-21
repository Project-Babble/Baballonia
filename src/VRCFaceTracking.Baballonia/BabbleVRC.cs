using System.Reflection;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Babble;

public class BabbleVrc : ExtTrackingModule
{
    private BabbleOsc babbleOSC;

    public BabbleVrc(BabbleOsc babbleOsc)
    {
        babbleOSC = babbleOsc;
    }

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        Config babbleConfig = BabbleConfig.GetBabbleConfig();
        babbleOSC = new BabbleOsc(Logger, babbleConfig.Host, babbleConfig.Port);
        List<Stream> list = new List<Stream>();
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("VRCFaceTracking.Babble.BabbleLogo.png")!;
        list.Add(manifestResourceStream);
        ModuleInformation = new ModuleMetadata
        {
            Name = "Project Babble Eye and Face Module v1.0.1",
            StaticImages = list
        };
        return (true, true);
    }

    public override void Teardown()
    {
        babbleOSC.Teardown();
    }

    public override void Update()
    {
        UnifiedTracking.Data.Eye.Left.Gaze.x = BabbleOsc.Expressions[0];
        UnifiedTracking.Data.Eye.Left.Gaze.y = BabbleOsc.Expressions[1];
        UnifiedTracking.Data.Eye.Right.Gaze.x = BabbleOsc.Expressions[2];
        UnifiedTracking.Data.Eye.Right.Gaze.x = BabbleOsc.Expressions[3];
        UnifiedTracking.Data.Eye.Left.Openness = 1f;
        UnifiedTracking.Data.Eye.Right.Openness = 1f;

        foreach (UnifiedExpressions expression in BabbleExpressions.BabbleExpressionMap!)
        {
            UnifiedTracking.Data.Shapes[(int)expression].Weight = BabbleExpressions.BabbleExpressionMap.GetByKey1(expression);
        }
        Thread.Sleep(10);
    }
}
