# CodeChangeVisualizer.Viewer

A class library for generating PNG visualizations from code analysis data using SkiaSharp.

## Features

- **PNG Generation**: Creates high-quality PNG images from JSON analysis output
- **Color-Coded Visualization**: Different line types are represented with distinct colors
- **File Structure Display**: Shows file names and line group information
- **Legend**: Includes a color legend explaining the line type mappings

## Line Type Color Mapping

- **White**: Empty lines
- **Green**: Comment lines
- **Red**: Complexity-increasing lines (if, for, while, etc.)
- **Gray**: Regular code lines
- **Light Green**: Lines containing both code and comments

## Usage

The Viewer is now integrated into the Runner project. Use the Runner with the `--visualization` option:

```bash
dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --visualization output.png
```

## Dependencies

- **SkiaSharp**: Cross-platform 2D graphics library for .NET
- **CodeChangeVisualizer.Runner**: References the analysis engine

## Architecture

The viewer implements a horizontal color stack visualization as specified in the architecture:

- **Simple Design**: For large projects, keeps the view very simple
- **Horizontal Layout**: Files are arranged horizontally as 40-pixel wide stacks
- **Angled Text**: File names are rotated -45° (left bottom to right top) with precise measurement-based sizing and explicit text area management
- **Color Stack**: Each file shows a vertical stack of colored blocks
- **Pixel-Per-Line**: Each line range is represented as a block of pixels with height = number of lines
- **No Legend**: Maximum simplicity for performance with large codebases

The viewer consists of:
- **CodeVisualizer**: Main class responsible for generating PNG images
- **Program**: Console application entry point for command-line usage

## Output Format

The generated PNG images include:
- File headers with precisely measured angled text (-45°) using explicit text area and stack area separation
- Horizontal color stack visualization where:
  - Files are arranged horizontally as 40-pixel wide stacks
  - Each line range is represented as a block of pixels
  - Block height = number of lines in the range
  - Each line type has its specific color
  - No legend for maximum simplicity and performance

## Building

```bash
dotnet build CodeChangeVisualizer.slnx
```

## Testing

The viewer is tested through the Runner project:
1. Run the runner with visualization option
2. Verify the generated PNG file 