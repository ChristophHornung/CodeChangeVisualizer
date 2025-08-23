namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;

/// <summary>
/// Defines an abstraction for planning the layout of skyscraper towers (city blocks).
/// Implementations decide how to position towers in world space and describe the grid used.
/// Now stateful: set the current file list once, then query positions.
/// </summary>
public interface ICityPlanner
{
    /// <summary>
    /// Sets the current files used for layout decisions.
    /// </summary>
    void SetFiles(IReadOnlyList<FileAnalysis> files);

    /// <summary>
    /// Returns the planned world position for the tower at <paramref name="index"/> using the previously set files.
    /// </summary>
    /// <param name="index">Zero-based index of the tower to place in the current files list.</param>
    /// <returns>World position for the tower root (Y is typically 0).</returns>
    Vector3 GetPosition(int index);

    /// <summary>
    /// Computes the grid characteristics for the current files.
    /// </summary>
    /// <returns>Tuple of (rows, cols, gridSize), where gridSize is the base grid dimension (ceil(sqrt(totalCount))).</returns>
    (int rows, int cols, int gridSize) GetGrid();
}
