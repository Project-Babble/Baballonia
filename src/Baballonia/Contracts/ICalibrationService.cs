using System.Threading.Tasks;
using Baballonia.Services.Calibration;
using Baballonia.Services.Inference.Filters;

namespace Baballonia.Contracts;

public interface ICalibrationService
{
    void SetExpression(string expression, float value);

    CalibrationParameter GetExpressionSettings(string parameterName);

    AutocalibOptimized? FaceAutocalib { get; set; }

    float ApplyCalibrationSetting(string expression, float value);
    float[] ApplyFaceCalibration(float[] expression);
    float GetExpressionSetting(string expression);
    void ResetValues();
    void ResetMinimums();
    void ResetMaximums();

}
