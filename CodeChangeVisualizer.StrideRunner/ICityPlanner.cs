namespace CodeChangeVisualizer.StrideRunner;

using CodeChangeVisualizer.Analyzer;
using Stride.Core.Mathematics;
using Stride.Engine;

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
    /// Computes a camera position and rotation that frames all towers/files currently set via SetFiles.
    /// This encapsulates the width/depth/height, FOV, and aspect calculations used to initially position the camera.
    /// </summary>
    /// <param name="game">Stride Game instance used to query back buffer for aspect ratio.</param>
    /// <param name="camera">Camera component whose FOV will be used for framing.</param>
    /// <returns>A tuple of (position, rotation) for the camera.</returns>
    (Vector3 position, Quaternion rotation) GetFullViewCameraPosition(Game game, CameraComponent camera);
}
