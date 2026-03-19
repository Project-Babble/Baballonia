using System;
using System.Text;
using System.Text.Json;

namespace Baballonia.Helpers
{
    public class JsonExtractor
    {
        private StringBuilder _buffer = new StringBuilder();
        private int _lastScannedIndex = 0;

        public JsonDocument ReadUntilValidJson(Func<string> readLineFunction, TimeSpan timeout)
        {

            var startTime = DateTime.Now;
            while (true)
            {
                if (DateTime.Now - startTime > timeout)
                    throw new TimeoutException("Timeout reached");

                var content = _buffer.ToString();

                var start = -1;
                var braceDepth = 0;

                for (var i = _lastScannedIndex; i < content.Length; i++)
                {
                    if (content[i] == '{')
                    {
                        if (braceDepth == 0)
                            start = i;
                        braceDepth++;
                    }
                    else if (content[i] == '}')
                    {
                        braceDepth--;
                        if (braceDepth != 0 || start == -1) continue;

                        var lenghh = i - start + 1;
                        var candidatestr = content.Substring(start, lenghh);

                        var candidate = TryParseJson(candidatestr);
                        if (candidate == null) continue;

                        _buffer.Remove(0, i + 1);
                        _lastScannedIndex = 0;
                        return candidate;
                    }

                }
                _lastScannedIndex = Math.Max(0, content.Length - 1);

                // Only read if buffer was processed and still no JSON
                var line = readLineFunction();
                if (string.IsNullOrWhiteSpace(line)) continue;

                _buffer.Append(line);
                _lastScannedIndex = 0;
            }
        }

        private JsonDocument? TryParseJson(string input)
        {
            try
            {
                var res = JsonDocument.Parse(input);
                return res;
            }
            catch
            {
                return null;
            }
        }

    }
}
