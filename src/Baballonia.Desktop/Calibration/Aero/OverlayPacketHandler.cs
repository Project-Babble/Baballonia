using System;
using OverlaySDK;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration.Aero;

public class OverlayPacketHandler : PacketHandlerAdapter
{
    public event Action<HmdPositionalDataPacket> OnPositionData = _ => { };

    // inject the dispatcher itself for simplicity if needed
    private readonly OverlayMessageDispatcher _dispatcher;
    public OverlayPacketHandler(OverlayMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _dispatcher.RegisterHandler(this);
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        base.OnHmdPositionalData(positionalData);
        OnPositionData(positionalData);
    }
}
