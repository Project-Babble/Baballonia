using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaMiaDev.Models;

public partial class CalibrationItem : ObservableObject
{
    [ObservableProperty]
    public string? shapeName;

    [ObservableProperty]
    public float min;

    [ObservableProperty]
    public float max;
}
