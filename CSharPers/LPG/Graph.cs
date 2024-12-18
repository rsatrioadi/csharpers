namespace CSharPers.LPG;

#pragma warning disable CS9113 // Parameter is unread.
public class Graph(string name)
#pragma warning restore CS9113 // Parameter is unread.
{
    public Nodes Nodes { get; } = [];
    public Edges Edges { get; } = [];

    public override string ToString()
    {
        return ToString(CyJsonCodec.Instance);
    }

    public string ToString<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>(
        IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType> encoder)
    {
        return encoder.EncodeGraph(this)?.ToString() ?? "";
    }
}