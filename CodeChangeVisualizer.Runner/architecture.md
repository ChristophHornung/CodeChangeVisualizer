# Architecture

A console program that

1. Takes a directory
2. Reads all C# files in that directory
3. Creates a json with the format
   {[
   "file": "src/Foo.cs",
   "lines": [
   {start:1,
   lenght:2,
   type:Comment/ComplexityIncreasing/Code/CodeAndComment/Empty}
   ]
   ]}

Where each line element is a range of lines that all have the same type:

Comment: A comment (Block or single line)
ComplexityIncreasing: A line or block of code that increases cyclomatic complexity (e.g., if, for, while, switch, case,
catch, etc.)
Code: Regular code that does not fall into the above categories.
CodeAndComment: A line or block that contains both code and a comment.
Empty: An empty or whitespace-only line.

The program processes each file, analyzes the lines, groups them by type, and outputs the result as a JSON array as
shown above. This allows for easy visualization and further processing of code changes and structure.
