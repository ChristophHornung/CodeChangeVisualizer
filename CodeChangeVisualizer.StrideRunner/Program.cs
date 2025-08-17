namespace CodeChangeVisualizer.StrideRunner;

using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Skyboxes;
using Stride.Engine;

internal class Program
{
	private static void Main()
	{
		using var game = new Game();

		game.Run(start: (Scene rootScene) =>
		{
			Console.WriteLine("Setting up 3D skyscraper visualization...");

			// Set up the base 3D scene with Community Toolkit
			game.SetupBase3DScene();
			game.AddSkybox();

			// Create the skyscraper visualizer
			var visualizer = new SkyscraperVisualizer();

			// Create sample data for testing
			List<FileAnalysis> sampleData = Program.CreateSampleData();

			// Build the visualization
			visualizer.BuildScene(rootScene, sampleData, game);

			Console.WriteLine("Skyscraper visualization setup complete!");
		});
	}

	private static List<FileAnalysis> CreateSampleData()
	{
		return new List<FileAnalysis>
		{
			new FileAnalysis
			{
				File = "Sample1.cs",
				Lines = new List<LineGroup>
				{
					new LineGroup { Type = LineType.Code, Start = 1, Length = 10 },
					new LineGroup { Type = LineType.Comment, Start = 15, Length = 11 },
					new LineGroup { Type = LineType.ComplexityIncreasing, Start = 30, Length = 6 }
				}
			},
			new FileAnalysis
			{
				File = "Sample2.cs",
				Lines = new List<LineGroup>
				{
					new LineGroup { Type = LineType.Code, Start = 1, Length = 20 },
					new LineGroup { Type = LineType.Comment, Start = 25, Length = 16 }
				}
			}
		};
	}
}