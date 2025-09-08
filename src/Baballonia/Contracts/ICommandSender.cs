using System;

namespace Baballonia.Contracts
{
    public interface ICommandSender : IDisposable
    {
        public void WriteLine(string message);
        public string ReadLine(TimeSpan timeout);

    }
}
