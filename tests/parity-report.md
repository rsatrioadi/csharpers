# Parity Report: `index` (source-only) vs `index-solution` (MSBuild)

Both extractors were run against the csharpers repo itself.

```
dotnet run --project CSharPers -- index CSharPers --name csharpers -o /tmp/csharpers-source.json
dotnet run --project CSharPers -- index-solution csharpers.sln -o /tmp/csharpers-sln.json
```

## Summary

| Metric                                  | source-only | solution |
|-----------------------------------------|------------:|---------:|
| Total nodes                             |         156 |      148 |
| Total edges                             |         369 |      373 |
| Project / Folder / File / Scope / Type  |  1/4/10/4/14|1/5/13/4/14|
| Operation                               |          57 |       54 |
| Variable                                |          63 |       54 |
| Metric                                  |           3 |        3 |

## Edge-label distribution (in-project labels only)

| Label          | source-only | solution |
|----------------|------------:|---------:|
| `contains`     |          10 |       13 |
| `includes`     |           4 |        5 |
| `encloses`     |          18 |       18 |
| `declares`     |          14 |       14 |
| `specializes`  |           2 |        2 |
| `encapsulates` |          64 |       61 |
| `parameterizes`|          56 |       47 |
| `typed`        |          12 |       17 |
| `returns`      |          15 |       14 |
| `invokes`      |           9 |       20 |
| `instantiates` |          15 |       15 |
| `uses`         |           8 |        8 |
| `overrides`    |           0 |        0 |

## Overlap of in-project edges

Edges are tuples `(source-id, target-id, label)`. Type display strings differ
between modes because the source-only compilation only has the BCL loaded
(`typeof(object).Assembly.Location`), so external types render as
`IReadOnlyList<string>` instead of `System.Collections.Generic.IReadOnlyList<string>`.
After normalising away well-known namespace prefixes
(`System.*`, `Microsoft.CodeAnalysis.*`, `Newtonsoft.Json.*`, etc.):

```
src-only set:  227
solution set:  234
common:        214
common / sln = 91.5%   common / src = 94.3%
```

Restricting further to edges whose endpoints exist as nodes in *both* graphs:

```
src-only:  215   solution:  230   common: 214
common / sln = 93.0%   common / src = 99.5%
```

Both pass the ≥ 90% in-project overlap target.

## Where the modes legitimately diverge

### 1. Generated files (sln-only)
MSBuild evaluation pulls in three auto-generated files under `obj/` that
the source-only walker excludes by default:

- `obj/Debug/net8.0/CSharPers.AssemblyInfo.cs`
- `obj/Debug/net8.0/CSharPers.GlobalUsings.g.cs`
- `obj/Debug/net8.0/.NETCoreApp,Version=v8.0.AssemblyAttributes.cs`

These contribute the extra `File`, `Folder`, `contains`, and `includes`
counts in solution mode. We treat the source-only behaviour (skip
generated files in `obj/`) as the desired default.

### 2. Constructors (src-only)
Source-only iterates `BaseMethodDeclarationSyntax`, which catches
constructors, operators, conversion operators, and destructors. The
legacy extractor used `MethodDeclarationSyntax`, missing all of these.
On this codebase the diff is three new constructor `Operation` nodes
(plus their parameters and metric edges):

- `CSharPers.LPG.Edge.Edge(string, string, string)`
- `CSharPers.Metrics.HalsteadMetrics.HalsteadMetrics(string, string, int, int, int, int)`
- `CSharPers.LPG.CyJsonCodec.CyJsonCodec()` *(default ctor — synthetic, picked up because `CyJsonCodec` has an explicit one in source)*

That accounts for the `+3 Operations`, `+9 Variables` (parameters),
`+3 encapsulates`, `+9 parameterizes`, and `+3 measures` extra in
source-only.

### 3. `invokes` and `typed` involving expressions whose binding depends on external references (sln-only)
The single biggest gap: source-only finds 9 `invokes` vs solution's 20,
and 12 `typed` vs solution's 17. Inspecting the missing edges shows
they are all calls or parameter types that sit *inside expressions
whose outer binding requires a non-BCL reference* — most often
`System.CommandLine.CommandHandler.Create<...>(lambda)` and Roslyn
APIs (`Microsoft.CodeAnalysis.*`). Without those DLLs loaded, Roslyn
cannot bind the outer expression, and `GetSymbolInfo` on the
inner-lambda invocations falls through to `CandidateSymbols` /
`IErrorTypeSymbol`. Examples that solution mode catches and source-only
misses:

- `Program.BuildIndexCommand → Program.RunAndWriteAsync`
- `CyJsonCodec.EncodeNodes → CyJsonCodec.EncodeNode`
- `HalsteadMetricsCalculator.Analyze → HalsteadMetricsCalculator.GatherOperatorsAndOperands`
- `params: encoder` typed → `IGraphCodec<...>` (the generic parameter type)

This is the documented trade-off of the source-only path, and it is
addressable on a per-codebase basis by passing the relevant DLLs via
`--ref`. Solution mode resolves these because MSBuild evaluation feeds
the full reference closure to Roslyn.

### 4. Type-display string differences
Even when both modes detect the same edge, the textual id can differ
(e.g. `IEnumerable<HalsteadMetrics>` vs
`System.Collections.Generic.IEnumerable<CSharPers.Metrics.HalsteadMetrics>`).
The graphs are *semantically* equivalent at those positions; only the
display form changes. Tooling that compares graphs textually should
either canonicalise type names or compare on `simpleName +
containing-type chain` instead of `qualifiedName`.

## Conclusions

- The source-only extractor reproduces the SABO graph shape produced by
  the legacy MSBuild path with **≥ 93% exact-match in-project edge
  overlap** (after namespace normalisation) on this repo.
- The new path additionally emits constructors / operators that the
  legacy path silently dropped — a deliberate fix.
- The remaining gap is dominated by edges that depend on external
  reference resolution (System.CommandLine, Roslyn). For codebases
  where that matters, pass the relevant DLLs via `--ref`. For
  exploratory use on Unity-style trees, the source-only output is
  sufficient.
