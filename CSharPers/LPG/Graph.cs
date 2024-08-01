namespace CSharPers.LPG;

public class Graph(string name)
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
        return encoder.EncodeGraph(this).ToString();
    }
}