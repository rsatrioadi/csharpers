using System.Text;
using System.Text.RegularExpressions;
using CSharPers.LPG;
using CSharPers.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharPers.Extractor;

public class SourceOnlyCSharpGraphExtractor(
    string directory,
    string projectName,
    IReadOnlyList<string> extraReferenceDlls,
    IReadOnlyList<string> excludeGlobs,
    bool includeExternal) : IGraphExtractor
{
    private static readonly string[] DefaultExcludeDirs =
    {
        "bin", "obj",
        "Library", "Temp",
        ".git", ".vs", ".idea",
        "node_modules", "packages"
    };

    private static readonly string[] DefaultExcludeFileGlobs =
    {
        "*.Designer.cs", "*.g.cs"
    };

    public Task<Graph> ExtractAsync()
    {
        var rootDir = Path.GetFullPath(directory);
        var graph = new Graph(projectName);

        // 1) Project node
        var projectNode = new Node(projectName, "Project")
        {
            Properties =
            {
                ["simpleName"] = projectName,
                ["qualifiedName"] = rootDir,
                ["kind"] = "project"
            }
        };
        graph.Nodes.Add(projectNode);

        // 2) Discover .cs files and build single ad-hoc compilation
        var csFiles = DiscoverCSharpFiles(rootDir, excludeGlobs);
        var trees = csFiles
            .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p))
            .ToList();

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        foreach (var dll in extraReferenceDlls)
            refs.Add(MetadataReference.CreateFromFile(dll));

        var compilation = CSharpCompilation.Create(
            assemblyName: projectName,
            syntaxTrees: trees,
            references: refs,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable));

        // 3) Filesystem nodes
        var fileNodes = new Dictionary<string, Node>();
        var folderNodes = new Dictionary<string, Node>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var path = tree.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

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

            graph.Edges.Add(new Edge(folderNodes[dir].Id, fileNodes[path].Id, "contains"));
        }

        foreach (var folder in folderNodes.Values)
            graph.Edges.Add(new Edge(projectNode.Id, folder.Id, "includes"));

        // 4) Scopes (namespaces)
        var scopeNodes = new Dictionary<INamespaceSymbol, Node>(SymbolEqualityComparer.Default);
        ProcessNamespace(compilation.GlobalNamespace, null);

        void ProcessNamespace(INamespaceSymbol ns, INamespaceSymbol? parent)
        {
            if (!(ns.IsGlobalNamespace && parent == null))
            {
                if (!includeExternal && !ns.Locations.Any(loc => loc.IsInSource))
                    return;
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

                    if (parent != null && scopeNodes.TryGetValue(parent, out var pNode))
                        graph.Edges.Add(new Edge(pNode.Id, n.Id, "encloses"));
                }
            }

            foreach (var child in ns.GetNamespaceMembers())
                ProcessNamespace(child, ns);
        }

        // 5) Types
        var typeNodes = new Dictionary<INamedTypeSymbol, Node>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) continue;
                if (typeNodes.ContainsKey(sym)) continue;
                if (!includeExternal && !sym.Locations.Any(loc => loc.IsInSource)) continue;

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

                var fp = decl.SyntaxTree.FilePath;
                if (fileNodes.TryGetValue(fp, out var fnode))
                    graph.Edges.Add(new Edge(fnode.Id, n.Id, "declares"));

                if (scopeNodes.TryGetValue(sym.ContainingNamespace, out var sScope))
                    graph.Edges.Add(new Edge(sScope.Id, n.Id, "encloses"));
            }
        }

        // 6) Inheritance & nested
        foreach (var (key, tNode) in typeNodes)
        {
            foreach (var nested in key.GetTypeMembers())
                if (typeNodes.TryGetValue(nested, out var child))
                    graph.Edges.Add(new Edge(tNode.Id, child.Id, "encloses"));

            var bt = key.BaseType;
            if (bt != null && bt.SpecialType != SpecialType.System_Object
                           && typeNodes.TryGetValue(bt, out var baseNode))
                graph.Edges.Add(new Edge(tNode.Id, baseNode.Id, "specializes"));

            foreach (var iface in key.Interfaces)
                if (typeNodes.TryGetValue(iface, out var iNode))
                    graph.Edges.Add(new Edge(tNode.Id, iNode.Id, "specializes"));
        }

        // 7) Operations & variables
        var methodNodes = new Dictionary<IMethodSymbol, Node>(SymbolEqualityComparer.Default);
        var fieldNodes = new Dictionary<IFieldSymbol, Node>(SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            // Fields
            foreach (var fdecl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            foreach (var v in fdecl.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(v) is not IFieldSymbol fsym) continue;
                if (fieldNodes.ContainsKey(fsym)) continue;
                if (!includeExternal && !fsym.Locations.Any(loc => loc.IsInSource)) continue;

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

                if (typeNodes.TryGetValue(fsym.ContainingType, out var pt))
                    graph.Edges.Add(new Edge(pt.Id, fn.Id, "encapsulates"));

                if (fsym.Type is INamedTypeSymbol fnt && typeNodes.TryGetValue(fnt, out var typedNode))
                    graph.Edges.Add(new Edge(fn.Id, typedNode.Id, "typed"));
            }

            // Methods, constructors, operators, conversion operators, destructors
            foreach (var mdecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(mdecl) is not IMethodSymbol msym) continue;
                if (methodNodes.ContainsKey(msym)) continue;
                if (!includeExternal && !msym.Locations.Any(loc => loc.IsInSource)) continue;

                var idm = msym.ToString() ?? msym.Name + msym.Parameters;
                var mkind = msym.MethodKind switch
                {
                    MethodKind.Constructor => "constructor",
                    MethodKind.StaticConstructor => "constructor",
                    MethodKind.Destructor => "destructor",
                    MethodKind.UserDefinedOperator => "operator",
                    MethodKind.Conversion => "conversion",
                    _ => "method"
                };
                var on = new Node(idm, "Operation")
                {
                    Properties =
                    {
                        ["simpleName"] = msym.Name,
                        ["qualifiedName"] = msym.ToString() ?? string.Empty,
                        ["kind"] = mkind,
                        ["visibility"] = msym.DeclaredAccessibility.ToString().ToLower(),
                        ["sourceText"] = mdecl.ToString().Trim(),
                        ["docComment"] = msym.GetDocumentationCommentXml() ?? ""
                    }
                };
                graph.Nodes.Add(on);
                methodNodes[msym] = on;

                if (typeNodes.TryGetValue(msym.ContainingType, out var pt2))
                    graph.Edges.Add(new Edge(pt2.Id, on.Id, "encapsulates"));

                if (!msym.ReturnsVoid
                    && msym.ReturnType is INamedTypeSymbol rnt
                    && typeNodes.TryGetValue(rnt, out var rNode))
                    graph.Edges.Add(new Edge(on.Id, rNode.Id, "returns"));

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

                    if (p.Type is INamedTypeSymbol pnt && typeNodes.TryGetValue(pnt, out var ptNode))
                        graph.Edges.Add(new Edge(pn.Id, ptNode.Id, "typed"));
                }
            }
        }

        // 8) invokes / instantiates / uses / overrides
        foreach (var (key, mNode) in methodNodes)
        {
            var sref = key.DeclaringSyntaxReferences.FirstOrDefault();
            if (sref?.GetSyntax() is not BaseMethodDeclarationSyntax decl) goto Overrides;

            var model = compilation.GetSemanticModel(decl.SyntaxTree);

            foreach (var inv in decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv).Symbol is IMethodSymbol target
                    && methodNodes.TryGetValue(target, out var tn))
                    graph.Edges.Add(new Edge(mNode.Id, tn.Id, "invokes"));
            }

            foreach (var oc in decl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (model.GetTypeInfo(oc).Type is INamedTypeSymbol ocType
                    && typeNodes.TryGetValue(ocType, out var tn2))
                    graph.Edges.Add(new Edge(mNode.Id, tn2.Id, "instantiates"));
            }

            foreach (var ma in decl.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (model.GetSymbolInfo(ma).Symbol is IFieldSymbol fs
                    && fieldNodes.TryGetValue(fs, out var fn))
                    graph.Edges.Add(new Edge(mNode.Id, fn.Id, "uses"));
            }

            Overrides:
            if (key.OverriddenMethod is { } sup
                && methodNodes.TryGetValue(sup, out var supNode))
                graph.Edges.Add(new Edge(mNode.Id, supNode.Id, "overrides"));

            foreach (var ifaceImpl in key.ExplicitInterfaceImplementations)
                if (methodNodes.TryGetValue(ifaceImpl, out var ifaceNode))
                    graph.Edges.Add(new Edge(mNode.Id, ifaceNode.Id, "overrides"));
        }

        // 9) NumMethods metric
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
            graph.Edges.Add(new Edge(kv.Value.Id, nmNode.Id, "measures")
            {
                Properties = { ["value"] = count }
            });
        }

        // 10) NumStatements metric
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
            var decl = msym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as BaseMethodDeclarationSyntax;
            var stmts = decl?.Body?.Statements.Count ?? 0;

            graph.Edges.Add(new Edge(on.Id, nsNode.Id, "measures")
            {
                Properties = { ["value"] = stmts }
            });
        }

        // 11) Halstead
        var hmNode = new Node($"{projectName}#HalsteadMetrics", "Metric")
        {
            Properties =
            {
                ["simpleName"] = "HalsteadMetrics",
                ["qualifiedName"] = "HalsteadMetrics",
                ["kind"] = "metric"
            }
        };
        graph.Nodes.Add(hmNode);

        var halList = HalsteadMetricsCalculator.Analyze(compilation);
        foreach (var m in halList)
        {
            var source = graph.Nodes.FirstOrDefault(n =>
                n.Properties.TryGetValue("qualifiedName", out var qn) &&
                qn.Equals(m.ElementId));
            if (source == null) continue;

            var e = new Edge(source.Id, hmNode.Id, "measures");
            foreach (var kv in m.ToDictionary().Where(kv => kv.Key != "id" && kv.Key != "kind"))
                e.Properties[kv.Key] = kv.Value;
            graph.Edges.Add(e);
        }

        return Task.FromResult(graph);
    }

    private static List<string> DiscoverCSharpFiles(string root, IReadOnlyList<string> userExcludeGlobs)
    {
        var defaultDirs = new HashSet<string>(DefaultExcludeDirs, StringComparer.OrdinalIgnoreCase);
        var fileGlobs = DefaultExcludeFileGlobs.Select(GlobToRegex).ToList();
        var userGlobs = userExcludeGlobs.Select(GlobToRegex).ToList();

        var results = new List<string>();
        Walk(root);
        return results;

        void Walk(string dir)
        {
            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir, "*.cs");
            }
            catch (UnauthorizedAccessException) { return; }
            catch (DirectoryNotFoundException) { return; }

            foreach (var f in files)
            {
                var fname = Path.GetFileName(f);
                if (fileGlobs.Any(rx => rx.IsMatch(fname))) continue;

                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (userGlobs.Any(rx => rx.IsMatch(rel))) continue;

                results.Add(f);
            }

            foreach (var sd in subdirs)
            {
                var name = Path.GetFileName(sd);
                if (defaultDirs.Contains(name)) continue;

                var rel = Path.GetRelativePath(root, sd).Replace('\\', '/');
                if (userGlobs.Any(rx => rx.IsMatch(rel))) continue;

                Walk(sd);
            }
        }
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i++;
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }
}
