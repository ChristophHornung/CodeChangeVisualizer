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
	private IReadOnlyList<FileAnalysis>? _files;

	// Stability state
	private readonly Dictionary<string, int> _assignedIndexByFile = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Vector3> _positionByFile = new(StringComparer.OrdinalIgnoreCase);
	private List<Vector3> _lastPositions = new(); // positions aligned with the latest SetFiles(files) order
	private int _fixedCols = 0; // fixed number of columns chosen at the first non-empty SetFiles

	public void SetFiles(IReadOnlyList<FileAnalysis> files)
	{
		this._files = files;
		// Initialize fixed columns on the first non-empty call
		if (this._fixedCols <= 0)
		{
			int count = files?.Count ?? 0;
			if (count > 0)
			{
				this._fixedCols = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Sqrt(count)));
			}
		}

		// Rebuild alignment for the provided files order while preserving positions for known files
		this._lastPositions = new List<Vector3>(files?.Count ?? 0);
		if (files == null)
		{
			return;
		}

		for (int i = 0; i < files.Count; i++)
		{
			string file = files[i].File;
			if (!this._positionByFile.TryGetValue(file, out Vector3 pos))
			{
				// Assign a new slot index at the end (append) and compute its position using fixed columns
				int slot = this._assignedIndexByFile.Count;
				this._assignedIndexByFile[file] = slot;
				int col = (this._fixedCols > 0) ? (slot % this._fixedCols) : slot; // if cols not set, fall back to row=0
				int row = (this._fixedCols > 0) ? (slot / this._fixedCols) : 0;
				pos = new Vector3(col * GridCityPlanner.TowerSpacing, 0f, row * GridCityPlanner.TowerSpacing);
				this._positionByFile[file] = pos;
			}
			this._lastPositions.Add(pos);
		}
	}

	public Vector3 GetPosition(int index)
	{
		if (index < 0 || index >= this._lastPositions.Count)
		{
			// Out of bounds: return origin to avoid exceptions
			return Vector3.Zero;
		}
		return this._lastPositions[index];
	}

	public (int rows, int cols, int gridSize) GetGrid()
	{
		int totalCount = this._files?.Count ?? 0;
		if (totalCount <= 0)
		{
			return (0, 0, 0);
		}
		int cols = (this._fixedCols > 0) ? System.Math.Min(this._fixedCols, totalCount) : System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Sqrt(totalCount)));
		int rows = (int)System.Math.Ceiling((float)totalCount / cols);
		int gridSize = System.Math.Max(cols, rows);
		return (rows, cols, gridSize);
	}

	public (Vector3 position, Quaternion rotation) GetFullViewCameraPosition(Game game, CameraComponent camera)
	{
		int count = this._files?.Count ?? 0;
		if (count <= 0)
		{
			// Default camera at origin looking down -Z with no rotation
			return (new Vector3(0f, 0f, 0f), Quaternion.Identity);
		}

		var (rows, cols, gridSize) = this.GetGrid();
		float width = (cols - 1) * GridCityPlanner.TowerSpacing;
		float depth = (rows - 1) * GridCityPlanner.TowerSpacing;
		float maxHeight = (this._files ?? Array.Empty<FileAnalysis>())
			.Select(f => (f.Lines?.Sum(g => g.Length) ?? 0) * LayoutCalculator.Constants.UnitsPerLine)
			.DefaultIfEmpty(0f).Max();

		Console.WriteLine($"[CameraFit] count={count} cols={cols} rows={rows} width={width:F2} depth={depth:F2} maxHeight={maxHeight:F2}");

		// Compute center and bounding sphere radius
		Vector3 center = new Vector3(width / 2f, Math.Max(0.5f, maxHeight / 2f), depth / 2f);
		float radius = 0.5f * (float)System.Math.Sqrt(width * width + depth * depth + maxHeight * maxHeight);

		float vfov = camera.VerticalFieldOfView;
		if (vfov <= 0f)
		{
			vfov = MathUtil.DegreesToRadians(60f);
		}

		Texture? backBuffer = game.GraphicsDevice?.Presenter?.BackBuffer;
		float aspect = (backBuffer != null && backBuffer.Height > 0)
			? (float)backBuffer.Width / backBuffer.Height
			: (16f / 9f);
		float hfov = 2f * (float)System.Math.Atan(System.Math.Tan(vfov / 2f) * aspect);
		float limitingFov = System.Math.Min(vfov, hfov);
		Console.WriteLine($"[CameraFit] aspect={aspect:F3} vfovDeg={MathUtil.RadiansToDegrees(vfov):F1} hfovDeg={MathUtil.RadiansToDegrees(hfov):F1} limitingFovDeg={MathUtil.RadiansToDegrees(limitingFov):F1}");

		float distance = radius / (float)System.Math.Sin(System.Math.Max(0.1f, limitingFov / 2f));
		float margin = 1.25f;
		distance *= margin;
		Console.WriteLine($"[CameraFit] radius={radius:F2} distance={distance:F2}");

		Vector3 dir = Vector3.Normalize(new Vector3(1f, -(float)System.Math.Sqrt(2), 1f));
		Vector3 position = center - dir * distance;
		Console.WriteLine($"[CameraFit] dir=({dir.X:F3},{dir.Y:F3},{dir.Z:F3}) position=({position.X:F2},{position.Y:F2},{position.Z:F2}) center=({center.X:F2},{center.Y:F2},{center.Z:F2})");

		Vector3 fwd = Vector3.Normalize(center - position);
		float yaw = (float)System.Math.Atan2(fwd.X, fwd.Z);
		float pitch = (float)System.Math.Asin(fwd.Y);
		float appliedYaw = yaw + MathUtil.Pi;
		float appliedPitch = -System.Math.Abs(pitch);
		Quaternion rotation = Quaternion.RotationYawPitchRoll(appliedYaw, appliedPitch, 0f);
		Console.WriteLine($"[CameraFit] yawDeg={MathUtil.RadiansToDegrees(appliedYaw):F1} pitchDeg={MathUtil.RadiansToDegrees(appliedPitch):F1} (applied, yaw+180, downward pitch)");

		return (position, rotation);
	}
}
