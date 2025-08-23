namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

/// <summary>
/// Builds a Stride Scene representing the analysis as a set of "skyscraper" towers.
/// One tower per file; each line group is a colored 3D block stacked vertically.
/// </summary>
public class SkyscraperVisualizer
{
	// World units (shared with LayoutCalculator)
	private const float BlockWidth = LayoutCalculator.Constants.BlockWidth;
	private const float BlockDepth = LayoutCalculator.Constants.BlockDepth;
	private const float UnitsPerLine = LayoutCalculator.Constants.UnitsPerLine; // 50 lines per unit (adjust as needed)

	private const float
		TowerSpacing = LayoutCalculator.Constants.TowerSpacing; // Increased spacing for better visibility

	private static readonly Dictionary<LineType, Color4> LineTypeColors = new()
	{
		// Mirror Viewer project's color choices for consistency
		[LineType.Empty] = new Color4(1f, 1f, 1f, 1f), // White
		[LineType.Comment] = new Color4(0f, 1f, 0f, 1f), // Green
		[LineType.ComplexityIncreasing] = new Color4(1f, 0f, 0f, 1f), // Red
		[LineType.Code] = new Color4(0.5f, 0.5f, 0.5f, 1f), // Gray
		[LineType.CodeAndComment] = new Color4(0.56f, 0.93f, 0.56f, 1f) // LightGreen
	};

	// Cache one material per unique RGBA color to avoid creating thousands of identical materials
	private static readonly Dictionary<int, Material> MaterialCache = new();

	// Reuse a single unit-cube model for all blocks to avoid creating thousands of meshes
	private static Model? s_UnitCubeModel;

	private static void EnsureUnitCubeModel(Game game)
	{
		if (SkyscraperVisualizer.s_UnitCubeModel != null)
		{
			return;
		}

		// Create a 1x1x1 cube once and reuse its Model
		var createOptions = new Primitive3DCreationOptions { Size = new Vector3(1f, 1f, 1f), IncludeCollider = false };
		Entity temp = game.Create3DPrimitive(PrimitiveModelType.Cube, createOptions);
		var modelComp = temp.Get<ModelComponent>();
		SkyscraperVisualizer.s_UnitCubeModel = modelComp?.Model;
		// We don't add temp to the scene; it will be GC'ed. We only keep the Model reference.
	}

	private static Material GetOrCreateSolidColorMaterial(Game game, Color4 color)
	{
		int key = color.ToRgba();
		if (SkyscraperVisualizer.MaterialCache.TryGetValue(key, out var cached))
		{
			return cached;
		}

		var materialDesc = new MaterialDescriptor
		{
			Attributes = new MaterialAttributes
			{
				Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
				DiffuseModel = new MaterialDiffuseLambertModelFeature(),
				// Emissive = new MaterialEmissiveMapFeature(new ComputeColor(color))
			}
		};

		var material = Material.New(game.GraphicsDevice, materialDesc);
		SkyscraperVisualizer.MaterialCache[key] = material;
		return material;
	}

	private static CameraComponent? FindCamera(Scene scene)
	{
		// Search all entities recursively for a CameraComponent
		foreach (var root in scene.Entities)
		{
			var cam = SkyscraperVisualizer.FindCameraRecursive(root);
			if (cam != null)
			{
				return cam;
			}
		}

		return null;
	}

	private static CameraComponent? FindCameraRecursive(Entity entity)
	{
		var cam = entity.Get<CameraComponent>();
		if (cam != null)
		{
			return cam;
		}

		foreach (var child in entity.Transform.Children)
		{
			if (child.Entity != null)
			{
				var found = SkyscraperVisualizer.FindCameraRecursive(child.Entity);
				if (found != null)
				{
					return found;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Builds skyscrapers in an existing scene using Community Toolkit primitives.
	/// </summary>
	public void BuildScene(Scene scene, List<FileAnalysis> analysis, Game game)
	{
		Console.WriteLine("Building skyscraper visualization...");

		this.BuildTowers(scene, analysis, game);

		// After building, frame the camera so all skyscrapers are visible
		this.FrameCameraToFit(scene, game, analysis);

		Console.WriteLine("Skyscraper visualization complete!");
	}

	/// <summary>
	/// Builds all towers for the given analysis data.
	/// </summary>
	private void BuildTowers(Scene scene, List<FileAnalysis> analysis, Game game)
	{
		// Arrange towers using a city planner abstraction
		int count = analysis.Count;
		if (count == 0)
		{
			return;
		}
		
		ICityPlanner planner = new GridCityPlanner();
		int index = 0;
		foreach (FileAnalysis file in analysis)
		{
			Vector3 pos = planner.GetPosition(index, analysis);
			Entity fileRoot = this.CreateTowerRoot(file, pos.X, pos.Z);
			scene.Entities.Add(fileRoot);
			
			this.BuildTowerBlocks(file, fileRoot, game);
			
			index++;
		}
	}

	/// <summary>
	/// Positions the default camera so that all towers are within view.
	/// </summary>
	private void FrameCameraToFit(Scene scene, Game game, List<FileAnalysis> analysis)
	{
		int count = analysis.Count;
		if (count == 0)
		{
			return;
		}
		
		ICityPlanner planner = new GridCityPlanner();
		var (rows, cols, gridSize) = planner.GetGrid(analysis);
		
		float width = (cols - 1) * SkyscraperVisualizer.TowerSpacing;
		float depth = (rows - 1) * SkyscraperVisualizer.TowerSpacing;
		float maxHeight = analysis.Select(f => (f.Lines?.Sum(g => g.Length) ?? 0) * SkyscraperVisualizer.UnitsPerLine)
			.DefaultIfEmpty(0f).Max();

		Console.WriteLine(
			$"[CameraFit] count={count} gridSize={gridSize} rows={rows} cols={cols} width={width:F2} depth={depth:F2} maxHeight={maxHeight:F2}");

		// Find a camera in the scene (search recursively)
		CameraComponent? camera = SkyscraperVisualizer.FindCamera(scene);
		if (camera == null)
		{
			Console.WriteLine("[CameraFit] No camera found in scene.");
			return;
		}

		Console.WriteLine($"[CameraFit] Using camera entity: {camera.Entity?.Name}");

		// Compute center of bounds
		Vector3 center = new Vector3(width / 2f, Math.Max(0.5f, maxHeight / 2f), depth / 2f);
		// Bounding sphere radius (half-diagonal of the box)
		float radius = 0.5f * (float)Math.Sqrt(width * width + depth * depth + maxHeight * maxHeight);
		float vfov = camera.VerticalFieldOfView;
		if (vfov <= 0f)
		{
			vfov = MathUtil.DegreesToRadians(60f);
		}

		Texture? backBuffer = game.GraphicsDevice?.Presenter?.BackBuffer;
		float aspect = (backBuffer != null && backBuffer.Height > 0)
			? (float)backBuffer.Width / backBuffer.Height
			: (16f / 9f);
		float hfov = 2f * (float)Math.Atan(Math.Tan(vfov / 2f) * aspect);
		float limitingFov = Math.Min(vfov, hfov);
		Console.WriteLine(
			$"[CameraFit] aspect={aspect:F3} vfovDeg={MathUtil.RadiansToDegrees(vfov):F1} hfovDeg={MathUtil.RadiansToDegrees(hfov):F1} limitingFovDeg={MathUtil.RadiansToDegrees(limitingFov):F1}");

		// Distance to fit the sphere inside frustum using limiting FOV; add a margin
		float distance = radius / (float)Math.Sin(Math.Max(0.1f, limitingFov / 2f));
		float margin = 1.25f;
		distance *= margin;
		Console.WriteLine($"[CameraFit] radius={radius:F2} distance={distance:F2}");

		// Choose a 45° elevated diagonal viewing direction (45° azimuth and 45° elevation)
		// We want the camera to be ABOVE the scene, looking DOWN toward the center.
		// Therefore, the direction from camera to center must have a NEGATIVE Y component.
		// Rotate azimuth by 180° (flip X/Z) to face the city while keeping 45° elevation downward.
		Vector3 dir = Vector3.Normalize(new Vector3(1f, -(float)Math.Sqrt(2), 1f));
		Vector3 position = center - dir * distance;
		Console.WriteLine(
			$"[CameraFit] dir=({dir.X:F3},{dir.Y:F3},{dir.Z:F3}) position=({position.X:F2},{position.Y:F2},{position.Z:F2}) center=({center.X:F2},{center.Y:F2},{center.Z:F2})");

		Entity camEntity = camera.Entity;
		camEntity.Transform.Position = position;

		// Orient camera to look at center
		Vector3 fwd = Vector3.Normalize(center - position);
		float yaw = (float)Math.Atan2(fwd.X, fwd.Z);
		float pitch = (float)Math.Asin(fwd.Y);
		// Apply a 180° yaw flip to ensure the city is in front, and force a downward pitch.
		float appliedYaw = yaw + MathUtil.Pi;
		float appliedPitch = -Math.Abs(pitch);
		camEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(appliedYaw, appliedPitch, 0f);
		Console.WriteLine(
			$"[CameraFit] yawDeg={MathUtil.RadiansToDegrees(appliedYaw):F1} pitchDeg={MathUtil.RadiansToDegrees(appliedPitch):F1} (applied, yaw+180, downward pitch)");
	}

	/// <summary>
	/// Creates the root entity for a tower.
	/// </summary>
	private Entity CreateTowerRoot(FileAnalysis file, float x, float z)
	{
		Entity fileRoot = new Entity(file.File);
		fileRoot.Transform.Position = new Vector3(x, 0f, z);
		return fileRoot;
	}

	/// <summary>
	/// Builds all blocks for a single tower.
	/// </summary>
	private void BuildTowerBlocks(FileAnalysis file, Entity fileRoot, Game game)
	{
		SkyscraperVisualizer.EnsureUnitCubeModel(game);
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
		float height = group.Length * SkyscraperVisualizer.UnitsPerLine;
		Color4 color = SkyscraperVisualizer.LineTypeColors[group.Type];

		// Create an entity that reuses a shared unit-cube model and scale it to desired size
		Entity cube = new Entity(file.File);
		if (SkyscraperVisualizer.s_UnitCubeModel != null)
		{
			cube.Add(new ModelComponent { Model = SkyscraperVisualizer.s_UnitCubeModel });
		}

		cube.Transform.Position = new Vector3(0f, currentY + height / 2, 0f);
		cube.Transform.Scale = new Vector3(SkyscraperVisualizer.BlockWidth, height, SkyscraperVisualizer.BlockDepth);

		// Apply material color directly so it renders with the intended color
		this.ApplyColorToCube(game, cube, color);

		// Attach color metadata component so renderers can use it (also used for picking)
		cube.Add(new BlockDescriptorComponent
		{
			Size = new Vector3(SkyscraperVisualizer.BlockWidth, height, SkyscraperVisualizer.BlockDepth), Color = color
		});

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

		// Use a cached material for this color to reduce allocations and state changes
		Material material = SkyscraperVisualizer.GetOrCreateSolidColorMaterial(game, color);

		// IMPORTANT: assign per-entity override, not the shared Model's materials
		var materials = modelComponent.Materials;
		if (materials.Count > 0)
		{
			materials[0] = material;
		}
		else
		{
			materials.Add(new KeyValuePair<int, Material>(0, material));
		}
	}
}