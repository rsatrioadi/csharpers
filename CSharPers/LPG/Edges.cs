namespace CSharPers.LPG;

public class Edge
{
    public Edge(string sourceId, string targetId, string label)
    {
        SourceId = sourceId;
        TargetId = targetId;
        Label = label;
        Id = $"{SourceId}-{Label}-{TargetId}";
        Properties = new Dictionary<string, object> { { "weight", 1 } };
    }

    public string SourceId { get; }
    public string TargetId { get; }
    public string Id { get; }
    public string Label { get; }
    public Dictionary<string, object> Properties { get; }

    public override bool Equals(object? obj)
    {
        if (obj is Edge otherEdge)
            return SourceId == otherEdge.SourceId &&
                   TargetId == otherEdge.TargetId &&
                   Label == otherEdge.Label;
        return false;
    }

    public override int GetHashCode()
    {
        // Combine SourceId, TargetId, and Label to create a hash code
        return HashCode.Combine(SourceId, TargetId, Label);
    }

    public override string ToString()
    {
        return ToString(CyJsonCodec.Instance);
    }

    public string ToString<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>(
        IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType> encoder)
    {
        return encoder.EncodeEdge(this)?.ToString() ?? "";
    }
}

public class Edges : HashSet<Edge>
{
    public static Edges Of(params Edge[] edges)
    {
        var result = new Edges();
        foreach (var edge in edges) result.Add(edge);
        return result;
    }

    public List<Edge> AsList()
    {
        return [.. this];
    }

    public override string ToString()
    {
        return ToString(CyJsonCodec.Instance);
    }

    public string ToString<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>(
        IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType> encoder)
    {
        return encoder.EncodeEdges(this)?.ToString() ?? "";
    }
}