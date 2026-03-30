namespace X3DCcdOptimizer.Models;

public record CoreSnapshot
{
    public int CoreIndex { get; init; }
    public int CcdIndex { get; init; }
    public double LoadPercent { get; init; }
    public double FrequencyMHz { get; init; }
    public double? TemperatureC { get; init; }
}
