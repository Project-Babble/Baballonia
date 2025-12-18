using System;

namespace Baballonia.Contracts;

public interface IUsbService
{
    public event Action<string> OnUsbConnected;
    public event Action<string> OnUsbDisconnected;
}
