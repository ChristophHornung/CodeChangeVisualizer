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

		Configuration config = new();
		string? configFile = null;

		// Parse command line arguments
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i].ToLower())
			{
				case "--config":
				case "-f":
					if (i + 1 < args.Length)
					{
						configFile = args[++i];
					}

					break;
				case "--directory":
				case "-d":
					if (i + 1 < args.Length)
					{
						config.Directory = args[++i];
					}

					break;
				case "--json":
				case "-j":
					if (i + 1 < args.Length)
					{
						config.JsonOutput = args[++i];
					}

					break;
				case "--visualization":
				case "-v":
					if (i + 1 < args.Length)
					{
						config.VisualizationOutput = args[++i];
					}

					break;
				case "--console":
				case "-c":
					config.OutputToConsole = true;
					break;
				case "--ignore":
				case "-i":
					if (i + 1 < args.Length)
					{
						config.IgnorePatterns.Add(args[++i]);
					}

					break;
				case "--help":
				case "-h":
					Program.PrintUsage();
					return;
				case "--git-start":
					if (i + 1 < args.Length)
					{
						config.GitStart = args[++i];
					}
					break;
				default:
					// If no flag is provided, treat as directory path (backward compatibility)
					if (config.Directory == null)
					{
						config.Directory = args[i];
						// For backward compatibility, if only directory is provided, output to console
						if (args.Length == 1)
						{
							config.OutputToConsole = true;
						}
					}

					break;
			}
		}

		// Load config file if specified
		if (configFile != null)
		{
			config = await Program.LoadConfigurationFromFile(configFile, config);
		}

		if (config.Directory == null)
		{
			Console.WriteLine("Error: Directory path is required.");
			Program.PrintUsage();
			return;
		}

		if (!Directory.Exists(config.Directory))
		{
			Console.WriteLine($"Error: Directory '{config.Directory}' does not exist.");
			return;
		}

		if (config.JsonOutput == null && config.VisualizationOutput == null && !config.OutputToConsole)
		{
			Console.WriteLine("Error: At least one output option is required (--json, --visualization, or --console).");
			Program.PrintUsage();
			return;
		}

		try
		{
			if (!string.IsNullOrWhiteSpace(config.GitStart))
			{
				// Advanced git mode
				var log = await RunAdvancedGitAnalysisAsync(config);
				var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
				{
					WriteIndented = true,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				});
				// Suppress console output in advanced git mode to avoid extremely large logs.
				if (!string.IsNullOrEmpty(config.JsonOutput))
				{
					await File.WriteAllTextAsync(config.JsonOutput!, json);
					Console.WriteLine($"Advanced analysis JSON saved to: {config.JsonOutput}");
				}
				if (!string.IsNullOrEmpty(config.VisualizationOutput))
				{
					Console.WriteLine("Visualization is not supported in advanced git mode; ignoring --visualization.");
				}
			}
			else
			{
				// Regular single analysis mode
				CodeAnalyzer analyzer = new CodeAnalyzer();
    DirectoryAnalysis dirAnalysis = await analyzer.AnalyzeDirectoryAsync(
					config.Directory,
					config.IgnorePatterns.Count > 0 ? config.IgnorePatterns : null,
					config.FileExtensions.Count > 0 ? config.FileExtensions : null);

				// Output JSON to console if requested
    if (config.OutputToConsole)
				{
					string jsonOutput = JsonSerializer.Serialize(dirAnalysis, new JsonSerializerOptions
					{
						WriteIndented = true,
						PropertyNamingPolicy = JsonNamingPolicy.CamelCase
					});
					Console.WriteLine(jsonOutput);
				}

				// Output JSON to file if requested
    if (config.JsonOutput != null)
				{
					string jsonOutput = JsonSerializer.Serialize(dirAnalysis, new JsonSerializerOptions
					{
						WriteIndented = true,
						PropertyNamingPolicy = JsonNamingPolicy.CamelCase
					});
					await File.WriteAllTextAsync(config.JsonOutput, jsonOutput);
					Console.WriteLine($"JSON analysis saved to: {config.JsonOutput}");
				}

				// Generate visualization if requested
				if (config.VisualizationOutput != null)
				{
     CodeVisualizer visualizer = new CodeVisualizer();
				visualizer.GenerateVisualization(dirAnalysis.Files, config.VisualizationOutput);
				Console.WriteLine($"Visualization saved to: {config.VisualizationOutput}");
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
		}
	}

	// Advanced git analysis data models
	public class RevisionLog
	{
		public List<RevisionEntry> Revisions { get; set; } = new();
	}
	public class RevisionEntry
	{
		public string Commit { get; set; } = string.Empty;
		public List<FileAnalysis>? Analysis { get; set; }
		public List<FileChangeEntry>? Diff { get; set; }
	}
	public class FileChangeEntry
	{
		public string File { get; set; } = string.Empty;
		public FileAnalysisDiff Change { get; set; } = new();
	}

	private static async Task<RevisionLog> RunAdvancedGitAnalysisAsync(Configuration config)
	{
		string workDir = Path.GetFullPath(config.Directory!);
		// Ensure clean working directory
		string status = await RunGitAsync("status --porcelain", workDir);
		if (!string.IsNullOrWhiteSpace(status))
		{
			throw new InvalidOperationException("Git working tree has local changes. Please commit or stash before running advanced analysis.");
		}

		string originalRef = (await RunGitAsync("rev-parse --abbrev-ref HEAD", workDir)).Trim();
		string originalSha = (await RunGitAsync("rev-parse HEAD", workDir)).Trim();
		bool restoreBySha = string.Equals(originalRef, "HEAD", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(originalRef);

		string startSha = (await RunGitAsync($"rev-parse {config.GitStart}", workDir)).Trim();
		if (string.IsNullOrWhiteSpace(startSha))
		{
			throw new InvalidOperationException($"Cannot resolve start git hash '{config.GitStart}'.");
		}

		// Commits after start (excluding start)
		string list = await RunGitAsync($"rev-list --reverse {startSha}..HEAD", workDir);
		List<string> commits = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
		// Include start as first
		commits.Insert(0, startSha);

		var log = new RevisionLog();

		List<FileAnalysis>? prev = null;
		try
		{
			for (int idx = 0; idx < commits.Count; idx++)
			{
				string sha = commits[idx];
				await RunGitAsync($"checkout --quiet {sha}", workDir);

				CodeAnalyzer analyzer = new CodeAnalyzer();
    DirectoryAnalysis currentAll = await analyzer.AnalyzeDirectoryAsync(
					workDir,
					config.IgnorePatterns.Count > 0 ? config.IgnorePatterns : null,
					config.FileExtensions.Count > 0 ? config.FileExtensions : null);
				List<FileAnalysis> current = currentAll.Files;

				if (prev == null)
				{
					log.Revisions.Add(new RevisionEntry { Commit = sha, Analysis = current });
				}
				else
				{
					var diff = ComputeDiff(prev, current);
					log.Revisions.Add(new RevisionEntry { Commit = sha, Diff = diff });
				}
				prev = current;
			}
		}
		finally
		{
			// Restore original ref
			string target = restoreBySha ? originalSha : originalRef;
			await RunGitAsync($"checkout --quiet {target}", workDir);
		}

		return log;
	}

	private static List<FileChangeEntry> ComputeDiff(List<FileAnalysis> oldAnalyses, List<FileAnalysis> newAnalyses)
	{
		var result = new List<FileChangeEntry>();
		var oldMap = oldAnalyses.ToDictionary(f => f.File, f => f);
		var newMap = newAnalyses.ToDictionary(f => f.File, f => f);

		// Removed files
		foreach (var kv in oldMap)
		{
			if (!newMap.ContainsKey(kv.Key))
			{
				var fd = FileDiffer.DiffFile(kv.Value, new FileAnalysis { File = kv.Key, Lines = new List<LineGroup>() });
				result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
			}
		}
		// Added and modified files
		foreach (var kv in newMap)
		{
			if (!oldMap.TryGetValue(kv.Key, out var oldFa))
			{
				var fd = FileDiffer.DiffFile(new FileAnalysis { File = kv.Key, Lines = new List<LineGroup>() }, kv.Value);
				result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
			}
			else
			{
				var fd = FileDiffer.DiffFile(oldFa, kv.Value);
				// Skip no-op modify (no edits)
				if (fd.Kind != FileChangeKind.Modify || (fd.Edits != null && fd.Edits.Count > 0))
				{
					result.Add(new FileChangeEntry { File = kv.Key, Change = FileAnalysisDiff.FromFileDiff(fd) });
				}
			}
		}

		// Order by file for deterministic output
		return result.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static async Task<string> RunGitAsync(string arguments, string workingDirectory)
	{
		var psi = new System.Diagnostics.ProcessStartInfo
		{
			FileName = "git",
			Arguments = arguments,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		using var proc = System.Diagnostics.Process.Start(psi)!;
		string stdout = await proc.StandardOutput.ReadToEndAsync();
		string stderr = await proc.StandardError.ReadToEndAsync();
		await proc.WaitForExitAsync();
		if (proc.ExitCode != 0)
		{
			throw new InvalidOperationException($"git {arguments} failed: {stderr}");
		}
		return stdout;
	}

	private static async Task<Configuration> LoadConfigurationFromFile(string configFile, Configuration baseConfig)
	{
		if (!File.Exists(configFile))
		{
			throw new FileNotFoundException($"Configuration file '{configFile}' not found.");
		}

		string jsonContent = await File.ReadAllTextAsync(configFile);
		Configuration? fileConfig = JsonSerializer.Deserialize<Configuration>(jsonContent, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

		if (fileConfig == null)
		{
			throw new InvalidOperationException($"Failed to parse configuration file '{configFile}'.");
		}

		// Merge configurations (command line takes precedence)
		return Program.MergeConfigurations(fileConfig, baseConfig);
	}

	private static Configuration MergeConfigurations(Configuration fileConfig, Configuration commandLineConfig)
	{
		Configuration merged = new()
		{
			Directory = commandLineConfig.Directory ?? fileConfig.Directory,
			JsonOutput = commandLineConfig.JsonOutput ?? fileConfig.JsonOutput,
			VisualizationOutput = commandLineConfig.VisualizationOutput ?? fileConfig.VisualizationOutput,
			OutputToConsole = commandLineConfig.OutputToConsole || fileConfig.OutputToConsole,
			IgnorePatterns = new List<string>(fileConfig.IgnorePatterns),
			FileExtensions = new List<string>(fileConfig.FileExtensions),
			GitStart = commandLineConfig.GitStart ?? fileConfig.GitStart
		};

		// Add command line ignore patterns
		merged.IgnorePatterns.AddRange(commandLineConfig.IgnorePatterns);

		return merged;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("CodeChangeVisualizer.Runner - Code Analysis and Visualization Tool");
		Console.WriteLine();
		Console.WriteLine("Usage:");
		Console.WriteLine("  CodeChangeVisualizer.Runner --directory <path> [options]");
		Console.WriteLine("  CodeChangeVisualizer.Runner --config <file> [options]");
		Console.WriteLine();
		Console.WriteLine("Options:");
		Console.WriteLine("  -f, --config <file>        Load configuration from JSON file");
		Console.WriteLine("  -d, --directory <path>     Directory to analyze (required)");
		Console.WriteLine("  -j, --json <file>          Output JSON analysis to file");
		Console.WriteLine("  -v, --visualization <file> Output PNG visualization to file");
		Console.WriteLine("  -c, --console              Output JSON analysis to console");
		Console.WriteLine("  -i, --ignore <pattern>     Regex pattern to ignore files (can be used multiple times)");
		Console.WriteLine("      --git-start <hash>     Advanced mode: analyze all revisions from <hash> to current HEAD");
		Console.WriteLine("  -h, --help                 Show this help message");
		Console.WriteLine();
		Console.WriteLine("Configuration File Format:");
		Console.WriteLine("  {");
		Console.WriteLine("    \"directory\": \"./src\",");
		Console.WriteLine("    \"jsonOutput\": \"analysis.json\",");
		Console.WriteLine("    \"visualizationOutput\": \"output.png\",");
		Console.WriteLine("    \"outputToConsole\": false,");
		Console.WriteLine("    \"ignorePatterns\": [");
		Console.WriteLine("      \"\\\\.git/.*\",");
		Console.WriteLine("      \".*\\\\.generated\\\\.cs$\"");
		Console.WriteLine("    ],");
		Console.WriteLine("    \"fileExtensions\": [\"*.cs\", \"*.vb\"],");
		Console.WriteLine("    \"gitStart\": null");
		Console.WriteLine("  }");
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
		Console.WriteLine("  # Advanced git mode: analyze from a start commit to HEAD and emit first analysis then diffs");
		Console.WriteLine(
			"  CodeChangeVisualizer.Runner --directory ./repo/subdir --git-start <commit> --json advanced.json");
		Console.WriteLine();
		Console.WriteLine("  # Ignore files using regex patterns");
		Console.WriteLine(
			"  CodeChangeVisualizer.Runner --directory ./src --ignore \"\\\\.git/.*\" --ignore \".*\\\\.generated\\\\.cs$\"");
		Console.WriteLine();
		Console.WriteLine("  # Use configuration file");
		Console.WriteLine("  CodeChangeVisualizer.Runner --config config.json");
		Console.WriteLine();
		Console.WriteLine("  # Backward compatibility (outputs JSON to console)");
		Console.WriteLine("  CodeChangeVisualizer.Runner ./src");
	}
}