using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharPers.Metrics
{
    public static class HalsteadMetricsCalculator
    {
        /// <summary>
        /// Analyze every method in the compilation, then aggregate per class and namespace.
        /// </summary>
        public static List<HalsteadMetrics> Analyze(Compilation compilation)
        {
            var methodMetrics = new List<HalsteadMetrics>();

            // 1) Per‐method metrics
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root  = tree.GetRoot();

                var methods = root.DescendantNodes()
                                  .OfType<MethodDeclarationSyntax>()
                                  .Where(m => model.GetDeclaredSymbol(m) is not null);

                foreach (var mdecl in methods)
                {
                    try
                    {
                        var msym = model.GetDeclaredSymbol(mdecl);
                        var pick = GatherOperatorsAndOperands(mdecl, model);

                        var hm = new HalsteadMetrics(
                            elementId: msym!.ToString() ?? msym.ToDisplayString(),
                            elementKind: "method",
                            n1: pick.Operators.Count,
                            n2: pick.Operands.Count,
                            N1: pick.TotalOperators,
                            N2: pick.TotalOperands
                        );

                        methodMetrics.Add(hm);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

            // 2) Per‐class aggregation
            var classMetrics = methodMetrics
                .GroupBy(m => {
                    var dot = m.ElementId.IndexOf('(');
                    var full = (dot >= 0 ? m.ElementId[..dot] : m.ElementId);
                    return full[..full.LastIndexOf('.')];
                })
                .Select(g => HalsteadMetrics.Aggregate(
                    elementId:   g.Key,
                    elementKind: "class",
                    list:        g))
                .ToList();

            // 3) Per‐namespace aggregation
            var namespaceMetrics = classMetrics
                .GroupBy(m => {
                    var lastDot = m.ElementId.LastIndexOf('.');
                    return lastDot >= 0 ? m.ElementId[..lastDot] : "<global>";
                })
                .Select(g => HalsteadMetrics.Aggregate(
                    elementId:   g.Key,
                    elementKind: "namespace",
                    list:        g))
                .ToList();

            // Return in order: methods, then classes, then namespaces
            var all = new List<HalsteadMetrics>();
            all.AddRange(methodMetrics);
            all.AddRange(classMetrics);
            all.AddRange(namespaceMetrics);
            return all;
        }

        private class TokenCount
        {
            public readonly HashSet<string> Operators = [];
            public readonly HashSet<string> Operands  = [];
            public int TotalOperators;
            public int TotalOperands;
        }

        private static TokenCount GatherOperatorsAndOperands(
            MethodDeclarationSyntax method,
            SemanticModel model)
        {
            var tc = new TokenCount();

            // --- Operands ---

            // 1) this references
            foreach (var unused in method.DescendantNodes().OfType<ThisExpressionSyntax>())
            {
                tc.Operands.Add("this");
                tc.TotalOperands++;
            }

            // 2) identifier usages (reads + writes)
            foreach (var id in method.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                tc.Operands.Add(id.Identifier.ValueText);
                tc.TotalOperands++;
            }

            // 3) parameters
            foreach (var p in method.ParameterList.Parameters)
            {
                tc.Operands.Add(p.Identifier.ValueText);
                tc.TotalOperands++;
            }

            // 4) type names
            foreach (var ty in method.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var sym = model.GetSymbolInfo(ty).Symbol;
                if (sym is ITypeSymbol)
                {
                    tc.Operands.Add(ty.Identifier.ValueText);
                    tc.TotalOperands++;
                }
            }

            // 5) literals
            foreach (var lit in method.DescendantNodes().OfType<LiteralExpressionSyntax>())
            {
                tc.Operands.Add(lit.Token.ValueText);
                tc.TotalOperands++;
            }

            // --- Operators ---

            // a) keywords/control flow
            var keywords = new[] {
                SyntaxKind.IfKeyword, SyntaxKind.ForKeyword, SyntaxKind.WhileKeyword,
                SyntaxKind.DoKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.TryKeyword,
                SyntaxKind.CatchKeyword, SyntaxKind.CaseKeyword,
                SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword,
                SyntaxKind.ReturnKeyword, SyntaxKind.ThrowKeyword, SyntaxKind.DefaultKeyword
            };
            foreach (var k in keywords)
            {
                var nodes = method.DescendantTokens().Where(t => t.IsKind(k));
                foreach (var t in nodes)
                {
                    tc.Operators.Add(t.Text);
                    tc.TotalOperators++;
                }
            }

            // b) binary/unary operators
            foreach (var bop in method.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                var op = bop.OperatorToken.Text;
                tc.Operators.Add(op);
                tc.TotalOperators++;
            }
            foreach (var uop in method.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
            {
                var op = uop.OperatorToken.Text;
                tc.Operators.Add(op);
                tc.TotalOperators++;
            }
            foreach (var uop in method.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
            {
                var op = uop.OperatorToken.Text;
                tc.Operators.Add(op);
                tc.TotalOperators++;
            }

            // c) assignment
            foreach (var a in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var op = a.OperatorToken.Text;
                tc.Operators.Add(op);
                tc.TotalOperators++;
            }

            // d) logical/conditional
            foreach (var c in method.DescendantNodes().OfType<ConditionalExpressionSyntax>())
            {
                // ? and :
                tc.Operators.Add("?");
                tc.TotalOperators++;
                tc.Operators.Add(":");
                tc.TotalOperators++;
            }

            return tc;
        }
    }
}
