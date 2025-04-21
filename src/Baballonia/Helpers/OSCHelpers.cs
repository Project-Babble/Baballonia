using System.Collections.Generic;
using OscCore;

namespace AvaloniaMiaDev.Helpers;

public static class OscHelper
{
    public static List<OscMessage> ExtractMessages(OscPacket packet)
    {
        var messages = new List<OscMessage>();

        if (packet is OscMessage msg)
        {
            messages.Add(msg);
        }
        else if (packet is OscBundle bundle)
        {
            foreach (var innerPacket in bundle)
            {
                messages.AddRange(ExtractMessages(innerPacket)); // Recursive call
            }
        }

        return messages;
    }
}
