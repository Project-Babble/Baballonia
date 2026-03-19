using Baballonia.Contracts;
using Baballonia.Helpers;
using System;

namespace Baballonia.Factories
{
    public class CommandSenderFactory : ICommandSenderFactory
    {
        public ICommandSender Create(CommandSenderType type, string port)
        {
            switch (type)
            {
                case CommandSenderType.Serial:
                    return new SerialCommandSender(port);
            };
            throw new NotImplementedException();
        }
    }
}
