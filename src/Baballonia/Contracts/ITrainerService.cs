using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OverlaySDK.Packets;

namespace Baballonia.Contracts;

public interface ITrainerService : IDisposable
{
    public event Action<TrainerProgressReportPacket>? OnProgress;
    public void RunTraining(string usercalbinPath, string outputfilePath);

    public Task WaitAsync();
}
