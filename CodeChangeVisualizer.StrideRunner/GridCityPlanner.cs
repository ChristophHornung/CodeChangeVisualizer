namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

/// <summary>
/// Default city planner: places towers on a square grid with equal spacing.
/// Now stable across SetFiles updates: existing towers never move; new towers are appended.
/// </summary>
public sealed class GridCityPlanner : ICityPlanner
{
	private const float TowerSpacing = LayoutCalculator.Constants.TowerSpacing;
	private IReadOnlyList<FileAnalysis>? files;

	// Stability state
	private readonly Dictionary<string, int> assignedIndexByFile = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Vector3> positionByFile = new(StringComparer.OrdinalIgnoreCase);
	private List<Vector3> lastPositions = new(); // positions aligned with the latest SetFiles(files) order
	private int fixedCols = 0; // fixed number of columns chosen at the first non-empty SetFiles

	public void SetFiles(IReadOnlyList<FileAnalysis> files)
	{
		this.files = files;
		// Initialize fixed columns on the first non-empty call
		if (this.fixedCols <= 0)
		{
			int count = files?.Count ?? 0;
			if (count > 0)
			{
				this.fixedCols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
			}
		}

		// Rebuild alignment for the provided files order while preserving positions for known files
		this.lastPositions = new List<Vector3>(files?.Count ?? 0);
		if (files == null)
		{
			return;
		}

		for (int i = 0; i < files.Count; i++)
		{
			string file = files[i].File;
			if (!this.positionByFile.TryGetValue(file, out Vector3 pos))
			{
				// Assign a new slot index at the end (append) and compute its position using a square-layer growth pattern.
				// This expands the grid in both X (cols) and Z (rows) without moving previously assigned towers.
				int slot = this.assignedIndexByFile.Count;
				this.assignedIndexByFile[file] = slot;
				int k = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(slot + 1))); // current square size
				int prevSquare = (k - 1) * (k - 1);
				int indexWithinLayer = slot - prevSquare;
				int row, col;
				if (indexWithinLayer < k)
				{
					// Fill the new rightmost column from top to bottom
					row = indexWithinLayer;
					col = k - 1;
				}
				else
				{
					// Then fill the new bottom row from left to right (excluding the overlapping corner)
					row = k - 1;
					col = indexWithinLayer - k;
				}
				pos = new Vector3(col * GridCityPlanner.TowerSpacing, 0f, row * GridCityPlanner.TowerSpacing);
				this.positionByFile[file] = pos;
			}

			this.lastPositions.Add(pos);
		}
	}

	public Vector3 GetPosition(int index)
	{
		if (index < 0 || index >= this.lastPositions.Count)
		{
			// Out of bounds: return origin to avoid exceptions
			return Vector3.Zero;
		}

		return this.lastPositions[index];
	}

	public (int rows, int cols, int gridSize) GetGrid()
	{
		int totalCount = this.files?.Count ?? 0;
		if (totalCount <= 0)
		{
			return (0, 0, 0);
		}
		// Report a square bound that encloses all assigned positions without moving existing towers.
		int k = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(totalCount)));
		int rows = k;
		int cols = k;
		int gridSize = k;
		return (rows, cols, gridSize);
	}

	public (Vector3 position, Quaternion rotation) GetFullViewCameraPosition(Game game, CameraComponent camera)
	{
		int count = this.files?.Count ?? 0;
		if (count <= 0)
		{
			// Default camera at origin looking down -Z with no rotation
			return (new Vector3(0f, 0f, 0f), Quaternion.Identity);
		}

		var (rows, cols, gridSize) = this.GetGrid();
		float width = (cols - 1) * GridCityPlanner.TowerSpacing;
		float depth = (rows - 1) * GridCityPlanner.TowerSpacing;
		float maxHeight = (this.files ?? Array.Empty<FileAnalysis>())
			.Select(f => (f.Lines?.Sum(g => g.Length) ?? 0) * LayoutCalculator.Constants.UnitsPerLine)
			.DefaultIfEmpty(0f).Max();

		Console.WriteLine($"[CameraFit] count={count} cols={cols} rows={rows} width={width:F2} depth={depth:F2} maxHeight={maxHeight:F2}");

		// Compute center and bounding sphere radius
		Vector3 center = new Vector3(width / 2f, Math.Max(0.5f, maxHeight / 2f), depth / 2f);
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
		Console.WriteLine($"[CameraFit] aspect={aspect:F3} vfovDeg={MathUtil.RadiansToDegrees(vfov):F1} hfovDeg={MathUtil.RadiansToDegrees(hfov):F1} limitingFovDeg={MathUtil.RadiansToDegrees(limitingFov):F1}");

		float distance = radius / (float)Math.Sin(Math.Max(0.1f, limitingFov / 2f));
		float margin = 1.25f;
		distance *= margin;
		Console.WriteLine($"[CameraFit] radius={radius:F2} distance={distance:F2}");

		Vector3 dir = Vector3.Normalize(new Vector3(1f, -(float)Math.Sqrt(2), 1f));
		Vector3 position = center - dir * distance;
		Console.WriteLine($"[CameraFit] dir=({dir.X:F3},{dir.Y:F3},{dir.Z:F3}) position=({position.X:F2},{position.Y:F2},{position.Z:F2}) center=({center.X:F2},{center.Y:F2},{center.Z:F2})");

		Vector3 fwd = Vector3.Normalize(center - position);
		float yaw = (float)Math.Atan2(fwd.X, fwd.Z);
		float pitch = (float)Math.Asin(fwd.Y);
		float appliedYaw = yaw + MathUtil.Pi;
		float appliedPitch = -Math.Abs(pitch);
		Quaternion rotation = Quaternion.RotationYawPitchRoll(appliedYaw, appliedPitch, 0f);
		Console.WriteLine($"[CameraFit] yawDeg={MathUtil.RadiansToDegrees(appliedYaw):F1} pitchDeg={MathUtil.RadiansToDegrees(appliedPitch):F1} (applied, yaw+180, downward pitch)");

		return (position, rotation);
	}
}
