namespace CodeChangeVisualizer.Analyzer;

/// <summary>
/// Describes the kind of change between two versions of a file.
/// </summary>
public enum FileChangeKind
{
    /// <summary>The file existed in both versions and changed at the block level.</summary>
    Modify,
    /// <summary>The file did not exist previously and was added in the new version.</summary>
    FileAdd,
    /// <summary>The file existed previously and was deleted in the new version.</summary>
    FileDelete
}

/// <summary>
/// Represents a diff between two analyses of a single file, including whole-file add/delete.
/// </summary>
public class FileDiff
{
    /// <summary>
    /// Gets the kind of file change.
    /// </summary>
    public FileChangeKind Kind { get; init; }

    /// <summary>
    /// For <see cref="FileChangeKind.Modify"/>, the list of block-level edits.
    /// </summary>
    public List<DiffEdit>? Edits { get; init; }

    /// <summary>
    /// For <see cref="FileChangeKind.FileAdd"/>, the full set of blocks in the new file.
    /// </summary>
    public List<LineGroup>? NewFileLines { get; init; }

    /// <summary>
    /// Optional original file name (helper for diagnostics/serialization).
    /// </summary>
    public string? OldFileName { get; init; }
    /// <summary>
    /// Optional new file name (helper for diagnostics/serialization).
    /// </summary>
    public string? NewFileName { get; init; }
}
