using Baballonia.Contracts;
using Baballonia.Validation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Baballonia.Models;

public partial class OscTarget : ObservableValidator, IOscTarget
{
    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [property: SavedSetting("OSCInPort", 8889)]
    private int _inPort;

    [ObservableProperty]
    [property: SavedSetting("OSCOutPort", 8888)]
    private int _outPort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InPort))] [NotifyPropertyChangedFor(nameof(OutPort))]

    [property: SavedSetting("OSCAddress", "127.0.0.1")]
    [ValidIpAddress]
    private string _destinationAddress;

    public OscTarget(ILocalSettingsService localSettingsService)
    {
        localSettingsService.Load(this);
        PropertyChanged += (_, _) => localSettingsService.Save(this);
    }
}
