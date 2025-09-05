using System;
using System.Text;
using System.Text.Json;

namespace Baballonia.Helpers
{
    public class JsonExtractor
    {
        private StringBuilder _buffer = new StringBuilder();

        public JsonDocument ReadUntilValidJson(Func<string> readLineFunction, TimeSpan timeout)
        {
            _buffer.Clear();

            var startTime = DateTime.Now;
            while (true)
            {
                if (DateTime.Now - startTime > timeout)
                    throw new TimeoutException("Timeout reached");

                string line = readLineFunction();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                _buffer.Append(line);
                string content = _buffer.ToString();

                int start = -1;
                int braceDepth = 0;

                for (int i = 0; i < content.Length; i++)
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
                        if (braceDepth == 0 && start != -1)
                        {
                            int lenghh = i - start + 1;
                            string candidatestr = content.Substring(start, lenghh);

                            var candidate = TryParseJson(candidatestr);
                            if(candidate != null)
                            {
                                _buffer.Remove(0, i + 1);
                                return candidate;
                            }

                        }
                    }

                }
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
