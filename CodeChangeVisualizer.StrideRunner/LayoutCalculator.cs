namespace CodeChangeVisualizer.StrideRunner;

using System;
using System.Collections.Generic;
using System.Linq;
using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;

/// <summary>
/// Pure, headless geometry/layout calculator used by tests to validate positions and sizes
/// without needing to boot Stride. Visualization code should keep constants in sync by
/// referencing this class to avoid drift.
/// </summary>
public static class LayoutCalculator
{
	public static class Constants
	{
		public const float UnitsPerLine = 0.02f; // 50 lines per unit
		public const float TowerSpacing = 3.0f;
		public const float BlockWidth = 1.0f;
		public const float BlockDepth = 1.0f;
	}

	public sealed record BlockLayout(
		int Index,
		LineType LineType,
		float Height,
		Vector3 Size,
		Vector3 CenterPosition
	);

	public sealed record TowerLayout(
		string File,
		int GridIndex,
		Vector3 Position,
		IReadOnlyList<BlockLayout> Blocks
	);

	/// <summary>
	/// Computes the grid (x,z) position for a given tower index using the same
	/// algorithm as the visualizer: square grid packing with spacing.
	/// </summary>
	public static Vector3 ComputeTowerPosition(int gridIndex)
	{
		int gridSize = (int)Math.Ceiling(Math.Sqrt(gridIndex + 1));
		int row = gridIndex / gridSize;
		int col = gridIndex % gridSize;
		float x = col * Constants.TowerSpacing;
		float z = row * Constants.TowerSpacing;
		return new Vector3(x, 0f, z);
	}

	/// <summary>
	/// Computes block layouts (center position and size) for a single file tower at the given base position.
	/// Blocks are stacked along +Y, with each block centered at currentY + height / 2.
	/// </summary>
	public static IReadOnlyList<BlockLayout> ComputeBlocks(FileAnalysis file)
	{
		List<BlockLayout> blocks = new();
		float currentY = 0f;
		for (int i = 0; i < file.Lines.Count; i++)
		{
			LineGroup group = file.Lines[i];
			float height = group.Length * Constants.UnitsPerLine;
			Vector3 size = new(Constants.BlockWidth, height, Constants.BlockDepth);
			Vector3 center = new(0f, currentY + height / 2f, 0f);
			blocks.Add(new BlockLayout(i, group.Type, height, size, center));
			currentY += height;
		}
		return blocks;
	}

	/// <summary>
	/// Computes full city layout: grid positions for each file tower and per-block layouts.
	/// </summary>
	public static IReadOnlyList<TowerLayout> ComputeCityLayout(IReadOnlyList<FileAnalysis> analysis)
	{
		List<TowerLayout> result = new();
		for (int index = 0; index < analysis.Count; index++)
		{
			FileAnalysis file = analysis[index];
			Vector3 basePos = ComputeTowerPosition(index);
			var blocks = ComputeBlocks(file);
			// Note: block centers are relative to tower root; tower root is placed at basePos.
			result.Add(new TowerLayout(file.File, index, basePos, blocks));
		}
		return result;
	}
}
