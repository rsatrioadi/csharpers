namespace CSharPers.Metrics;

public class HalsteadMetrics
{
    public HalsteadMetrics(string elementId, string elementKind, int n1, int n2, int N1, int N2)
    {
        ElementId = elementId;
        ElementKind = elementKind;
        this.n1 = n1;
        this.n2 = n2;
        this.N1 = N1;
        this.N2 = N2;

        Vocabulary = n1 + n2;
        Length = N1 + N2;
        Volume = Length * Math.Log2(Vocabulary > 0 ? Vocabulary : 1);
        Difficulty = n2 > 0 ? n1 / 2.0 * (N2 / (double)n2) : 0;
        Effort = Difficulty * Volume;
        EstimatedBugs = Volume / 3000.0;
    }

    public string ElementId { get; }
    public string ElementKind { get; }

    private int n1 { get; } // Unique operators
    private int n2 { get; } // Unique operands
    private int N1 { get; } // Total operators
    private int N2 { get; } // Total operands

    private int Vocabulary { get; init; }
    private int Length { get; init; }
    private double Volume { get; init; }
    private double Difficulty { get; init; }
    private double Effort { get; init; }
    private double EstimatedBugs { get; init; }

    /// <summary>
    ///     Aggregates multiple HalsteadMetrics (e.g. methods → class; classes → namespace).
    /// </summary>
    public static HalsteadMetrics Aggregate(string elementId, string elementKind, IEnumerable<HalsteadMetrics> list)
    {
        List<HalsteadMetrics> halsteadMetricsEnumerable = list.ToList();
        var totalLength = halsteadMetricsEnumerable.Sum(m => m.Length);
        var totalVolume = halsteadMetricsEnumerable.Sum(m => m.Volume);
        var totalEffort = halsteadMetricsEnumerable.Sum(m => m.Effort);
        return new HalsteadMetrics(elementId, elementKind, 0, 0, 0, 0)
        {
            // override with aggregated values
            Vocabulary = -1,
            Length = totalLength,
            Volume = totalVolume,
            Difficulty = double.NaN,
            Effort = totalEffort,
            EstimatedBugs = totalVolume / 3000.0
        };
    }

    /// <summary>
    ///     Exports everything as a property map.
    /// </summary>
    public IDictionary<string, object> ToDictionary(object? nanReplacement = null)
    {
        return new Dictionary<string, object>
        {
            ["id"] = ElementId,
            ["kind"] = ElementKind,
            // ["uniqueOperators"] = n1,
            // ["uniqueOperands"] = n2,
            // ["totalOperators"] = N1,
            // ["totalOperands"] = N2,
            ["vocabulary"] = Vocabulary,
            ["length"] = Length,
            ["volume"] = Fix(Volume),
            ["difficulty"] = Fix(Difficulty),
            ["effort"] = Fix(Effort),
            ["estimatedBugs"] = Fix(EstimatedBugs)
        };

        object Fix(double d)
        {
            return double.IsNaN(d) ? nanReplacement ?? -1 : d;
        }
    }
}