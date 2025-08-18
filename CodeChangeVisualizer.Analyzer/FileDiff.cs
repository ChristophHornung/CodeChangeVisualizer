namespace CodeChangeVisualizer.Analyzer;

public enum FileChangeKind
{
    Modify,
    FileAdd,
    FileDelete
}

public class FileDiff
{
    public FileChangeKind Kind { get; init; }

    // For Modify: Edits is populated
    public List<DiffEdit>? Edits { get; init; }

    // For FileAdd: NewFileLines carries all blocks of the new file
    public List<LineGroup>? NewFileLines { get; init; }

    // File names (optional helpers)
    public string? OldFileName { get; init; }
    public string? NewFileName { get; init; }
}
