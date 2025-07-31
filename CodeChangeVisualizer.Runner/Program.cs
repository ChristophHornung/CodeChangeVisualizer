namespace CodeChangeVisualizer.Runner;

using System.Text.Json;
using CodeChangeVisualizer.Analyzer;
using CodeChangeVisualizer.Viewer;

public class Program
{
	public static async Task Main(string[] args)
	{
		if (args.Length == 0)
		{
			Program.PrintUsage();
			return;
		}

		string? directoryPath = null;
		string? jsonOutputPath = null;
		string? visualizationOutputPath = null;
		bool outputToConsole = false;

		// Parse command line arguments
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i].ToLower())
			{
				case "--directory":
				case "-d":
					if (i + 1 < args.Length)
					{
						directoryPath = args[++i];
					}

					break;
				case "--json":
				case "-j":
					if (i + 1 < args.Length)
					{
						jsonOutputPath = args[++i];
					}

					break;
				case "--visualization":
				case "-v":
					if (i + 1 < args.Length)
					{
						visualizationOutputPath = args[++i];
					}

					break;
				case "--console":
				case "-c":
					outputToConsole = true;
					break;
				case "--help":
				case "-h":
					Program.PrintUsage();
					return;
				default:
					// If no flag is provided, treat as directory path (backward compatibility)
					if (directoryPath == null)
					{
						directoryPath = args[i];
						// For backward compatibility, if only directory is provided, output to console
						if (args.Length == 1)
						{
							outputToConsole = true;
						}
					}

					break;
			}
		}

		if (directoryPath == null)
		{
			Console.WriteLine("Error: Directory path is required.");
			Program.PrintUsage();
			return;
		}

		if (!Directory.Exists(directoryPath))
		{
			Console.WriteLine($"Error: Directory '{directoryPath}' does not exist.");
			return;
		}

		if (jsonOutputPath == null && visualizationOutputPath == null && !outputToConsole)
		{
			Console.WriteLine("Error: At least one output option is required (--json, --visualization, or --console).");
			Program.PrintUsage();
			return;
		}

		try
		{
			CodeAnalyzer analyzer = new CodeAnalyzer();
			List<FileAnalysis> results = await analyzer.AnalyzeDirectoryAsync(directoryPath);

			// Output JSON to console if requested
			if (outputToConsole)
			{
				string jsonOutput = JsonSerializer.Serialize(results, new JsonSerializerOptions
				{
					WriteIndented = true,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});
				Console.WriteLine(jsonOutput);
			}

			// Output JSON to file if requested
			if (jsonOutputPath != null)
			{
				string jsonOutput = JsonSerializer.Serialize(results, new JsonSerializerOptions
				{
					WriteIndented = true,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});
				await File.WriteAllTextAsync(jsonOutputPath, jsonOutput);
				Console.WriteLine($"JSON analysis saved to: {jsonOutputPath}");
			}

			// Generate visualization if requested
			if (visualizationOutputPath != null)
			{
				CodeVisualizer visualizer = new CodeVisualizer();
				visualizer.GenerateVisualization(results, visualizationOutputPath);
				Console.WriteLine($"Visualization saved to: {visualizationOutputPath}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}

	private static void PrintUsage()
	{
		Console.WriteLine("CodeChangeVisualizer.Runner - Code Analysis and Visualization Tool");
		Console.WriteLine();
		Console.WriteLine("Usage:");
		Console.WriteLine("  CodeChangeVisualizer.Runner --directory <path> [options]");
		Console.WriteLine();
		Console.WriteLine("Options:");
		Console.WriteLine("  -d, --directory <path>     Directory to analyze (required)");
		Console.WriteLine("  -j, --json <file>          Output JSON analysis to file");
		Console.WriteLine("  -v, --visualization <file> Output PNG visualization to file");
		Console.WriteLine("  -c, --console              Output JSON analysis to console");
		Console.WriteLine("  -h, --help                 Show this help message");
		Console.WriteLine();
		Console.WriteLine("Examples:");
		Console.WriteLine("  # Generate JSON only to console");
		Console.WriteLine("  CodeChangeVisualizer.Runner --directory ./src --console");
		Console.WriteLine();
		Console.WriteLine("  # Generate JSON only to file");
		Console.WriteLine("  CodeChangeVisualizer.Runner --directory ./src --json analysis.json");
		Console.WriteLine();
		Console.WriteLine("  # Generate visualization only");
		Console.WriteLine("  CodeChangeVisualizer.Runner --directory ./src --visualization output.png");
		Console.WriteLine();
		Console.WriteLine("  # Generate both JSON and visualization");
		Console.WriteLine(
			"  CodeChangeVisualizer.Runner --directory ./src --json analysis.json --visualization output.png");
		Console.WriteLine();
		Console.WriteLine("  # Backward compatibility (outputs JSON to console)");
		Console.WriteLine("  CodeChangeVisualizer.Runner ./src");
	}
}