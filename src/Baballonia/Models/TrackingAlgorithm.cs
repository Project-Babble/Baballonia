namespace AvaloniaMiaDev.Models;

public class TrackingAlgorithm(int order, bool shouldUse, string name, string description)
{
    public int Order { get; set; } = order;
    public bool ShouldUse { get; set; } = shouldUse;
    public string Name { get; } = name;
    public string Description { get; } = description;
}
