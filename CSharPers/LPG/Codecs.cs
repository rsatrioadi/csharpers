using Newtonsoft.Json.Linq;

namespace CSharPers.LPG;

public interface IGraphCodec<TGraphType, TNodesType, TEdgesType, TNodeType, TEdgeType>
{
    TNodeType EncodeNode(Node node);
    TNodesType EncodeNodes(Nodes nodes);
    TEdgeType EncodeEdge(Edge edge);
    TEdgesType EncodeEdges(Edges edges);
    TGraphType EncodeGraph(Graph graph);

    Graph DecodeGraph(TGraphType encodedGraph);
    Nodes DecodeNodes(TNodesType encodedNodes);
    Edges DecodeEdges(TEdgesType encodedEdges);
    Node DecodeNode(TNodeType encodedNode);
    Edge DecodeEdge(TEdgeType encodedEdge);
}

public class CyJsonCodec : IGraphCodec<JObject, JArray, JArray, JObject, JObject>
{
    private static readonly Lazy<CyJsonCodec> LazyInstance = new(() => new CyJsonCodec());

    private CyJsonCodec()
    {
    }

    public static CyJsonCodec Instance => LazyInstance.Value;

    public JObject EncodeGraph(Graph graph)
    {
        var elements = new JObject
        {
            ["nodes"] = EncodeNodes(graph.Nodes),
            ["edges"] = EncodeEdges(graph.Edges)
        };

        var obj = new JObject
        {
            ["elements"] = elements
        };

        return obj;
    }

    public JArray EncodeNodes(Nodes nodes)
    {
        var nodeArray = new JArray();
        foreach (var node in nodes.AsList()) nodeArray.Add(EncodeNode(node));
        return nodeArray;
    }

    public JArray EncodeEdges(Edges edges)
    {
        var edgeArray = new JArray();
        foreach (var edge in edges.AsList()) edgeArray.Add(EncodeEdge(edge));
        return edgeArray;
    }

    public JObject EncodeNode(Node node)
    {
        var data = new JObject
        {
            ["id"] = node.Id,
            ["labels"] = new JArray(node.Labels),
            ["properties"] = JObject.FromObject(node.Properties)
        };

        var element = new JObject
        {
            ["data"] = data
        };

        return element;
    }

    public JObject EncodeEdge(Edge edge)
    {
        var data = new JObject
        {
            ["id"] = edge.Id,
            ["source"] = edge.SourceId,
            ["target"] = edge.TargetId,
            ["label"] = edge.Label,
            ["properties"] = JObject.FromObject(edge.Properties)
        };

        var element = new JObject
        {
            ["data"] = data
        };

        return element;
    }

    public Graph DecodeGraph(JObject encodedGraph)
    {
        throw new NotImplementedException();
    }

    public Nodes DecodeNodes(JArray encodedNodes)
    {
        throw new NotImplementedException();
    }

    public Edges DecodeEdges(JArray encodedEdges)
    {
        throw new NotImplementedException();
    }

    public Node DecodeNode(JObject encodedNode)
    {
        var data = encodedNode["data"] as JObject;
        var node = new Node(
            data!["id"]!.ToString(),
            data["labels"]!.Select(label => label.ToString()).ToArray()
        );

        var properties = data["properties"] as JObject;
        foreach (var (key, value) in properties ?? [])
        {
            switch (value)
            {
                case null:
                    continue;
                case JArray array:
                {
                    var newValue = array.ToObject<List<object>>();
                    if (newValue != null) node.Properties[key] = newValue;
                    break;
                }
                default:
                {
                    var newValue = value.ToObject<object>();
                    if (newValue != null) node.Properties[key] = newValue;
                    break;
                }
            }
        }

        return node;
    }

    public Edge DecodeEdge(JObject encodedEdge)
    {
        var data = encodedEdge["data"] as JObject;
        var edge = new Edge(
            data!["source"]!.ToString(),
            data["target"]!.ToString(),
            data["label"]!.ToString()
        );

        var properties = data["properties"] as JObject;
        foreach (var (key, value) in properties ?? [])
        {
            switch (value)
            {
                case null:
                    continue;
                case JArray array:
                {
                    var newValue = array.ToObject<List<object>>();
                    if (newValue != null) edge.Properties[key] = newValue;
                    break;
                }
                default:
                {
                    var newValue = value.ToObject<object>();
                    if (newValue != null) edge.Properties[key] = newValue;
                    break;
                }
            }
        }

        return edge;
    }

    public void WriteToFile(Graph graph, string directory, string baseName)
    {
        var path = Path.Combine(directory, $"{baseName}.json");
        File.WriteAllText(path, EncodeGraph(graph).ToString());
    }
}