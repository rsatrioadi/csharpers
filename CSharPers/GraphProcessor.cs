using CSharPers.LPG;
using Microsoft.CodeAnalysis.MSBuild;

namespace CSharPers;

internal static class GraphProcessor
{
    public static async Task ProcessSolutionAsync(string solutionPath, Graph graph)
    {
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();

                SyntaxNodeCollector.CollectNodesAndEdges(root, semanticModel, graph);
            }
        }

        // Cleanup step: Remove edges with non-existent source or target nodes
        var validNodeIds = new HashSet<string>(graph.Nodes.Select(node => node.Id));
        graph.Edges.RemoveWhere(edge =>
            !validNodeIds.Contains(edge.SourceId) || !validNodeIds.Contains(edge.TargetId));
    }
}