# CSharPers

tl;dr: Similar to [javapers](https://github.com/rsatrioadi/javapers) but for C#.

**CSharPers** processes C# codebases and produces a graph-based
representation of the various components within them — classes,
interfaces, methods, fields, parameters, and their relationships. The
tool leverages Roslyn's compiler APIs to extract semantic information
and constructs a graph where each node represents a C# symbol (e.g.
classes, methods, parameters) and each edge represents a relationship
between those symbols (e.g. method calling, inheritance, field usage).

## Two indexing modes

CSharPers ships two extractors. Both produce the same SABO-shaped LPG
graph; they differ only in how they discover source.

| Command           | Input                             | Use when                                                                 |
|-------------------|-----------------------------------|--------------------------------------------------------------------------|
| `index`           | A directory of `.cs` files        | Unity projects, ad-hoc C# trees, anywhere `.sln` / `.csproj` is missing  |
| `index-solution`  | A `.sln` file (MSBuild evaluated) | You have a buildable solution and want full reference resolution         |

The source-only `index` mode does **not** require MSBuild and does **not**
attempt to read `.csproj` files. It builds a single ad-hoc Roslyn
`CSharpCompilation` from every `.cs` file under the input directory.

## Prerequisites

- **.NET SDK 8.0**
- Roslyn — pulled in automatically as a NuGet dependency.
- MSBuild — required only if you use `index-solution` (legacy mode).

## Building

```bash
git clone https://github.com/rsatrioadi/csharpers.git
cd csharpers
dotnet build
```

## Usage

### Source-only mode (recommended for Unity / no-solution trees)

```bash
CSharPers index <directory> [options]
```

Options:

- `--name <project-name>`   Override the project node's `simpleName` and id (default: directory basename).
- `--ref <dll-path>`        Add an extra reference DLL. Repeatable. Use this to point at `UnityEngine.dll`, NuGet artifacts, etc., when you want external types to resolve.
- `--exclude <glob>`        Exclude paths matching the glob (relative to the input directory). Repeatable.
- `-o`, `--output <file>`   Write output to a file instead of stdout.
- `-e`, `--include-external` Include symbols whose `Locations` are not in source.

By default, the walker excludes `bin`, `obj`, `Library`, `Temp`,
`.git`, `.vs`, `.idea`, `node_modules`, `packages`, plus
`*.Designer.cs` and `*.g.cs`.

Examples:

```bash
# Index a Unity Assets folder
CSharPers index ./MyUnityProject/Assets/Scripts -o graph.json

# Same, but resolve UnityEngine references
CSharPers index ./MyUnityProject/Assets/Scripts \
    --ref ./MyUnityProject/Library/ScriptAssemblies/UnityEngine.dll \
    -o graph.json

# Override the project name and exclude a tests subtree
CSharPers index ./src --name MyApp --exclude "Tests/**" -o graph.json
```

### Solution mode (legacy)

```bash
CSharPers index-solution <path-to-solution.sln> [-o <file>] [-e]
```

This loads the solution via `MSBuildWorkspace`, evaluates each project,
and uses the project's full reference closure for binding. Use it when
you need the most accurate cross-reference graph.

### Output

The resulting JSON can be visualized in
[ClassViz](https://satrio.rukmono.id/classviz)
([alternate link](https://rsatrioadi.github.io/classviz)).

## Architecture

The two extractors share an interface so that future hosts (a server,
LSP integration, etc.) can dispatch over them:

```csharp
public interface IGraphExtractor
{
    Task<Graph> ExtractAsync();
}
```

- `SourceOnlyCSharpGraphExtractor` — directory in, ad-hoc `CSharpCompilation`.
- `MSBuildSolutionGraphExtractor` — `.sln` in, MSBuild-evaluated workspace.

A small parity report comparing the two on this repo is at
[`tests/parity-report.md`](tests/parity-report.md). A minimal end-to-end
fixture lives at [`tests/fixtures/SimpleApp/`](tests/fixtures/SimpleApp).

## Customization

- Modify the extractors to add custom properties or edge types.
- Extend graph processing to support additional C# constructs or relationships.
- Change the output format by adding a new codec under `CSharPers/LPG`.

## Contributing

We welcome contributions! Feel free to submit issues and pull requests.
