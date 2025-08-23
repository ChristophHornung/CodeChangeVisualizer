namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;

/// <summary>
/// Default city planner: places towers on a square grid with equal spacing.
/// Matches the previous layout logic used in SkyscraperVisualizer and DiffPlaybackScript.
/// Ignores file details for now; keeps behavior unchanged.
/// </summary>
public sealed class GridCityPlanner : ICityPlanner
{
	private const float TowerSpacing = LayoutCalculator.Constants.TowerSpacing;
	private IReadOnlyList<FileAnalysis>? _files;

	public void SetFiles(IReadOnlyList<FileAnalysis> files)
	{
		this._files = files;
	}

	public Vector3 GetPosition(int index)
	{
		if (index < 0) index = 0;
		int totalCount = this._files?.Count ?? (index + 1);
		if (totalCount < 1) totalCount = index + 1;
		int gridSize = (int)System.Math.Ceiling(System.Math.Sqrt(totalCount));
		int row = index / gridSize;
		int col = index % gridSize;
		float x = col * GridCityPlanner.TowerSpacing;
		float z = row * GridCityPlanner.TowerSpacing;
		return new Vector3(x, 0f, z);
	}

	public (int rows, int cols, int gridSize) GetGrid()
	{
		int totalCount = this._files?.Count ?? 0;
		if (totalCount <= 0)
		{
			return (0, 0, 0);
		}
		int gridSize = (int)System.Math.Ceiling(System.Math.Sqrt(totalCount));
		int rows = (int)System.Math.Ceiling((float)totalCount / gridSize);
		int cols = System.Math.Min(gridSize, totalCount);
		return (rows, cols, gridSize);
	}
}
