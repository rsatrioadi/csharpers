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
        // Define the input file argument
        var solutionPathArgument = new Argument<string>("solution", "Path to the solution file (.sln)");

        var outputOption = new Option<string?>(
            aliases: ["--output", "-o"],
            description: "Path to the output file (optional). If omitted, output will be written to stdout.");

        var externalOption = new Option<bool>(
            aliases: ["--include-external", "-e"],
            description: "Include external (non-source) types and members",
            getDefaultValue: () => false
        );

        var rootCommand = new RootCommand("CSharPers: Process & analyze C# solutions")
        {
            solutionPathArgument, outputOption, externalOption
        };

        rootCommand.Handler = CommandHandler.Create<string, string?, bool>(async (solution, output, external) =>
        {
            MSBuildLocator.RegisterDefaults();

            // Extract the graph using our new extractor
            Graph graph;
            try
            {
                graph = await FullCSharpGraphExtractor.ExtractAsync(solution, external);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error during graph extraction: {ex.Message}");
                return;
            }

            // Serialize graph to string
            var graphOutput = graph.ToString();

            // Write to file or stdout
            if (!string.IsNullOrEmpty(output))
                try
                {
                    await File.WriteAllTextAsync(output, graphOutput);
                    Console.WriteLine($"Graph successfully written to {output}");
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Error writing to file '{output}': {ex.Message}");
                }
            else
                Console.WriteLine(graphOutput);
        });

        // Invoke the parser
        return await rootCommand.InvokeAsync(args);
    }
}