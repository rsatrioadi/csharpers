using CSharPers.LPG;
using Microsoft.Build.Locator;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace CSharPers;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        // Define the input file argument
        var solutionPathArgument = new Argument<string>("solution", "Path to the solution file (.sln)");

        // Define the output file option
        var outputOption = new Option<string?>(
            ["--output", "-o"],
            "Path to the output file (optional). If omitted, output will be written to stdout.");

        // Create a root command
        var rootCommand = new RootCommand("CSharPers: A tool to process and analyze C# solutions.")
        {
            solutionPathArgument,
            outputOption
        };

        // Define the handler
        rootCommand.Handler = CommandHandler.Create<string, string?>(async (solution, output) =>
        {
            MSBuildLocator.RegisterDefaults();

            Graph graph = new(solution);

            await GraphProcessor.ProcessSolutionAsync(solution, graph);

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
                    Console.WriteLine($"Error writing to file: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine(graphOutput);
            }
        });

        // Invoke the command
        await rootCommand.InvokeAsync(args);
    }
}