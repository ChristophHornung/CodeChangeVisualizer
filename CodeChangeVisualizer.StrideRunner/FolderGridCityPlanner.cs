namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

/// <summary>
/// Alternative planner that groups files by their folder.
/// Each folder gets its own inner grid for its files, and folders are laid out on an outer grid.
/// Stabilized: once a folder/file gets a slot, it keeps it across SetFiles updates. New items append.
/// </summary>
public sealed class FolderGridCityPlanner : ICityPlanner
{
	private const float TowerSpacing = LayoutCalculator.Constants.TowerSpacing; // spacing between towers within a folder
	private const float FolderSpacingMultiplier = 4.0f; // how much bigger the gap between folders is, in multiples of TowerSpacing

	private IReadOnlyList<FileAnalysis>? files;
	private List<string> fileOrder = new();

	// Stable outer (folder) layout state
	private readonly Dictionary<string, int> folderAssignedIndex = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Vector3> folderPosition = new(StringComparer.OrdinalIgnoreCase);

	// Stable inner (file-within-folder) layout state
	private readonly Dictionary<string, int>
		fileAssignedLocalIndex = new(StringComparer.OrdinalIgnoreCase); // by file path

	private readonly Dictionary<string, Vector3>
		fileLocalPosition = new(StringComparer.OrdinalIgnoreCase); // inner offset by file path

	// Current mapping for quick GetPosition lookup aligned with latest SetFiles order
	private readonly Dictionary<string, string> fileToFolder = new(StringComparer.OrdinalIgnoreCase);
	private List<Vector3> lastPositions = new();

	public void SetFiles(IReadOnlyList<FileAnalysis> files)
	{
		this.files = files;
		// Normalize file order for deterministic indexing (case-insensitive by file path)
		this.fileOrder = (files ?? Array.Empty<FileAnalysis>())
			.Select(f => f.File)
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
			.ToList();

		this.lastPositions = new List<Vector3>(this.fileOrder.Count);
		this.fileToFolder.Clear();

		// Ensure folders are assigned stable outer slots
		// Build ordered list of folders present in this call
		List<string> foldersThisCall = this.fileOrder
			.Select(GetFolderOf)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (string folder in foldersThisCall)
		{
			if (!this.folderAssignedIndex.TryGetValue(folder, out int slot))
			{
				// Assign next outer slot
				slot = this.folderAssignedIndex.Count;
				this.folderAssignedIndex[folder] = slot;
				this.folderPosition[folder] = FolderGridCityPlanner.ComputeSquareLayerPosition(slot,
					FolderGridCityPlanner.TowerSpacing * FolderGridCityPlanner.FolderSpacingMultiplier);
			}
		}

		// For each file, ensure it has a stable inner slot within its folder and compute absolute position
		// Track how many files are already assigned per folder to append new ones
		Dictionary<string, int> assignedCountPerFolder = new(StringComparer.OrdinalIgnoreCase);
		foreach (var kv in this.fileAssignedLocalIndex)
		{
			string f = GetFolderOf(kv.Key);
			assignedCountPerFolder[f] = Math.Max(assignedCountPerFolder.TryGetValue(f, out int c) ? c : 0, kv.Value + 1);
		}

		foreach (string file in this.fileOrder)
		{
			string folder = GetFolderOf(file);
			this.fileToFolder[file] = folder;

			if (!this.fileAssignedLocalIndex.TryGetValue(file, out int localSlot))
			{
				int countSoFar = assignedCountPerFolder.TryGetValue(folder, out int c) ? c : 0;
				localSlot = countSoFar; // append within folder
				this.fileAssignedLocalIndex[file] = localSlot;
				assignedCountPerFolder[folder] = countSoFar + 1;
				this.fileLocalPosition[file] =
					FolderGridCityPlanner.ComputeSquareLayerPosition(localSlot, FolderGridCityPlanner.TowerSpacing);
			}
			// Absolute position = outer folder position + inner file offset
			Vector3 outer = this.folderPosition.TryGetValue(folder, out var op) ? op : Vector3.Zero;
			Vector3 inner = this.fileLocalPosition.TryGetValue(file, out var ip) ? ip : Vector3.Zero;
			this.lastPositions.Add(new Vector3(outer.X + inner.X, 0f, outer.Z + inner.Z));
		}
	}

	public Vector3 GetPosition(int index)
	{
		if (this.files == null || this.files.Count == 0)
		{
			return Vector3.Zero;
		}

		if (index < 0 || index >= this.lastPositions.Count)
		{
			return Vector3.Zero;
		}

		return this.lastPositions[index];
	}

	public (int rows, int cols, int gridSize) GetGrid()
	{
		// Return a square bound that encloses all assigned folder positions.
		int folderCount = this.folderAssignedIndex.Count;
		if (folderCount <= 0)
		{
			return (0, 0, 0);
		}
		int k = (int)Math.Ceiling(Math.Sqrt(folderCount));
		return (k, k, k);
	}

	public (Vector3 position, Quaternion rotation) GetFullViewCameraPosition(Game game, CameraComponent camera)
	{
		int count = this.fileOrder.Count;
		if (count <= 0)
		{
			return (new Vector3(0f, 0f, 0f), Quaternion.Identity);
		}

		// Compute bounds by sampling all tower positions
		float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
		for (int i = 0; i < count; i++)
		{
			Vector3 p = this.GetPosition(i);
			minX = Math.Min(minX, p.X);
			maxX = Math.Max(maxX, p.X);
			minZ = Math.Min(minZ, p.Z);
			maxZ = Math.Max(maxZ, p.Z);
		}
		float width = Math.Max(0f, maxX - minX);
		float depth = Math.Max(0f, maxZ - minZ);
		float maxHeight = (this.files ?? Array.Empty<FileAnalysis>())
			.Select(f => (f.Lines?.Sum(g => g.Length) ?? 0) * LayoutCalculator.Constants.UnitsPerLine)
			.DefaultIfEmpty(0f).Max();

		Console.WriteLine($"[CameraFit] count={count} width={width:F2} depth={depth:F2} maxHeight={maxHeight:F2} (FolderGrid)");

		Vector3 center = new Vector3(minX + width / 2f, Math.Max(0.5f, maxHeight / 2f), minZ + depth / 2f);
		float radius = 0.5f * (float)Math.Sqrt(width * width + depth * depth + maxHeight * maxHeight);

		float vfov = camera.VerticalFieldOfView;
		if (vfov <= 0f) vfov = MathUtil.DegreesToRadians(60f);
		Texture? backBuffer = game.GraphicsDevice?.Presenter?.BackBuffer;
		float aspect = (backBuffer != null && backBuffer.Height > 0)
			? (float)backBuffer.Width / backBuffer.Height
			: (16f / 9f);
		float hfov = 2f * (float)Math.Atan(Math.Tan(vfov / 2f) * aspect);
		float limitingFov = Math.Min(vfov, hfov);

		float distance = radius / (float)Math.Sin(Math.Max(0.1f, limitingFov / 2f)) * 1.25f; // margin
		Vector3 dir = Vector3.Normalize(new Vector3(1f, -(float)Math.Sqrt(2), 1f));
		Vector3 position = center - dir * distance;

		Vector3 fwd = Vector3.Normalize(center - position);
		float yaw = (float)Math.Atan2(fwd.X, fwd.Z);
		float pitch = (float)Math.Asin(fwd.Y);
		float appliedYaw = yaw + MathUtil.Pi;
		float appliedPitch = -Math.Abs(pitch);
		Quaternion rotation = Quaternion.RotationYawPitchRoll(appliedYaw, appliedPitch, 0f);
		return (position, rotation);
	}

	private static Vector3 ComputeSquareLayerPosition(int slot, float spacing)
	{
		// Square-layer growth pattern: expands in both axes without moving prior slots
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
		return new Vector3(col * spacing, 0f, row * spacing);
	}

	private static string GetFolderOf(string file)
	{
		if (string.IsNullOrEmpty(file)) return "";
		// Normalize separators and take directory part
		string normalized = file.Replace('\\', '/');
		int idx = normalized.LastIndexOf('/');
		return idx >= 0 ? normalized.Substring(0, idx) : ""; // root-level files share empty folder key
	}
}