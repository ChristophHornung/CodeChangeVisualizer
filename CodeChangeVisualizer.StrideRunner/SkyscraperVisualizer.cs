namespace CodeChangeVisualizer.StrideRunner;

using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.CommunityToolkit.Engine;
using Stride.Rendering.Materials;
using Stride.Rendering;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.Lights;

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
	private const float TowerSpacing = 3.0f; // Increased spacing for better visibility

	private static readonly Dictionary<LineType, Color4> LineTypeColors = new()
	{
		// Mirror Viewer project's color choices for consistency
		[LineType.Empty] = new Color4(1f, 1f, 1f, 1f), // White
		[LineType.Comment] = new Color4(0f, 1f, 0f, 1f), // Green
		[LineType.ComplexityIncreasing] = new Color4(1f, 0f, 0f, 1f), // Red
		[LineType.Code] = new Color4(0.5f, 0.5f, 0.5f, 1f), // Gray
		[LineType.CodeAndComment] = new Color4(0.56f, 0.93f, 0.56f, 1f) // LightGreen
	};

	/// <summary>
	/// Builds skyscrapers in an existing scene using Community Toolkit primitives.
	/// </summary>
	public void BuildScene(Scene scene, List<FileAnalysis> analysis, Game game)
	{
		Console.WriteLine("Building skyscraper visualization...");

		this.BuildTowers(scene, analysis, game);

		Console.WriteLine("Skyscraper visualization complete!");
	}

	/// <summary>
	/// Builds all towers for the given analysis data.
	/// </summary>
	private void BuildTowers(Scene scene, List<FileAnalysis> analysis, Game game)
	{
		float towerX = 0f;
		foreach (FileAnalysis file in analysis)
		{
			Entity fileRoot = this.CreateTowerRoot(file, towerX);
			scene.Entities.Add(fileRoot);

			this.BuildTowerBlocks(file, fileRoot, game);

			towerX += SkyscraperVisualizer.TowerSpacing;
		}
	}

	/// <summary>
	/// Creates the root entity for a tower.
	/// </summary>
	private Entity CreateTowerRoot(FileAnalysis file, float towerX)
	{
		Entity fileRoot = new Entity(file.File);
		fileRoot.Transform.Position = new Vector3(towerX, 0f, 0f);
		return fileRoot;
	}

	    /// <summary>
    /// Builds all blocks for a single tower.
    /// </summary>
    private void BuildTowerBlocks(FileAnalysis file, Entity fileRoot, Game game)
    {
        float currentY = 0f;
        Console.WriteLine($"Building tower for file: {fileRoot.Name}");

        foreach (LineGroup group in file.Lines)
        {
            float height = group.Length * SkyscraperVisualizer.UnitsPerLine;
            Entity block = this.CreateBlock(file, group, currentY, game);
            fileRoot.AddChild(block);
            currentY += height; // Move up for the next block
        }

        Console.WriteLine($"Tower complete, total height: {currentY}");
    }

	    /// <summary>
    /// Creates a single block for a line group.
    /// </summary>
    private Entity CreateBlock(FileAnalysis file, LineGroup group, float currentY, Game game)
    {
	    float height = group.Length * SkyscraperVisualizer.UnitsPerLine;w
        Color4 color = SkyscraperVisualizer.LineTypeColors[group.Type];

        // Create a cube using Community Toolkit primitives with size options
        Primitive3DCreationOptions createOptions = new Primitive3DCreationOptions
        {
            Size = new Vector3(SkyscraperVisualizer.BlockWidth, height, SkyscraperVisualizer.BlockDepth)
        };
        
        Entity cube = game.Create3DPrimitive(PrimitiveModelType.Cube, createOptions);
        cube.Name = $"{file.File} [{group.Start}-{group.Start + group.Length - 1}] {group.Type}";
        cube.Transform.Position = new Vector3(0f, currentY + height / 2, 0f);

        // Apply material color directly so it renders with the intended color
        this.ApplyColorToCube(game, cube, color);

        // Attach color metadata component so renderers can use it
        cube.Add(new BlockDescriptorComponent { Size = new Vector3(SkyscraperVisualizer.BlockWidth, height, SkyscraperVisualizer.BlockDepth), Color = color });

        Console.WriteLine($"  Added block: {cube.Name} at Y={currentY + height / 2}, height={height}, color={color}");

        return cube;
    }

    /// <summary>
    /// Applies a solid color material to the given cube entity.
    /// </summary>
    private void ApplyColorToCube(Game game, Entity cube, Color4 color)
    {
        ModelComponent? modelComponent = cube.Get<ModelComponent>();
        if (modelComponent == null)
        {
            return; // Nothing to color
        }

        // Note on lighting and perceived black colors:
        // Stride uses a PBR (physically based) lighting model. A Diffuse/Albedo color does not emit light by itself;
        // it only reflects incoming light. If the scene lacks sufficient light (e.g., no/weak lights, grazing angles,
        // low exposure/tonemapping), purely diffuse objects can look very dark or even black. Adding an Emissive
        // feature makes the material self-lit so the intended color is visible regardless of lighting conditions.
        // For production, prefer fixing lighting (directional/ambient intensity and orientation) and exposure,
        // then reduce/remove Emissive once the scene is properly lit.
        MaterialDescriptor materialDesc = new()
        {
            Attributes = new MaterialAttributes
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
	            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                // Emissive = new MaterialEmissiveMapFeature(new ComputeColor(color))
            }
        };

        Material? material = Material.New(game.GraphicsDevice, materialDesc);

        if (modelComponent.Model != null)
        {
            if (modelComponent.Model.Materials.Count > 0)
            {
                modelComponent.Model.Materials[0] = material;
            }
            else
            {
                modelComponent.Model.Materials.Add(material);
            }
        }
    }
}