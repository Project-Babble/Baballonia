using OverlaySDK.Packets;
using System;
using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface ITrainerService : IDisposable
{
    public event Action<TrainerProgressReportPacket>? OnProgress;
    public void RunTraining(string usercalbinPath, string outputfilePath);

    public Task WaitAsync();
}
