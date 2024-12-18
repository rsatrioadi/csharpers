namespace CSharPers.LPG;

public class Node(string id, params string[] labels)
{
    public string Id { get; } = id;
    public List<string> Labels { get; } = [..labels];
    public Dictionary<string, object> Properties { get; } = [];

    public override bool Equals(object? obj)
    {
        if (obj is Node otherNode) return Id == otherNode.Id;
        return false;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public override string ToString()
    {
        return ToString(CyJsonCodec.Instance);
    }

    public string ToString<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>(
        IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType> encoder)
    {
        return encoder.EncodeNode(this)?.ToString() ?? "";
    }
}

public class Nodes : HashSet<Node>
{
    public static Nodes Of(params Node[] nodes)
    {
        var result = new Nodes();
        foreach (var node in nodes) result.Add(node);
        return result;
    }

    public List<Node> AsList()
    {
        return [.. this];
    }

    public Node? FindById(string id)
    {
        return this.FirstOrDefault(node => node.Id == id);
    }

    public List<Node> FindNodesWithLabel(string label)
    {
        return this.Where(node => node.Labels.Contains(label)).ToList();
    }

    public override string ToString()
    {
        return ToString(CyJsonCodec.Instance);
    }

    public string ToString<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>(
        IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType> encoder)
    {
        return encoder.EncodeNodes(this)?.ToString() ?? "";
    }
}