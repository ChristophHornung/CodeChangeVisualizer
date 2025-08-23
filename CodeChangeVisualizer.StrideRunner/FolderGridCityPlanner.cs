namespace CodeChangeVisualizer.StrideRunner;

using System.Text;
using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

/// <summary>
/// Alternative planner that groups files by their folder.
/// Each folder gets its own inner grid for its files, and folders are laid out on an outer grid.
/// This can cause towers to move between steps when the set of files/folders changes.
/// </summary>
public sealed class FolderGridCityPlanner : ICityPlanner
{
	private const float TowerSpacing = LayoutCalculator.Constants.TowerSpacing; // spacing between towers within a folder
	private const float FolderSpacingMultiplier = 4.0f; // how much bigger the gap between folders is, in multiples of TowerSpacing

	private IReadOnlyList<FileAnalysis>? _files;
	private List<string> _fileOrder = new();
	private List<string> _folders = new();
	private Dictionary<string, List<string>> _folderToFiles = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, int> _folderIndex = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, int> _fileLocalIndex = new(StringComparer.OrdinalIgnoreCase);

	public void SetFiles(IReadOnlyList<FileAnalysis> files)
	{
		this._files = files;
		// Normalize file order for deterministic indexing (case-insensitive by file path)
		this._fileOrder = (files ?? Array.Empty<FileAnalysis>())
			.Select(f => f.File)
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
			.ToList();

		// Build folder groups
		this._folderToFiles.Clear();
		foreach (string file in this._fileOrder)
		{
			string folder = FolderGridCityPlanner.GetFolderOf(file);
			if (!this._folderToFiles.TryGetValue(folder, out var list))
			{
				list = new List<string>();
				this._folderToFiles[folder] = list;
			}
			list.Add(file);
		}

		// Sort folders
		this._folders = this._folderToFiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
		this._folderIndex = this._folders.Select((f, i) => new { f, i }).ToDictionary(x => x.f, x => x.i, StringComparer.OrdinalIgnoreCase);

		// Within each folder, sort files by name and record local index
		this._fileLocalIndex.Clear();
		foreach (string folder in this._folders)
		{
			List<string> list = this._folderToFiles[folder];
			list.Sort(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < list.Count; i++)
			{
				this._fileLocalIndex[list[i]] = i;
			}
		}
	}

	public Vector3 GetPosition(int index)
	{
		if (this._files == null || this._files.Count == 0)
		{
			return Vector3.Zero;
		}
		if (index < 0 || index >= this._fileOrder.Count)
		{
			index = Math.Clamp(index, 0, Math.Max(0, this._fileOrder.Count - 1));
		}

		string file = this._fileOrder[index];
		string folder = FolderGridCityPlanner.GetFolderOf(file);
		int fIdx = this._folderIndex.TryGetValue(folder, out var fi) ? fi : 0;
		int localIdx = this._fileLocalIndex.TryGetValue(file, out var li) ? li : 0;

		// Outer (folders) grid placement
		int folderCount = this._folders.Count;
		int outerSize = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, folderCount)));
		int outerRow = fIdx / outerSize;
		int outerCol = fIdx % outerSize;
		float outerX = outerCol * TowerSpacing * FolderSpacingMultiplier;
		float outerZ = outerRow * TowerSpacing * FolderSpacingMultiplier;

		// Inner (files within folder) grid placement
		int innerCount = this._folderToFiles.TryGetValue(folder, out var filesInFolder) ? filesInFolder.Count : 1;
		int innerSize = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, innerCount)));
		int innerRow = localIdx / innerSize;
		int innerCol = localIdx % innerSize;
		float innerX = innerCol * TowerSpacing;
		float innerZ = innerRow * TowerSpacing;

		return new Vector3(outerX + innerX, 0f, outerZ + innerZ);
	}

	public (int rows, int cols, int gridSize) GetGrid()
	{
		// For legacy callers; return outer grid characteristics
		int folderCount = this._folders.Count;
		if (folderCount <= 0)
		{
			return (0, 0, 0);
		}
		int gridSize = (int)Math.Ceiling(Math.Sqrt(folderCount));
		int rows = (int)Math.Ceiling((float)folderCount / gridSize);
		int cols = Math.Min(gridSize, folderCount);
		return (rows, cols, gridSize);
	}

	public (Vector3 position, Quaternion rotation) GetFullViewCameraPosition(Game game, CameraComponent camera)
	{
		int count = this._fileOrder.Count;
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
		float maxHeight = (this._files ?? Array.Empty<FileAnalysis>())
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

	private static string GetFolderOf(string file)
	{
		if (string.IsNullOrEmpty(file)) return "";
		// Normalize separators and take directory part
		string normalized = file.Replace('\\', '/');
		int idx = normalized.LastIndexOf('/');
		return idx >= 0 ? normalized.Substring(0, idx) : ""; // root-level files share empty folder key
	}
}