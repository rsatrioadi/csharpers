using CSharPers.LPG;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace CSharPers;

internal class NodeFactory
{
	public static Node CreateNamespaceNode(INamespaceSymbol namespaceSymbol)
    {
        return new Node(RemoveAngleBrackets(namespaceSymbol.ToString()), "Container")
        {
            Properties =
            {
                ["simpleName"] = namespaceSymbol.Name,
                ["qualifiedName"] = namespaceSymbol.ToString(),
                ["kind"] = namespaceSymbol.Kind.ToString(),
                ["visibility"] = "public" // Namespaces in C# are public
            }
        };
    }

    public static bool CollectClassNode(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration,
        Graph graph, out Node? node)
    {
        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            node = null;
            return false;
        }

        node = new Node(RemoveAngleBrackets(classSymbol.ToString()), "Structure")
        {
            Properties =
            {
                ["simpleName"] = classSymbol.Name,
                ["qualifiedName"] = classSymbol.ToString(),
                ["kind"] = classSymbol.Kind.ToString(),
                ["visibility"] = GetVisibility(classSymbol.DeclaredAccessibility)
            }
        };
        graph.Nodes.Add(node);

        CollectClassMembers(classDeclaration, node, semanticModel, graph);

        // Handle inheritance
        if (classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object)
		{
            AddOrUpdateEdge(graph, node.Id, RemoveAngleBrackets(classSymbol.BaseType.ToString()), "specializes");
		}

        foreach (var interfaceImplemented in classSymbol.Interfaces)
		{
			AddOrUpdateEdge(graph, node.Id, RemoveAngleBrackets(interfaceImplemented.ToString()), "specializes");
		}

        foreach (var nestedClass in classDeclaration.Members.OfType<ClassDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(nestedClass) is not INamedTypeSymbol nestedClassSymbol)
                continue;

            AddOrUpdateEdge(graph, node.Id, RemoveAngleBrackets(nestedClassSymbol.ToString()), "nests");
        }

        return true;
    }

    public static bool CollectInterfaceNode(SemanticModel semanticModel,
        InterfaceDeclarationSyntax interfaceDeclaration, Graph graph, out Node? node)
    {
        if (semanticModel.GetDeclaredSymbol(interfaceDeclaration) is not INamedTypeSymbol interfaceSymbol)
        {
            node = null;
            return false;
        }

        node = new Node(RemoveAngleBrackets(interfaceSymbol.ToString()), "Structure")
        {
            Properties =
            {
                ["simpleName"] = interfaceSymbol.Name,
                ["qualifiedName"] = interfaceSymbol.ToString(),
                ["kind"] = interfaceSymbol.Kind.ToString(),
                ["visibility"] = GetVisibility(interfaceSymbol.DeclaredAccessibility)
            }
        };
        graph.Nodes.Add(node);

        foreach (var nestedInterface in interfaceDeclaration.Members.OfType<InterfaceDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(nestedInterface) is not INamedTypeSymbol nestedInterfaceSymbol)
                continue;

            AddOrUpdateEdge(graph, node.Id, RemoveAngleBrackets(nestedInterfaceSymbol.ToString()), "nests");
        }

        return true;
    }

    private static void CollectClassMembers(ClassDeclarationSyntax classDeclaration, Node classNode,
        SemanticModel semanticModel, Graph graph)
    {
        foreach (var member in classDeclaration.Members)
            if (member is MethodDeclarationSyntax methodDeclaration)
            {
                if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol) continue;

                var methodNode = new Node(RemoveAngleBrackets(methodSymbol.ToString()), "Operation")
                {
                    Properties =
                    {
                        ["simpleName"] = methodSymbol.Name,
                        ["qualifiedName"] = methodSymbol.ToString(),
                        ["kind"] = methodSymbol.Kind.ToString(),
                        ["visibility"] = GetVisibility(methodSymbol.DeclaredAccessibility)
                    }
                };
                graph.Nodes.Add(methodNode);

                AddOrUpdateEdge(graph, classNode.Id, methodNode.Id, "hasScript");

                foreach (var parameter in methodSymbol.Parameters)
                {
                    var parameterNode = new Node(RemoveAngleBrackets($"{methodSymbol}.{parameter.Name}"), "Variable")
                    {
                        Properties =
                        {
                            ["simpleName"] = parameter.Name,
                            ["qualifiedName"] = $"{methodSymbol}.{parameter.Name}",
                            ["kind"] = parameter.Kind.ToString(),
                            ["visibility"] = "public" // Parameters in C# are public by default
                        }
                    };
                    graph.Nodes.Add(parameterNode);

                    AddOrUpdateEdge(graph, methodNode.Id, parameterNode.Id, "hasParameter");
                    AddOrUpdateEdge(graph, parameterNode.Id, RemoveAngleBrackets(parameter.Type.ToString()), "type");
                }

                if (methodSymbol.ReturnType.ToString() != "void")
                    AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(methodSymbol.ReturnType.ToString()), "returnType");

                foreach (var invokedMethod in methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (semanticModel.GetSymbolInfo(invokedMethod).Symbol is not IMethodSymbol invokedSymbol) continue;

                    AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(invokedSymbol.ToString()), "invokes");
                }

                foreach (var objectCreation in
                         methodDeclaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                    AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(semanticModel.GetTypeInfo(objectCreation).Type?.ToString()),
                        "instantiates");
            }
            else if (member is ConstructorDeclarationSyntax constructorDeclaration)
            {
                if (semanticModel.GetDeclaredSymbol(constructorDeclaration) is not IMethodSymbol constructorSymbol)
                    continue;

                var constructorNode = new Node(RemoveAngleBrackets(constructorSymbol.ToString()), "Constructor")
                {
                    Properties =
                    {
                        ["simpleName"] = constructorSymbol.Name,
                        ["qualifiedName"] = constructorSymbol.ToString(),
                        ["kind"] = constructorSymbol.Kind.ToString(),
                        ["visibility"] = GetVisibility(constructorSymbol.DeclaredAccessibility)
                    }
                };
                graph.Nodes.Add(constructorNode);

                AddOrUpdateEdge(graph, classNode.Id, constructorNode.Id, "hasScript");

                foreach (var parameter in constructorSymbol.Parameters)
                {
                    var parameterNode = new Node(RemoveAngleBrackets($"{constructorSymbol}.{parameter.Name}"), "Variable")
                    {
                        Properties =
                        {
                            ["simpleName"] = parameter.Name,
                            ["qualifiedName"] = $"{constructorSymbol}.{parameter.Name}",
                            ["kind"] = parameter.Kind.ToString(),
                            ["visibility"] = "public" // Parameters in C# are public by default
                        }
                    };
                    graph.Nodes.Add(parameterNode);

                    AddOrUpdateEdge(graph, constructorNode.Id, parameterNode.Id, "hasParameter");
                    AddOrUpdateEdge(graph, parameterNode.Id, RemoveAngleBrackets(parameter.Type.ToString()), "type");
                }

                AddOrUpdateEdge(graph, constructorNode.Id, classNode.Id, "returnType");

                foreach (var invokedMethod in constructorDeclaration.DescendantNodes()
                             .OfType<InvocationExpressionSyntax>())
                {
                    if (semanticModel.GetSymbolInfo(invokedMethod).Symbol is not IMethodSymbol invokedSymbol) continue;

                    AddOrUpdateEdge(graph, constructorNode.Id, RemoveAngleBrackets(invokedSymbol.ToString()), "invokes");
                }

                foreach (var objectCreation in constructorDeclaration.DescendantNodes()
                             .OfType<ObjectCreationExpressionSyntax>())
                    AddOrUpdateEdge(graph, constructorNode.Id,
						RemoveAngleBrackets(semanticModel.GetTypeInfo(objectCreation).Type?.ToString()), "instantiates");
            }
            else if (member is FieldDeclarationSyntax fieldDeclaration)
            {
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol) continue;

                    var fieldNode = new Node(RemoveAngleBrackets(fieldSymbol.ToString()), "Variable")
                    {
                        Properties =
                        {
                            ["simpleName"] = fieldSymbol.Name,
                            ["qualifiedName"] = fieldSymbol.ToString(),
                            ["kind"] = fieldSymbol.Kind.ToString(),
                            ["visibility"] = GetVisibility(fieldSymbol.DeclaredAccessibility)
                        }
                    };
                    graph.Nodes.Add(fieldNode);

                    if (variable.Initializer?.Value is { } initializer)
                        AnalyzeInitializer(initializer, fieldSymbol, semanticModel, graph);

                    AddOrUpdateEdge(graph, classNode.Id, fieldNode.Id, "hasVariable");
                    AddOrUpdateEdge(graph, fieldNode.Id, RemoveAngleBrackets(fieldSymbol.Type.ToString()), "type");
                }
            }
    }

    private static void AnalyzeInitializer(ExpressionSyntax initializer, IFieldSymbol fieldSymbol,
        SemanticModel semanticModel, Graph graph)
    {
        foreach (var invokedMethod in initializer.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invokedMethod).Symbol is not IMethodSymbol invokedSymbol) continue;

            var fieldInitializerNode = new Node(RemoveAngleBrackets($"{fieldSymbol}.initializer"), "Script")
            {
                Properties =
                {
                    ["simpleName"] = $"{fieldSymbol.Name}.initializer",
                    ["qualifiedName"] = $"{fieldSymbol}.initializer",
                    ["kind"] = "FieldInitializer",
                    ["visibility"] = "not applicable"
                }
            };
            graph.Nodes.Add(fieldInitializerNode);

            AddOrUpdateEdge(graph, fieldInitializerNode.Id, RemoveAngleBrackets(invokedSymbol.ToString()), "invokes");
        }

        foreach (var objectCreation in initializer.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var fieldInitializerNode = new Node(RemoveAngleBrackets($"{fieldSymbol}.initializer"), "Script")
            {
                Properties =
                {
                    ["simpleName"] = $"{fieldSymbol.Name}.initializer",
                    ["qualifiedName"] = $"{fieldSymbol}.initializer",
                    ["kind"] = "FieldInitializer",
                    ["visibility"] = "private"
                }
            };
            graph.Nodes.Add(fieldInitializerNode);

            AddOrUpdateEdge(graph, fieldInitializerNode.Id, RemoveAngleBrackets(semanticModel.GetTypeInfo(objectCreation).Type?.ToString()),
                "instantiates");
        }
    }

    private static string GetVisibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedAndInternal => "protected internal",
            Accessibility.NotApplicable => "not applicable",
            _ => "unknown"
        };
	}

	public static string RemoveAngleBrackets(string input)
	{
		// Regular expression pattern to match angle brackets and their content
		string pattern = @"<[^>]*>";

		// Replace matched patterns with an empty string
		string result = Regex.Replace(input, pattern, string.Empty);

		return result;
	}

	public static void AddOrUpdateEdge(Graph graph, string sourceId, string targetId, string label)
	{
		var existingEdge = graph.Edges.FirstOrDefault(edge =>
			edge.SourceId == sourceId && edge.TargetId == targetId && edge.Label == label);
		if (existingEdge != null)
		{
			existingEdge.Properties["weight"] = (int)existingEdge.Properties["weight"] + 1;
		}
		else
		{
			var edge = new Edge(sourceId, targetId, label);
			graph.Edges.Add(edge);
		}
	}
}