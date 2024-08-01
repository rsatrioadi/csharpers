using CSharPers.LPG;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharPers;

internal class SyntaxNodeCollector
{
    public static void CollectNodesAndEdges(SyntaxNode root, SemanticModel semanticModel, Graph graph)
    {
        // Handle file-scoped namespaces
        var fileScopedNamespaces = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>();

        foreach (var ns in fileScopedNamespaces)
        {
            if (semanticModel.GetDeclaredSymbol(ns) is not INamespaceSymbol namespaceSymbol) continue;

            var namespaceNode = NodeFactory.CreateNamespaceNode(namespaceSymbol);
            graph.Nodes.Add(namespaceNode);

            foreach (var classDeclaration in ns.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!NodeFactory.CollectClassNodes(semanticModel, classDeclaration, graph, out var classNode)) continue;

                NodeFactory.AddOrUpdateEdge(graph, namespaceNode.Id, classNode.Id, "contains");
            }

            foreach (var interfaceDeclaration in ns.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                if (!NodeFactory.CollectInterfaceNodes(semanticModel, interfaceDeclaration, graph,
                        out var interfaceNode)) continue;

                NodeFactory.AddOrUpdateEdge(graph, namespaceNode.Id, interfaceNode.Id, "contains");
            }
        }

        // Handle block-scoped namespaces
        var blockScopedNamespaces = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>()
            .Where(ns => ns.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken)); // Block-scoped namespaces

        foreach (var ns in blockScopedNamespaces)
        {
            if (semanticModel.GetDeclaredSymbol(ns) is not INamespaceSymbol namespaceSymbol) continue;

            var namespaceNode = NodeFactory.CreateNamespaceNode(namespaceSymbol);
            graph.Nodes.Add(namespaceNode);

            foreach (var classDeclaration in ns.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!NodeFactory.CollectClassNodes(semanticModel, classDeclaration, graph, out var classNode)) continue;

                NodeFactory.AddOrUpdateEdge(graph, namespaceNode.Id, classNode.Id, "contains");
            }

            foreach (var interfaceDeclaration in ns.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                if (!NodeFactory.CollectInterfaceNodes(semanticModel, interfaceDeclaration, graph,
                        out var interfaceNode)) continue;

                NodeFactory.AddOrUpdateEdge(graph, namespaceNode.Id, interfaceNode.Id, "contains");
            }
        }

        // Handle classes and interfaces in the global namespace
        var globalClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => !c.Ancestors().OfType<NamespaceDeclarationSyntax>().Any());
        foreach (var classDeclaration in globalClasses)
            NodeFactory.CollectClassNodes(semanticModel, classDeclaration, graph, out _);

        var globalInterfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>()
            .Where(i => !i.Ancestors().OfType<NamespaceDeclarationSyntax>().Any());
        foreach (var interfaceDeclaration in globalInterfaces)
            NodeFactory.CollectInterfaceNodes(semanticModel, interfaceDeclaration, graph, out _);
    }
}