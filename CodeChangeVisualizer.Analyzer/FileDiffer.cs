namespace CodeChangeVisualizer.Analyzer;

public static class FileDiffer
{
    public static FileDiff DiffFile(FileAnalysis oldFile, FileAnalysis newFile)
    {
        if (oldFile == null) throw new ArgumentNullException(nameof(oldFile));
        if (newFile == null) throw new ArgumentNullException(nameof(newFile));

        bool oldEmpty = oldFile.Lines.Count == 0;
        bool newEmpty = newFile.Lines.Count == 0;

        if (oldEmpty && !newEmpty)
        {
            // Whole file add
            return new FileDiff
            {
                Kind = FileChangeKind.FileAdd,
                NewFileLines = newFile.Lines.Select(g => new LineGroup { Type = g.Type, Length = g.Length, Start = g.Start }).ToList(),
                OldFileName = oldFile.File,
                NewFileName = newFile.File
            };
        }

        if (!oldEmpty && newEmpty)
        {
            // Whole file delete
            return new FileDiff
            {
                Kind = FileChangeKind.FileDelete,
                OldFileName = oldFile.File,
                NewFileName = newFile.File
            };
        }

        // Otherwise, standard modify with block edits
        var edits = Differ.Diff(oldFile, newFile);
        return new FileDiff
        {
            Kind = FileChangeKind.Modify,
            Edits = edits,
            OldFileName = oldFile.File,
            NewFileName = newFile.File
        };
    }
}
