using System.Threading.Tasks;
using Baballonia.Services.Calibration;

namespace Baballonia.Contracts;

public interface ICalibrationService
{
    void SetExpression(string expression, float value);

    CalibrationParameter GetExpressionSettings(string parameterName);

    float GetExpressionSetting(string expression);
    void ResetValues();
    void ResetMinimums();
    void ResetMaximums();

}
