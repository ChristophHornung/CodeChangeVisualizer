# Architecture

The visualizer is the UI part for the code analyzer. It takes the json output and visualizes it.

# Default visualization

Currently we only have a single default visualization.

Since we work with possibly large projects we keep the view very simple:

1. For every file:
2. Generates a simple color stack which looks like:

[FileName]
----------
(from here every line range is a block of pixels of the same height, i.e. a line object that is 10 lines long is 10
pixels)
XXXX
OOOO
////

(assuming X is e.g. the ComplexityIncreasing line type, O just a code line, //// a comment line)
Every line gets its specific color

3. Each stack is only 40 pixels wide
4. The stacks are arranged horizontally
5. File names are angled at -45° (left bottom to right top) with precise measurement-based sizing and explicit text area
   management
6. No legend is displayed for maximum simplicity

## Color Scheme

- **Red**: Complexity-increasing lines (if, for, while, etc.)
- **Green**: Comment lines
- **Light Green**: Lines containing both code and comments
- **Gray**: Regular code lines
- **White**: Empty lines
