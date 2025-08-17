# CodeChangeVisualizer

A comprehensive code analysis and visualization solution with separate projects for analysis, runner, viewer, and
testing.

## Project Structure

- **CodeChangeVisualizer.Analyzer**: Core analysis engine that processes C# files and identifies line types
- **CodeChangeVisualizer.Runner**: Console application that uses the analyzer and viewer to process directories and
  generate visualizations
- **CodeChangeVisualizer.Viewer**: Class library for generating PNG visualizations using SkiaSharp
- **CodeChangeVisualizer.Stride**: Class library for generating a 3D tower-like visualization (skyscraper per file)
  using Stride
- **CodeChangeVisualizer.Tests**: Unit tests for the analyzer functionality

## Features

- **PNG Generation**: Creates high-quality PNG images from JSON analysis output
- **Color-Coded Visualization**: Different line types are represented with distinct colors
- **File Structure Display**: Shows file names and line group information
- **Legend**: Includes a color legend explaining the line type mappings

## Line Type Color Mapping

- **White**: Empty lines
- **Light Gray**: Comment lines
- **Red**: Complexity-increasing lines (if, for, while, etc.)
- **Blue**: Regular code lines
- **Orange**: Lines containing both code and comments

## Usage

```bash
dotnet run --project CodeChangeVisualizer.Runner -- --directory <path> [options]
```

### Command Line Options

- `-d, --directory <path>` - Directory to analyze (required)
- `-j, --json <file>` - Output JSON analysis to file
- `-v, --visualization <file>` - Output PNG visualization to file
- `-c, --console` - Output JSON analysis to console
- `-h, --help` - Show help message

### Examples

```bash
# Generate JSON only to console
dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --console

# Generate JSON only to file
dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --json analysis.json

# Generate visualization only
dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --visualization output.png

# Generate both JSON and visualization
dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --json analysis.json --visualization output.png

# Backward compatibility (outputs JSON to console)
dotnet run --project CodeChangeVisualizer.Runner -- ./src
```

## Dependencies

- **SkiaSharp**: Cross-platform 2D graphics library for .NET (Viewer project)
- **System.Text.Json**: JSON serialization (Analyzer project)
- **xUnit**: Unit testing framework (Tests project)

## Architecture

The solution consists of:

- **CodeAnalyzer**: Core analysis engine that processes C# files and identifies line types
- **CodeVisualizer**: Main class responsible for generating PNG images
- **Program**: Console application with command-line options for analysis and visualization

## Output Format

The generated PNG images include:

- Color legend at the top
- File headers with separator lines
- Line groups with color-coded rectangles
- Line number ranges and type information

## Building

```bash
dotnet build CodeChangeVisualizer.slnx
```

## Testing

The solution can be tested by:

1. Running unit tests: `dotnet test CodeChangeVisualizer.slnx`
2. Running the runner with different command line options
3. Verifying JSON output and PNG visualizations 