# CodeChangeVisualizer.Tests

A comprehensive XUnit test suite for the CodeChangeVisualizer.Runner project, focusing on line type detection accuracy.

## Test Coverage

The test suite includes comprehensive unit tests that verify individual line type detection:

### Test Categories

1. **Empty Lines** - Tests for whitespace-only lines
2. **Comment Lines** - Tests for various comment types (//, /*, */, ///, *)
3. **Complexity Increasing Lines** - Tests for keywords that increase cyclomatic complexity
4. **Regular Code Lines** - Tests for standard code statements
5. **Code with Comments** - Tests for lines containing both code and comments
6. **Complexity Increasing with Comments** - Tests for complexity keywords with comments
7. **Edge Cases** - Tests for boundary conditions
8. **Commented Out Code** - Tests for commented-out code blocks
9. **Keyword Detection Accuracy** - Tests for precise keyword matching

### Line Types Tested

- **Empty**: `""`, `"   "`, `"\t"`, `"\n"`
- **Comment**: `"// comment"`, `"/// XML doc"`, `"/* block */"`, `"* continuation"`
- **ComplexityIncreasing**: `"if (condition)"`, `"for (int i = 0; i < 10; i++)"`, `"switch (value)"`, `"try"`, `"catch (Exception ex)"`, `"return value;"`, `"break;"`, `"continue;"`, `"throw new Exception();"`, `"await Task.Delay(1000);"`, `"lock (obj)"`
- **Code**: `"var x = 5;"`, `"Console.WriteLine(\"Hello\");"`, `"public void Method()"`, `"x++;"`, `"Method();"`
- **CodeAndComment**: `"var x = 5; // Initialize x"`, `"Console.WriteLine(\"Hello\"); // Print greeting"`, `"x++; // Increment"`

### Test Results

- **Total Tests**: 88
- **Passed**: 88 ✅
- **Failed**: 0 ❌
- **Coverage**: 100%

## Test Architecture

### Test Structure

- **Theory Tests**: Use `[Theory]` and `[InlineData]` for parameterized testing
- **Single Line Analysis**: Each test analyzes a single line in isolation
- **Temporary File Creation**: Tests create temporary `.cs` files for realistic analysis
- **Cleanup**: Proper cleanup of temporary files after each test

### Key Test Methods

- `AnalyzeSingleLine()`: Helper method that creates a temporary file with a single line and analyzes it
- `LineTypeDetectionTests`: Main test class containing all line type detection tests

## Test Data Examples

### Complexity Keywords Tested

```csharp
// Control flow
"if (condition)", "else", "for (int i = 0; i < 10; i++)", "foreach (var item in items)"
"while (condition)", "do", "switch (value)", "case 1:", "case \"test\":"

// Exception handling
"try", "catch (Exception ex)", "finally", "throw new Exception();"

// Jump statements
"return value;", "break;", "continue;", "goto label;"

// Async and other
"yield return item;", "await Task.Delay(1000);", "lock (obj)"
```

### Comment Types Tested

```csharp
// Single-line comments
"// This is a comment", "//", "  // Comment with leading spaces"

// XML documentation
"/// XML documentation comment", "/// <summary>", "/// <param name=\"x\">Parameter</param>"

// Block comments
"/* Block comment start", " * Block comment continuation", "*/", "  * Indented block comment"
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "LineTypeDetectionTests"
```

## Test Validation

The tests validate that the `CodeAnalyzer` correctly identifies:

1. **Empty lines** as `Empty`
2. **Pure comment lines** as `Comment`
3. **Lines with complexity keywords** as `ComplexityIncreasing`
4. **Regular code lines** as `Code`
5. **Lines with code and comments** as `CodeAndComment`
6. **Lines with complexity keywords and comments** as `ComplexityIncreasing`

## Implementation Notes

- Tests use temporary files to simulate real file analysis
- Each test case is isolated and independent
- Comprehensive coverage of edge cases and boundary conditions
- Tests validate both the detection logic and the JSON serialization
- All tests pass consistently with the current implementation 