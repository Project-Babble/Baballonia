using System.Collections.Generic;

namespace Baballonia.Services.Inference;

public interface IFilter
{
    Dictionary<string, float> Filter(Dictionary<string, float> input);
}
