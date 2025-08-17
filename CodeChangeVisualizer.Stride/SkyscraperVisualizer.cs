using Color4 = Stride.Core.Mathematics.Color4;
using Entity = Stride.Engine.Entity;
using EntityTransformExtensions = Stride.Engine.EntityTransformExtensions;
using Scene = Stride.Engine.Scene;
using Vector3 = Stride.Core.Mathematics.Vector3;

namespace CodeChangeVisualizer.Stride;

// Local DTOs to avoid dependency on Analyzer project

/// <summary>
/// Builds a Stride Scene representing the analysis as a set of "skyscraper" towers.
/// One tower per file; each line group is a colored 3D block stacked vertically.
/// </summary>
public class SkyscraperVisualizer
{
	// World units
	private const float BlockWidth = 1.0f;
	private const float BlockDepth = 1.0f;
	private const float UnitsPerLine = 0.02f; // 50 lines per unit (adjust as needed)
	private const float TowerSpacing = 1.5f;

	private static readonly Dictionary<LineType, Color4> LineTypeColors = new()
	{
		// Mirror Viewer project's color choices for consistency
		[LineType.Empty] = new Color4(1f, 1f, 1f, 1f), // White
		[LineType.Comment] = new Color4(0f, 1f, 0f, 1f), // Green
		[LineType.ComplexityIncreasing] = new Color4(1f, 0f, 0f, 1f), // Red
		[LineType.Code] = new Color4(0.5f, 0.5f, 0.5f, 1f), // Gray
		[LineType.CodeAndComment] = new Color4(0.56f, 0.93f, 0.56f, 1f) // LightGreen
	};

	private static Entity CreateBlockEntity(string name, float width, float height, float depth, Color4 color)
	{
		Entity e = new(name);

		// Store the intended block size and color; a render system can create meshes/materials from this.
		e.Add(new BlockDescriptorComponent
		{
			Size = new Vector3(width, height, depth),
			Color = color
		});

		return e;
	}

	/// <summary>
	/// Builds a Stride Scene with 3D block entities representing the given analysis.
	/// Note: This constructs entities and transforms; actual model/mesh creation is deferred
	/// to runtime using the included BlockDescriptorComponent data (size and color), so you
	/// can attach your own system to generate meshes/materials from descriptors.
	/// </summary>
	public Scene BuildScene(List<FileAnalysis> analysis)
	{
		if (analysis == null)
		{
			throw new ArgumentNullException(nameof(analysis));
		}

		Scene scene = new Scene();

		// Lay towers along X axis
		float x = 0f;
		foreach (FileAnalysis file in analysis)
		{
			// Parent entity per file
			Entity fileRoot = new Entity(file.File);
			fileRoot.Transform.Position = new Vector3(x, 0f, 0f);
			scene.Entities.Add(fileRoot);

			float currentY = 0f; // base of the next block in world units
			foreach (LineGroup group in file.Lines)
			{
				float height = MathF.Max(group.Length * SkyscraperVisualizer.UnitsPerLine, 0.001f);
				Color4 color = SkyscraperVisualizer.LineTypeColors[group.Type];

				Entity block = SkyscraperVisualizer.CreateBlockEntity(
					name: $"{file.File} [{group.Start}-{group.Start + group.Length - 1}] {group.Type}",
					width: SkyscraperVisualizer.BlockWidth,
					height: height,
					depth: SkyscraperVisualizer.BlockDepth,
					color: color);

				// Position so the block sits on top of the previous one
				block.Transform.Position = new Vector3(0f, currentY + height / 2f, 0f);

				EntityTransformExtensions.AddChild(fileRoot, block);
				currentY += height;
			}

			x += SkyscraperVisualizer.BlockWidth + SkyscraperVisualizer.TowerSpacing;
		}

		return scene;
	}

	/// <summary>
	/// Returns the data needed to build skyscrapers.
	/// </summary>
	public List<(Entity fileRoot, List<(Entity block, Vector3 position, Color4 color)> blocks)> GetSkyscraperData(
		List<FileAnalysis> analysis)
	{
		if (analysis == null)
		{
			throw new ArgumentNullException(nameof(analysis));
		}

		List<(Entity fileRoot, List<(Entity block, Vector3 position, Color4 color)> blocks)> result =
			new List<(Entity fileRoot, List<(Entity block, Vector3 position, Color4 color)> blocks)>();

		// Lay towers along X axis
		float x = 0f;
		foreach (FileAnalysis file in analysis)
		{
			// Parent entity per file
			Entity fileRoot = new Entity(file.File);
			fileRoot.Transform.Position = new Vector3(x, 0f, 0f);

			List<(Entity block, Vector3 position, Color4 color)> blocks =
				new List<(Entity block, Vector3 position, Color4 color)>();
			float currentY = 0f; // base of the next block in world units

			foreach (LineGroup group in file.Lines)
			{
				float height = MathF.Max(group.Length * SkyscraperVisualizer.UnitsPerLine, 0.001f);
				Color4 color = SkyscraperVisualizer.LineTypeColors[group.Type];

				// Create a simple entity for the block
				Entity block = new Entity($"{file.File} [{group.Start}-{group.Start + group.Length - 1}] {group.Type}");
				Vector3 position = new Vector3(0f, currentY + height / 2f, 0f);

				blocks.Add((block, position, color));
				currentY += height;
			}

			result.Add((fileRoot, blocks));
			x += SkyscraperVisualizer.BlockWidth + SkyscraperVisualizer.TowerSpacing;
		}

		return result;
	}
}