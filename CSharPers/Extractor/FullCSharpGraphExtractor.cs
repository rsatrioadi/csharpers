using CSharPers.LPG;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace CSharPers.Extractor;

public static class FullCSharpGraphExtractor
{
    /// <summary>
    ///     Builds a Graph from a .sln file, mirroring the Java extractor’s schema:
    ///     Project → Folder → File,
    ///     Scope (namespace) → Type → Operation/Variable,
    ///     all logical edges (encloses, specializes, encapsulates, parameterizes, returns,
    ///     invokes, uses, instantiates, overrides),
    ///     plus NumMethods and NumStatements metric nodes.
    /// </summary>
    public static async Task<Graph> ExtractAsync(string solutionPath)
    {
        // 0) Prepare
        var projectName = Path.GetFileNameWithoutExtension(solutionPath);
        var graph = new Graph(projectName);

        // 1) Project node
        var projectNode = new Node(projectName, "Project")
        {
            Properties =
            {
                ["simpleName"] = projectName,
                ["qualifiedName"] = solutionPath,
                ["kind"] = "project"
            }
        };
        graph.Nodes.Add(projectNode);

        // 2) Open solution
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        // 2a) Filesystem: collect all file & folder nodes
        var fileNodes = new Dictionary<string, Node>();
        var folderNodes = new Dictionary<string, Node>();

        foreach (var proj in solution.Projects)
        {
            var comp = await proj.GetCompilationAsync();
            if (comp == null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                var path = tree.FilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                // --- File node
                if (!fileNodes.ContainsKey(path))
                {
                    var f = new Node(path, "File")
                    {
                        Properties =
                        {
                            ["simpleName"] = Path.GetFileName(path),
                            ["qualifiedName"] = path,
                            ["kind"] = "file"
                        }
                    };
                    graph.Nodes.Add(f);
                    fileNodes[path] = f;
                }

                // --- Folder node
                var dir = Path.GetDirectoryName(path)!;
                if (!folderNodes.ContainsKey(dir))
                {
                    var d = new Node(dir, "Folder")
                    {
                        Properties =
                        {
                            ["simpleName"] = Path.GetFileName(dir),
                            ["qualifiedName"] = dir,
                            ["kind"] = "folder"
                        }
                    };
                    graph.Nodes.Add(d);
                    folderNodes[dir] = d;
                }

                // --- contains edge
                graph.Edges.Add(new Edge(
                    folderNodes[dir].Id,
                    fileNodes[path].Id,
                    "contains"
                ));
            }
        }

        // 2b) project includes → all root folders
        foreach (var folder in folderNodes.Values)
            graph.Edges.Add(new Edge(
                projectNode.Id,
                folder.Id,
                "includes"
            ));

        // 3) SCOPES (namespaces)
        var scopeNodes = new Dictionary<INamespaceSymbol, Node>();
        foreach (var proj in solution.Projects)
        {
            var comp = await proj.GetCompilationAsync();
            if (comp == null) continue;
            ProcessNamespace(comp.GlobalNamespace, null);
        }

        void ProcessNamespace(INamespaceSymbol ns, INamespaceSymbol? parent)
        {
            if (ns.IsGlobalNamespace && parent == null)
            {
                // skip the global namespace node
            }
            else
            {
                var id = ns.ToString() ?? ns.ToDisplayString();
                if (!scopeNodes.ContainsKey(ns))
                {
                    var n = new Node(id, "Scope")
                    {
                        Properties =
                        {
                            ["simpleName"] = ns.Name,
                            ["qualifiedName"] = id,
                            ["kind"] = "namespace"
                        }
                    };
                    graph.Nodes.Add(n);
                    scopeNodes[ns] = n;

                    if (parent != null && scopeNodes.TryGetValue(parent, out var value))
                        graph.Edges.Add(new Edge(
                            value.Id,
                            n.Id,
                            "encloses"
                        ));
                }
            }

            foreach (var child in ns.GetNamespaceMembers())
                ProcessNamespace(child, ns);
        }

        // 4) TYPES (classes, interfaces, structs, enums)
        var typeNodes = new Dictionary<INamedTypeSymbol, Node>();
        foreach (var proj in solution.Projects)
        {
            var comp = await proj.GetCompilationAsync();
            if (comp == null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                var model = comp.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();

                foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) continue;
                    if (typeNodes.ContainsKey(sym)) continue;

                    // node
                    var id = sym.ToString() ?? sym.ToDisplayString();
                    var kind = sym.TypeKind switch
                    {
                        TypeKind.Interface => "interface",
                        TypeKind.Enum => "enum",
                        TypeKind.Struct => "struct",
                        TypeKind.Class => sym.IsAbstract ? "abstract class" : "class",
                        _ => sym.TypeKind.ToString().ToLower()
                    };
                    var n = new Node(id, "Type")
                    {
                        Properties =
                        {
                            ["simpleName"] = sym.Name,
                            ["qualifiedName"] = id,
                            ["kind"] = kind,
                            ["visibility"] = sym.DeclaredAccessibility.ToString().ToLower(),
                            ["docComment"] = sym.GetDocumentationCommentXml() ?? ""
                        }
                    };
                    graph.Nodes.Add(n);
                    typeNodes[sym] = n;

                    // File → declares → Type
                    var fp = decl.SyntaxTree.FilePath;
                    if (fileNodes.TryGetValue(fp, out var fnode))
                        graph.Edges.Add(new Edge(fnode.Id, n.Id, "declares"));

                    // Scope → encloses → Type
                    if (scopeNodes.TryGetValue(sym.ContainingNamespace, out var sScope))
                        graph.Edges.Add(new Edge(sScope.Id, n.Id, "encloses"));
                }
            }
        }

        // 5) INHERITANCE & NESTED TYPES
        foreach (var (key, tNode) in typeNodes)
        {
            // nested
            foreach (var nested in key.GetTypeMembers())
                if (typeNodes.TryGetValue(nested, out var child))
                    graph.Edges.Add(new Edge(tNode.Id, child.Id, "encloses"));

            // base type
            var bt = key.BaseType;
            if (bt != null && bt.SpecialType != SpecialType.System_Object
                           && typeNodes.TryGetValue(bt, out var baseNode))
                graph.Edges.Add(new Edge(tNode.Id, baseNode.Id, "specializes"));

            // interfaces
            foreach (var iface in key.Interfaces)
                if (typeNodes.TryGetValue(iface, out var iNode))
                    graph.Edges.Add(new Edge(tNode.Id, iNode.Id, "specializes"));
        }

        // 6) OPERATIONS & VARIABLES
        var methodNodes = new Dictionary<IMethodSymbol, Node>();
        var fieldNodes = new Dictionary<IFieldSymbol, Node>();

        foreach (var proj in solution.Projects)
        {
            var comp = await proj.GetCompilationAsync();
            if (comp == null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                var model = comp.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();

                // --- Fields
                foreach (var fdecl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                foreach (var v in fdecl.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(v) is not IFieldSymbol fsym) continue;
                    if (fieldNodes.ContainsKey(fsym)) continue;

                    var idf = $"{fsym.ContainingType}.{fsym.Name}";
                    var fn = new Node(idf, "Variable")
                    {
                        Properties =
                        {
                            ["simpleName"] = fsym.Name,
                            ["qualifiedName"] = fsym.ToString() ?? string.Empty,
                            ["kind"] = "field",
                            ["visibility"] = fsym.DeclaredAccessibility.ToString().ToLower(),
                            ["sourceText"] = fdecl.ToString().Trim(),
                            ["docComment"] = fsym.GetDocumentationCommentXml() ?? ""
                        }
                    };
                    graph.Nodes.Add(fn);
                    fieldNodes[fsym] = fn;

                    // Type —encapsulates→ Variable
                    if (typeNodes.TryGetValue(fsym.ContainingType, out var pt))
                        graph.Edges.Add(new Edge(pt.Id, fn.Id, "encapsulates"));

                    // Variable —typed→ Type
                    var ftqn = fsym.Type.ToString();
                    var targetType = typeNodes.Keys.FirstOrDefault(t => t.ToString() == ftqn);
                    if (targetType != null)
                        graph.Edges.Add(new Edge(fn.Id, typeNodes[targetType].Id, "typed"));
                }

                // --- Methods & Constructors
                foreach (var mdecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(mdecl) is not IMethodSymbol msym) continue;
                    if (methodNodes.ContainsKey(msym)) continue;

                    // Operation node
                    var idm = msym.ToString() ?? msym.Name + msym.Parameters;
                    var on = new Node(idm, "Operation")
                    {
                        Properties =
                        {
                            ["simpleName"] = msym.Name,
                            ["qualifiedName"] = msym.ToString() ?? string.Empty,
                            ["kind"] = msym.MethodKind switch
                            {
                                MethodKind.Constructor => "constructor",
                                _ => "method"
                            },
                            ["visibility"] = msym.DeclaredAccessibility.ToString().ToLower(),
                            ["sourceText"] = mdecl.ToString().Trim(),
                            ["docComment"] = msym.GetDocumentationCommentXml() ?? ""
                        }
                    };
                    graph.Nodes.Add(on);
                    methodNodes[msym] = on;

                    // Type —encapsulates→ Operation
                    if (typeNodes.TryGetValue(msym.ContainingType, out var pt2))
                        graph.Edges.Add(new Edge(pt2.Id, on.Id, "encapsulates"));

                    // Returns
                    if (!msym.ReturnsVoid)
                    {
                        var rtn = typeNodes.Keys.FirstOrDefault(t => t.ToString() == msym.ReturnType.ToString());
                        if (rtn != null)
                            graph.Edges.Add(new Edge(on.Id, typeNodes[rtn].Id, "returns"));
                    }

                    // Parameters (invert)
                    for (var i = 0; i < msym.Parameters.Length; i++)
                    {
                        var p = msym.Parameters[i];
                        var idp = $"{idm}:param:{p.Name}";
                        var pn = new Node(idp, "Variable")
                        {
                            Properties =
                            {
                                ["simpleName"] = p.Name,
                                ["qualifiedName"] = p.ToString() ?? string.Empty,
                                ["kind"] = "parameter",
                                ["visibility"] = "public",
                                ["parameterPosition"] = i
                            }
                        };
                        graph.Nodes.Add(pn);
                        graph.Edges.Add(new Edge(pn.Id, on.Id, "parameterizes"));

                        // param —typed→ Type
                        var ptqn = p.Type.ToString();
                        var ptbs = typeNodes.Keys.FirstOrDefault(t => t.ToString() == ptqn);
                        if (ptbs != null)
                            graph.Edges.Add(new Edge(pn.Id, typeNodes[ptbs].Id, "typed"));
                    }
                }
            }
        }

        // 7) INVOKES / INSTANTIATES / USES / OVERRIDES
        foreach (var proj in solution.Projects)
        {
            var comp = await proj.GetCompilationAsync();
            if (comp == null) continue;

            foreach (var tree in comp.SyntaxTrees)
            {
                var model = comp.GetSemanticModel(tree);
                await tree.GetRootAsync();

                foreach (var (key, mNode) in methodNodes)
                {
                    if (await key.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntaxAsync()! is not
                        MethodDeclarationSyntax decl) continue;

                    // invokes
                    foreach (var inv in decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
                        try
                        {
                            if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol target
                                && methodNodes.TryGetValue(target, out var tn))
                                graph.Edges.Add(new Edge(mNode.Id, tn.Id, "invokes"));
                        }
                        catch (Exception)
                        {
                        }

                    // instantiates
                    foreach (var oc in decl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                        try
                        {
                            var typeInfo = model.GetTypeInfo(oc).Type;
                            if (typeInfo != null
                                && typeNodes.TryGetValue((typeInfo as INamedTypeSymbol)!, out var tn2))
                                graph.Edges.Add(new Edge(mNode.Id, tn2.Id, "instantiates"));
                        }
                        catch (Exception)
                        {
                        }

                    // uses
                    foreach (var ma in decl.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                        try
                        {
                            if (model.GetSymbolInfo(ma).Symbol is IFieldSymbol fs
                                && fieldNodes.TryGetValue(fs, out var fn))
                                graph.Edges.Add(new Edge(mNode.Id, fn.Id, "uses"));
                        }
                        catch (Exception)
                        {
                        }

                    // overrides
                    if (key.OverriddenMethod is { } sup
                        && methodNodes.TryGetValue(sup, out var supNode))
                        graph.Edges.Add(new Edge(mNode.Id, supNode.Id, "overrides"));
                }
            }
        }

        // 8) METRICS: NumMethods & NumStatements
        // --- NumMethods (one global Metric node)
        var nmNode = new Node($"{projectName}#NumMethods", "Metric")
        {
            Properties =
            {
                ["simpleName"] = "NumMethods",
                ["qualifiedName"] = "Number of Methods",
                ["kind"] = "metric"
            }
        };
        graph.Nodes.Add(nmNode);

        foreach (var kv in typeNodes)
        {
            var count = methodNodes.Keys.Count(m => SymbolEqualityComparer.Default.Equals(m.ContainingType, kv.Key));
            var e = new Edge(kv.Value.Id, nmNode.Id, "measures")
            {
                Properties =
                {
                    ["value"] = count
                }
            };
            graph.Edges.Add(e);
        }

        // --- NumStatements (one global Metric node)
        var nsNode = new Node($"{projectName}#NumStatements", "Metric")
        {
            Properties =
            {
                ["simpleName"] = "NumStatements",
                ["qualifiedName"] = "Number of Statements",
                ["kind"] = "metric"
            }
        };
        graph.Nodes.Add(nsNode);

        foreach (var kv in methodNodes)
        {
            var msym = kv.Key;
            var on = kv.Value;
            var decl =
                await msym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntaxAsync()! as MethodDeclarationSyntax;
            var stmts = decl?.Body?.Statements.Count ?? 0;

            var e = new Edge(on.Id, nsNode.Id, "measures")
            {
                Properties =
                {
                    ["value"] = stmts
                }
            };
            graph.Edges.Add(e);
        }

        return graph;
    }
}