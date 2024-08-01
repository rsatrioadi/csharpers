using CSharPers.LPG;
using Microsoft.Build.Locator;

namespace CSharPers;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: CSharPers <path-to-solution>");
            return;
        }

        var solutionPath = args[0];

        MSBuildLocator.RegisterDefaults();

        Graph graph = new(solutionPath);

        await GraphProcessor.ProcessSolutionAsync(solutionPath, graph);

        Console.WriteLine(graph.ToString());
    }
}