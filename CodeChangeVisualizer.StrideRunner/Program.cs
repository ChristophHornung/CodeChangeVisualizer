namespace CodeChangeVisualizer.StrideRunner;

using System.Text.Json;
using CodeChangeVisualizer.Analyzer;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Skyboxes;
using Stride.Engine;

internal class Program
{
	private static void Main()
	{
		// Expect a JSON file path (produced by the analyzer) as the first CLI argument.
		// If none is given, attempt to use a default analysis.json located under the solution (.slnx) directory's bin folder.
		string[] args = Environment.GetCommandLineArgs();
		string? jsonPath;
		if (args.Length > 1)
		{
			jsonPath = args[1];
		}
		else
		{
			jsonPath = Program.GetDefaultAnalysisJsonPath();
			if (jsonPath != null)
			{
				Console.WriteLine($"No parameter provided. Using default analysis JSON: {jsonPath}");
			}
		}

		if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
		{
			Console.WriteLine("Usage: CodeChangeVisualizer.StrideRunner <analysis.json>");
			Console.WriteLine(
				"No valid analysis JSON found. Looked for a default at <solution>\\bin\\analysis.json (and common debug paths).");
			return;
		}

		Console.WriteLine($"Loading analysis JSON: {jsonPath}");
		List<FileAnalysis>? analysis;
		try
		{
			string json = File.ReadAllText(jsonPath);
			// Support both new DirectoryAnalysis object and legacy List<FileAnalysis> formats.
			DirectoryAnalysis? dirAnalysis = null;
			try
			{
				dirAnalysis = JsonSerializer.Deserialize<DirectoryAnalysis>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}
			catch
			{
				// ignore and try legacy format
			}

			if (dirAnalysis != null && dirAnalysis.Files != null && dirAnalysis.Files.Count > 0)
			{
				analysis = dirAnalysis.Files;
			}
			else
			{
				analysis = JsonSerializer.Deserialize<List<FileAnalysis>>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}

			if (analysis == null)
			{
				Console.WriteLine("Error: Failed to parse analysis JSON.");
				return;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to load analysis JSON: {ex.Message}");
			return;
		}

		using Game game = new Game();

		game.Run(start: (Scene rootScene) =>
		{
			Console.WriteLine("Setting up 3D skyscraper visualization...");

			// Set up the base 3D scene with Community Toolkit
			game.SetupBase3DScene();
			game.AddSkybox();

			// Create the skyscraper visualizer
			SkyscraperVisualizer visualizer = new SkyscraperVisualizer();

			// Build the visualization using the provided JSON analysis data
			visualizer.BuildScene(rootScene, analysis, game);

			Console.WriteLine("Skyscraper visualization setup complete!");

			// Add hover tooltip script
			Entity hoverEntity = new Entity("HoverTooltip");
			hoverEntity.Add(new HoverTooltipScript());
			rootScene.Entities.Add(hoverEntity);
		});
	}

	private static string? GetDefaultAnalysisJsonPath()
	{
		string? solutionRoot = Program.FindSolutionRoot();
		if (string.IsNullOrEmpty(solutionRoot))
		{
			return null;
		}

		string[] candidates = new[]
		{
			Path.Combine(solutionRoot, "bin", "analysis.json"),
			Path.Combine(solutionRoot, "bin", "Debug", "net9.0", "analysis.json"),
			Path.Combine(solutionRoot, "bin", "Debug", "analysis.json")
		};

		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static string? FindSolutionRoot()
	{
		try
		{
			string? dir = AppContext.BaseDirectory;
			DirectoryInfo? current = new DirectoryInfo(dir);
			while (current != null)
			{
				string slnx = Path.Combine(current.FullName, "CodeChangeVisualizer.slnx");
				if (File.Exists(slnx))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}
		catch
		{
			// ignore
		}

		return null;
	}
}