using CSharPers.LPG;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace CSharPers;

internal static partial class NodeFactory
{
	public static Node CreateNamespaceNode(INamespaceSymbol namespaceSymbol)
	{
		return CreateNode(namespaceSymbol, "Container", "public", namespaceSymbol.ToString()!);
	}

	public static bool CollectClassNode(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration,
		Graph graph, out Node? node)
	{
		if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
		{
			node = null;
			return false;
		}

		node = CreateNode(classSymbol, "Structure", GetVisibility(classSymbol.DeclaredAccessibility), classSymbol.ToString()!);
		graph.Nodes.Add(node);

		CollectClassMembers(classDeclaration, node, semanticModel, graph);

		HandleInheritance(classSymbol, graph, node.Id);

		HandleNestedTypes<ClassDeclarationSyntax>(classDeclaration, semanticModel, graph, node, "contains");

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

		node = CreateNode(interfaceSymbol, "Structure", GetVisibility(interfaceSymbol.DeclaredAccessibility), interfaceSymbol.ToString()!);
		graph.Nodes.Add(node);

		HandleNestedTypes<InterfaceDeclarationSyntax>(interfaceDeclaration, semanticModel, graph, node, "contains");

		return true;
	}

	private static Node CreateNode(ISymbol symbol, string label, string visibility, string qualifiedName)
	{
		return new Node(RemoveAngleBrackets(qualifiedName), label)
		{
			Properties =
			{
				["simpleName"] = symbol.Name,
				["qualifiedName"] = qualifiedName,
				["kind"] = symbol.Kind.ToString(),
				["visibility"] = visibility
			}
		};
	}

	private static void HandleInheritance(INamedTypeSymbol classSymbol, Graph graph, string classId)
	{
		if (classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object)
		{
			AddOrUpdateEdge(graph, classId, RemoveAngleBrackets(classSymbol.BaseType.ToString()!), "specializes");
		}

		foreach (var interfaceImplemented in classSymbol.Interfaces)
		{
			AddOrUpdateEdge(graph, classId, RemoveAngleBrackets(interfaceImplemented.ToString()!), "specializes");
		}
	}

	private static void HandleNestedTypes<T>(TypeDeclarationSyntax declaration, SemanticModel semanticModel, Graph graph, Node node, string edgeLabel)
		where T : TypeDeclarationSyntax
	{
		if (declaration.Members.OfType<T>().Any())
		{
			node.Labels.Add("Container");
		}
		foreach (var nestedType in declaration.Members.OfType<T>())
		{
			if (semanticModel.GetDeclaredSymbol(nestedType) is not INamedTypeSymbol nestedSymbol)
				continue;

			AddOrUpdateEdge(graph, node.Id, RemoveAngleBrackets(nestedSymbol.ToString()!), edgeLabel);
		}
	}


	private static void CollectClassMembers(ClassDeclarationSyntax classDeclaration, Node classNode,
		SemanticModel semanticModel, Graph graph)
	{
		foreach (var member in classDeclaration.Members)
		{
			switch (member)
			{
				case MethodDeclarationSyntax methodDeclaration:
					CollectMethodNode(methodDeclaration, semanticModel, graph, classNode);
					break;

				case ConstructorDeclarationSyntax constructorDeclaration:
					CollectConstructorNode(constructorDeclaration, semanticModel, graph, classNode);
					break;

				case FieldDeclarationSyntax fieldDeclaration:
					CollectFieldNodes(fieldDeclaration, semanticModel, graph, classNode);
					break;
			}
		}
	}

	private static void CollectMethodNode(MethodDeclarationSyntax methodDeclaration, SemanticModel semanticModel,
		Graph graph, Node classNode)
	{
		if (semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
			return;

		var methodNode = CreateNode(methodSymbol, "Operation", GetVisibility(methodSymbol.DeclaredAccessibility), methodSymbol.ToString()!);
		graph.Nodes.Add(methodNode);

		AddOrUpdateEdge(graph, classNode.Id, methodNode.Id, "hasScript");

		CollectParameters(methodSymbol, methodNode, graph);

		if (methodSymbol.ReturnType.ToString() != "void")
			AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(methodSymbol.ReturnType.ToString()!), "returnType");

		CollectInvokedMethods(methodDeclaration, semanticModel, graph, methodNode);
		CollectUsedFields(methodDeclaration, semanticModel, graph, methodNode);
		CollectObjectCreations(methodDeclaration, semanticModel, graph, methodNode);
	}

	private static void CollectConstructorNode(ConstructorDeclarationSyntax constructorDeclaration,
		SemanticModel semanticModel, Graph graph, Node classNode)
	{
		if (semanticModel.GetDeclaredSymbol(constructorDeclaration) is not IMethodSymbol constructorSymbol)
			return;

		var constructorNode = CreateNode(constructorSymbol, "Constructor", GetVisibility(constructorSymbol.DeclaredAccessibility), constructorSymbol.ToString()!);
		graph.Nodes.Add(constructorNode);

		AddOrUpdateEdge(graph, classNode.Id, constructorNode.Id, "hasScript");

		CollectParameters(constructorSymbol, constructorNode, graph);

		AddOrUpdateEdge(graph, constructorNode.Id, classNode.Id, "returnType");

		CollectInvokedMethods(constructorDeclaration, semanticModel, graph, constructorNode);
		CollectUsedFields(constructorDeclaration, semanticModel, graph, constructorNode);
		CollectObjectCreations(constructorDeclaration, semanticModel, graph, constructorNode);
	}

	private static void CollectFieldNodes(FieldDeclarationSyntax fieldDeclaration, SemanticModel semanticModel,
		Graph graph, Node classNode)
	{
		foreach (var variable in fieldDeclaration.Declaration.Variables)
		{
			if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol) continue;

			var fieldNode = CreateNode(fieldSymbol, "Variable", GetVisibility(fieldSymbol.DeclaredAccessibility), fieldSymbol.ToString()!);
			graph.Nodes.Add(fieldNode);

			if (variable.Initializer?.Value is { } initializer)
				AnalyzeInitializer(initializer, fieldSymbol, semanticModel, graph);

			AddOrUpdateEdge(graph, classNode.Id, fieldNode.Id, "hasVariable");
			AddOrUpdateEdge(graph, fieldNode.Id, RemoveAngleBrackets(fieldSymbol.Type.ToString()!), "type");
		}
	}
	
	private static void CollectParameters(IMethodSymbol methodSymbol, Node methodNode, Graph graph)
	{
		for (var i = 0; i < methodSymbol.Parameters.Length; i++)
		{
			var parameter = methodSymbol.Parameters[i];
			var parameterNode = new Node(RemoveAngleBrackets($"{methodSymbol}.{parameter.Name}"), "Variable")
			{
				Properties =
				{
					["simpleName"] = parameter.Name,
					["qualifiedName"] = $"{methodSymbol}.{parameter.Name}",
					["kind"] = parameter.Kind.ToString(),
					["visibility"] = "public", // Parameters in C# are public by default
					["parameterPosition"] = i // Add the position of the parameter in the list
				}
			};
			graph.Nodes.Add(parameterNode);

			AddOrUpdateEdge(graph, methodNode.Id, parameterNode.Id, "hasParameter");
			AddOrUpdateEdge(graph, parameterNode.Id, RemoveAngleBrackets(parameter.Type.ToString()!), "type");
		}
	}

	private static void CollectInvokedMethods(SyntaxNode node, SemanticModel semanticModel, Graph graph, Node methodNode)
	{
		foreach (var invokedMethod in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (semanticModel.GetSymbolInfo(invokedMethod).Symbol is not IMethodSymbol invokedSymbol) continue;

			AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(invokedSymbol.ToString()!), "invokes", true);
		}
	}
	
	private static void CollectUsedFields(SyntaxNode node, SemanticModel semanticModel, Graph graph, Node methodNode)
	{
		foreach (var fieldAccess in node.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
		{
			// Get the symbol for the accessed member
			if (semanticModel.GetSymbolInfo(fieldAccess).Symbol is not IFieldSymbol fieldSymbol) continue;

			// Add the edge to the graph
			AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(fieldSymbol.ToString()!), "uses");
		}

		foreach (var identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>())
		{
			// Handle simple field access (e.g., "fieldName" instead of "this.fieldName")
			if (semanticModel.GetSymbolInfo(identifier).Symbol is not IFieldSymbol fieldSymbol) continue;

			// Add the edge to the graph
			AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(fieldSymbol.ToString()!), "uses");
		}
	}

	private static void CollectObjectCreations(SyntaxNode node, SemanticModel semanticModel, Graph graph, Node methodNode)
	{
		foreach (var objectCreation in node.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
		{
			AddOrUpdateEdge(graph, methodNode.Id, RemoveAngleBrackets(semanticModel.GetTypeInfo(objectCreation).Type?.ToString()!), "instantiates", true);
		}
	}

	private static void AnalyzeInitializer(ExpressionSyntax initializer, IFieldSymbol fieldSymbol,
		SemanticModel semanticModel, Graph graph)
	{
		foreach (var invokedMethod in initializer.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (semanticModel.GetSymbolInfo(invokedMethod).Symbol is not IMethodSymbol invokedSymbol) continue;

			var fieldInitializerNode = CreateFieldInitializerNode(fieldSymbol);
			graph.Nodes.Add(fieldInitializerNode);

			AddOrUpdateEdge(graph, fieldInitializerNode.Id, RemoveAngleBrackets(invokedSymbol.ToString()!), "invokes", true);
		}

		foreach (var objectCreation in initializer.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
		{
			var fieldInitializerNode = CreateFieldInitializerNode(fieldSymbol);
			graph.Nodes.Add(fieldInitializerNode);

			AddOrUpdateEdge(graph, fieldInitializerNode.Id, RemoveAngleBrackets(semanticModel.GetTypeInfo(objectCreation).Type?.ToString()!), "instantiates", true);
		}
	}

	private static Node CreateFieldInitializerNode(IFieldSymbol fieldSymbol)
	{
		return new Node(RemoveAngleBrackets($"{fieldSymbol}.initializer"), "Script")
		{
			Properties =
			{
				["simpleName"] = $"{fieldSymbol.Name}.initializer",
				["qualifiedName"] = $"{fieldSymbol}.initializer",
				["kind"] = "FieldInitializer",
				["visibility"] = "not applicable"
			}
		};
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

	private static string RemoveAngleBrackets(string input)
	{
		// Replace matched patterns with an empty string
		var result = AngleBracketsRegex().Replace(input, string.Empty);

		return result;
	}

	public static void AddOrUpdateEdge(Graph graph, string sourceId, string targetId, string label, bool update=false)
	{
		var existingEdge = graph.Edges.FirstOrDefault(edge =>
			edge.SourceId == sourceId && edge.TargetId == targetId && edge.Label == label);
		if (existingEdge == null)
		{
			var edge = new Edge(sourceId, targetId, label);
			graph.Edges.Add(edge);
		}
		else if (update)
		{
			existingEdge.Properties["weight"] = (int)existingEdge.Properties["weight"] + 1;
		}
	}

	// Regular expression pattern to match angle brackets and their content
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex AngleBracketsRegex();
}
