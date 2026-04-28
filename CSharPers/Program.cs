using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using CSharPers.Extractor;
using CSharPers.LPG;
using Microsoft.Build.Locator;

namespace CSharPers;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("CSharPers: Process & analyze C# codebases");
        rootCommand.AddCommand(BuildIndexCommand());
        rootCommand.AddCommand(BuildIndexSolutionCommand());
        return await rootCommand.InvokeAsync(args);
    }

    private static Command BuildIndexCommand()
    {
        var directoryArgument = new Argument<string>("directory", "Path to a directory containing C# source files");

        var nameOption = new Option<string?>(
            aliases: ["--name"],
            description: "Override project name (defaults to directory basename)");

        var refOption = new Option<string[]>(
            aliases: ["--ref"],
            description: "Path to an additional reference DLL (repeatable)")
        {
            AllowMultipleArgumentsPerToken = false
        };
        refOption.SetDefaultValue(Array.Empty<string>());

        var excludeOption = new Option<string[]>(
            aliases: ["--exclude"],
            description: "Glob (relative to directory) to exclude (repeatable)")
        {
            AllowMultipleArgumentsPerToken = false
        };
        excludeOption.SetDefaultValue(Array.Empty<string>());

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "Path to the output file (optional). If omitted, output is written to stdout.");

        var externalOption = new Option<bool>(
            aliases: ["--include-external", "-e"],
            description: "Include external (non-source) types and members",
            getDefaultValue: () => false);

        var cmd = new Command("index", "Index a directory of C# source files (no .sln/.csproj required)")
        {
            directoryArgument, nameOption, refOption, excludeOption, outputOption, externalOption
        };

        cmd.Handler = CommandHandler.Create<string, string?, string[], string[], string?, bool>(
            async (directory, name, @ref, exclude, output, includeExternal) =>
            {
                var projectName = !string.IsNullOrWhiteSpace(name)
                    ? name!
                    : Path.GetFileName(Path.GetFullPath(directory.TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)));

                IGraphExtractor extractor = new SourceOnlyCSharpGraphExtractor(
                    directory,
                    projectName,
                    @ref ?? Array.Empty<string>(),
                    exclude ?? Array.Empty<string>(),
                    includeExternal);

                await RunAndWriteAsync(extractor, output);
            });

        return cmd;
    }

    private static Command BuildIndexSolutionCommand()
    {
        var solutionPathArgument = new Argument<string>("solution", "Path to the solution file (.sln)");

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "Path to the output file (optional). If omitted, output is written to stdout.");

        var externalOption = new Option<bool>(
            aliases: ["--include-external", "-e"],
            description: "Include external (non-source) types and members",
            getDefaultValue: () => false);

        var cmd = new Command("index-solution", "Index a C# solution via MSBuild (legacy mode)")
        {
            solutionPathArgument, outputOption, externalOption
        };

        cmd.Handler = CommandHandler.Create<string, string?, bool>(async (solution, output, includeExternal) =>
        {
            MSBuildLocator.RegisterDefaults();
            IGraphExtractor extractor = new MSBuildSolutionGraphExtractor(solution, includeExternal);
            await RunAndWriteAsync(extractor, output);
        });

        return cmd;
    }

    private static async Task RunAndWriteAsync(IGraphExtractor extractor, string? output)
    {
        Graph graph;
        try
        {
            graph = await extractor.ExtractAsync();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error during graph extraction: {ex.Message}");
            return;
        }

        var graphOutput = graph.ToString();

        if (!string.IsNullOrEmpty(output))
        {
            try
            {
                await File.WriteAllTextAsync(output, graphOutput);
                Console.WriteLine($"Graph successfully written to {output}");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error writing to file '{output}': {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine(graphOutput);
        }
    }
}
