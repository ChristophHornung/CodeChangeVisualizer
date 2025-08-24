namespace CodeChangeVisualizer.StrideRunner;

using System.Text.Json;
using CodeChangeVisualizer.Analyzer;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Skyboxes;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

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
		RevisionLog? revForPlayback = null;
		try
		{
			string json = File.ReadAllText(jsonPath);
			// Support RevisionLog (advanced mode), DirectoryAnalysis (new), and legacy List<FileAnalysis> formats.
			RevisionLog? revLog = null;
			DirectoryAnalysis? dirAnalysis = null;
			try
			{
				revLog = JsonSerializer.Deserialize<RevisionLog>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}
			catch
			{
				/* ignore; try other formats */
			}

			revForPlayback = revLog;
			if (revForPlayback != null && revForPlayback.Revisions != null && revForPlayback.Revisions.Count > 0)
			{
				// Reconstruct the latest analysis by applying diffs across revisions
				List<RevisionEntry> revs = revForPlayback.Revisions;
				if (revs[0].Analysis == null)
				{
					Console.WriteLine("Error: Revision log missing initial full analysis.");
					return;
				}

				Dictionary<string, FileAnalysis> files = revs[0].Analysis.ToDictionary(f => f.File, f => f);
				for (int i = 1; i < revs.Count; i++)
				{
					RevisionEntry entry = revs[i];
					if (entry.Diff == null)
					{
						continue;
					}

					foreach (FileChangeEntry change in entry.Diff)
					{
						string file = change.File;
						FileAnalysisDiff fad = change.Change;
						switch (fad.Kind)
						{
							case FileAnalysisChangeKind.FileAdd:
							{
								List<LineGroup> lines = (fad.NewFileLines ?? new List<LineGroup>())
									.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start })
									.ToList();
								// Recompute Start
								int s = 0;
								foreach (var lg in lines)
								{
									lg.Start = s;
									s += lg.Length;
								}

								files[file] = new FileAnalysis { File = file, Lines = lines };
								break;
							}
							case FileAnalysisChangeKind.FileDelete:
							{
								files.Remove(file);
								break;
							}
							case FileAnalysisChangeKind.Modify:
							default:
							{
								if (!files.TryGetValue(file, out FileAnalysis? oldFa))
								{
									oldFa = new FileAnalysis { File = file, Lines = new List<LineGroup>() };
								}

								FileAnalysis patched = FileAnalysisApplier.Apply(oldFa, fad, file);
								files[file] = patched;
								break;
							}
						}
					} // end foreach file change
				} // end for each revision

				analysis = revs[0].Analysis.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
			}
			else
			{
				try
				{
					dirAnalysis = JsonSerializer.Deserialize<DirectoryAnalysis>(json, new JsonSerializerOptions
					{
						PropertyNameCaseInsensitive = true
					});
				}
				catch
				{
					/* ignore */
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

			// Ensure a grass-green ground at Y=0
			Program.EnsureGrassGround(rootScene, game);

			// Create the skyscraper visualizer
			SkyscraperVisualizer visualizer = new SkyscraperVisualizer();

			// Build the visualization using the provided JSON analysis data
			visualizer.BuildScene(rootScene, analysis, game);

			Console.WriteLine("Skyscraper visualization setup complete!");

			// Add hover tooltip script
			Entity hoverEntity = new Entity("HoverTooltip");
			hoverEntity.Add(new HoverTooltipScript());
			rootScene.Entities.Add(hoverEntity);

			// If we also have a revision log, attach diff playback with Space key
			if (revForPlayback != null && revForPlayback.Revisions != null && revForPlayback.Revisions.Count > 0)
			{
				var diffs = revForPlayback.Revisions.Skip(1)
					.Where(r => r.Diff != null && r.Diff.Count > 0)
					.Select(r => r.Diff!)
					.ToList();
				if (diffs.Count > 0)
				{
					Entity diffEntity = new Entity("DiffPlayback");
					DiffPlaybackScript script = new DiffPlaybackScript
					{
						InitialAnalysis = revForPlayback.Revisions[0].Analysis ?? analysis,
						Diffs = diffs
					};
					diffEntity.Add(script);
					rootScene.Entities.Add(diffEntity);
					Console.WriteLine(
						"Press SPACE to apply next diff (2s per step). Press 'L' to autoplay remaining steps.");
				}
			}
		});
	}

	private static void EnsureGrassGround(Scene rootScene, Game game)
	{
		// We want an effectively infinite ground plane. We'll use a very large size and/or scale.
		const float GroundSize = 100000f; // 100 km square, effectively infinite for our layouts
		const float GroundThickness = 0.1f;
		const float GroundScaleIfExists = 100f; // upscale existing ground to ensure huge effective size
		
		// Try to find an existing ground entity by name
		Entity? ground = rootScene.Entities
			.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name)
			                    && e.Name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0
			                    && e.Get<ModelComponent>() != null);
		
		if (ground == null)
		{
			// Create a very large, thin cube as ground so its top sits at Y=0
			var options = new Primitive3DCreationOptions { Size = new Vector3(GroundSize, GroundThickness, GroundSize), IncludeCollider = false };
			Entity plane = game.Create3DPrimitive(PrimitiveModelType.Cube, options);
			plane.Name = "Ground";
			plane.Transform.Position = new Vector3(0f, -GroundThickness / 2f, 0f);
			ground = plane;
			rootScene.Entities.Add(ground);
		}
		else
		{
			// Ensure existing ground is scaled up massively to appear infinite
			Vector3 s = ground.Transform.Scale;
			if (s.X < GroundScaleIfExists || s.Z < GroundScaleIfExists)
			{
				ground.Transform.Scale = new Vector3(GroundScaleIfExists, 1f, GroundScaleIfExists);
			}
		}
		
		// Apply a grass-green material to the ground
		ModelComponent? mc = ground.Get<ModelComponent>();
		if (mc != null && game.GraphicsDevice != null)
		{
			var green = new Color4(0.2f, 0.6f, 0.2f, 1f);
			MaterialDescriptor desc = new MaterialDescriptor
			{
				Attributes = new MaterialAttributes
				{
					Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(green)),
					DiffuseModel = new MaterialDiffuseLambertModelFeature(),
				}
			};
			Material mat = Material.New(game.GraphicsDevice, desc);
			var materials = mc.Materials;
			if (materials.Count > 0)
			{
				materials[0] = mat;
			}
			else
			{
				materials.Add(new KeyValuePair<int, Material>(0, mat));
			}
		}
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