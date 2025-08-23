namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;

/// <summary>
/// Defines an abstraction for planning the layout of skyscraper towers (city blocks).
/// Implementations decide how to position towers in world space and describe the grid used.
/// Receives the full list of files so planners can group by subfolders, etc.
/// </summary>
public interface ICityPlanner
{
    /// <summary>
    /// Returns the planned world position for the tower at <paramref name="index"/> given the full file list.
    /// Implementations should not mutate state; positions should be deterministic for the provided inputs.
    /// </summary>
    /// <param name="index">Zero-based index of the tower to place in the provided files list.</param>
    /// <param name="files">The full list of file analyses (only file identities are needed).</param>
    /// <returns>World position for the tower root (Y is typically 0).</returns>
    Vector3 GetPosition(int index, IReadOnlyList<FileAnalysis> files);

    /// <summary>
    /// Computes the grid characteristics for the given files.
    /// </summary>
    /// <param name="files">The full list of file analyses.</param>
    /// <returns>Tuple of (rows, cols, gridSize), where gridSize is the base grid dimension (ceil(sqrt(totalCount))).</returns>
    (int rows, int cols, int gridSize) GetGrid(IReadOnlyList<FileAnalysis> files);
}
