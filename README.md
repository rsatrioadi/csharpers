# CSharPers

tl;dr: Similar to [javapers](https://github.com/rsatrioadi/javapers) but for C#.

**CSharPers** is a tool designed to process and analyze C# solutions, creating a graph-based representation of the various components within the solution. This includes classes, interfaces, methods, fields, parameters, and their relationships. The tool leverages Roslyn's compiler APIs to extract semantic information and constructs a graph where each node represents a C# symbol (e.g., classes, methods, parameters) and each edge represents a relationship between those symbols (e.g., method calling, inheritance, field usage).

## Features
- **Solution Parsing**: Parses C# solutions (.sln files) to build a graph of the project's structure.
- **Node Representation**: Nodes represent various C# constructs such as classes, methods, fields, and parameters.
- **Edge Representation**: Edges represent relationships like inheritance, method calls, field usage, etc.
- **Output Options**: Results can be output to the console or saved to a file in JSON format for further analysis.

## Prerequisites

Before running **CSharPers**, ensure you have the following installed:

- **.NET SDK 6.0 or later**
- **Roslyn (C# compiler)** – This is automatically used by the project for semantic analysis.
- **MSBuild** – Required to process C# projects, integrated via the MSBuildLocator class.

## Installation

Clone the repository and restore the project dependencies:

```bash
git clone https://github.com/rsatrioadi/csharpers.git
cd csharpers
dotnet restore
```

You can also install `System.CommandLine` for command-line argument parsing:

```bash
dotnet add package System.CommandLine
```

## Usage

### Command-Line Arguments

1. **Basic Usage**:
   This will analyze a solution and output the graph to the console.

   ```bash
   CSharPers path\to\solution.sln
   ```

2. **Output to File**:
   To specify an output file where the graph will be written (in JSON format), use the `-o` option:

   ```bash
   CSharPers path\to\solution.sln -o path\to\output.json
   ```

   or

   ```bash
   CSharPers -o path\to\output.json path\to\solution.sln
   ```

### Options
- **`-o` or `--output`**: Specifies the path to the output file. If not provided, the output is printed to the console.

### Output
The output of the tool is a JSON representation of the parsed C# solution. Each node in the graph represents a C# element such as a class, method, field, etc., while edges represent relationships between these elements.

For example, a class with methods, fields, and parameters would generate nodes for the class, methods, fields, and parameters, with edges indicating method invocation, field usage, and more.

The resulting JSON file (produced using `-o` or redirecting console output to a file) can be visualized in [ClassViz](https://satrio.rukmono.id/classviz) ([alternate link](https://rsatrioadi.github.io/classviz)).

## Customization
The tool is designed to be easily extendable. You can:
- Modify the `NodeFactory` to add custom properties or edge types.
- Extend the graph processing to support additional C# constructs or relationships.
- Change the output format or extend the serialization functionality.

## Contributing
We welcome contributions! Feel free to submit issues and pull requests. Here are some ways you can help:
- Fix bugs or improve the parsing logic.
- Enhance the documentation.
- Add more features for graph representation (e.g., more edge types or custom visualizations).
