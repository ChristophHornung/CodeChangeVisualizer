# CodeChangeVisualizer.Runner

A console application that analyzes C# files in a directory and outputs a JSON representation of code structure and
complexity.

## Features

- **File Analysis**: Scans all `.cs` files in a directory and subdirectories
- **Line Type Classification**: Categorizes each line into one of five types:
    - `Comment`: Single-line or block comments
    - `ComplexityIncreasing`: Code that increases cyclomatic complexity (if, for, while, switch, etc.)
    - `Code`: Regular code that doesn't increase complexity
    - `CodeAndComment`: Lines containing both code and comments
    - `Empty`: Empty or whitespace-only lines
- **Line Grouping**: Groups consecutive lines of the same type into ranges
- **JSON Output**: Produces structured JSON output for easy processing

## Usage

```bash
dotnet run <directory_path>
```

### Example

```bash
dotnet run .
```

## Output Format

The program outputs a JSON array where each element represents a file:

```json
[
  {
    "file": "src/Example.cs",
    "lines": [
      {
        "start": 1,
        "length": 2,
        "type": "Comment"
      },
      {
        "start": 3,
        "length": 5,
        "type": "Code"
      },
      {
        "start": 8,
        "length": 1,
        "type": "ComplexityIncreasing"
      }
    ]
  }
]
```

## Architecture

### Classes

- **`Program`**: Main entry point that handles command-line arguments and orchestrates the analysis
- **`CodeAnalyzer`**: Core analysis engine that processes files and determines line types
- **`FileAnalysis`**: Data structure representing the analysis results for a single file
- **`LineGroup`**: Represents a range of consecutive lines with the same type
- **`LineType`**: Enum defining the five possible line types

### Key Methods

- **`AnalyzeDirectoryAsync`**: Processes all C# files in a directory
- **`AnalyzeFileAsync`**: Analyzes a single file and groups lines by type
- **`DetermineLineType`**: Classifies a single line based on its content
- **`IsCommentLine`**: Detects comment lines (//, /*, *, ///)
- **`IsComplexityIncreasingLine`**: Identifies complexity-increasing constructs
- **`HasCodeContent`**: Determines if a line contains actual code

## Complexity Detection

The analyzer identifies the following complexity-increasing constructs:

- Control flow statements: `if`, `else`, `for`, `foreach`, `while`, `do`
- Switch statements: `switch`, `case`
- Exception handling: `try`, `catch`, `finally`, `throw`
- Jump statements: `return`, `break`, `continue`, `goto`
- Async constructs: `await`, `yield`
- Synchronization: `lock`

## Error Handling

- Validates directory existence
- Handles file reading errors gracefully
- Provides clear error messages for invalid usage

## Dependencies

- .NET 9.0
- System.Text.Json for JSON serialization 