using System.Collections.Generic;

namespace Baballonia.Services.Inference;

public interface IFilter
{
    OrderedFloatMap Filter(OrderedFloatMap input);
}
