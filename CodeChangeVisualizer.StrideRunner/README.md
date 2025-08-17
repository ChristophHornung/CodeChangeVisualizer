# CodeChangeVisualizer.StrideRunner

A 3D visualization tool for code analysis data using Stride Game Engine.

## Overview

This project provides a 3D "skyscraper" visualization of code analysis data, where each file is represented as a tower
of colored blocks. Each block represents a line group with a specific line type, and the height of each block
corresponds to the number of lines in that group.

## Features

- **3D Skyscraper Visualization**: Files are displayed as vertical towers along the X-axis
- **Color-Coded Blocks**: Different line types are represented with distinct colors:
    - **White**: Empty lines
    - **Green**: Comment lines
    - **Red**: Complexity-increasing lines (if, for, while, etc.)
    - **Gray**: Regular code lines
    - **Light Green**: Lines containing both code and comments
- **Interactive 3D View**: Navigate around the visualization using mouse and keyboard
- **Real-time Rendering**: Powered by Stride Game Engine for smooth 3D graphics

## Usage

1. First, generate analysis data using the Runner project:
   ```bash
   dotnet run --project CodeChangeVisualizer.Runner -- --directory ./src --output analysis.json
   ```

2. Then visualize the data using StrideRunner:
   ```bash
   dotnet run --project CodeChangeVisualizer.StrideRunner -- analysis.json
   ```

## Controls

- **Mouse**: Rotate camera view
- **WASD**: Move camera
- **Mouse Wheel**: Zoom in/out
- **Escape**: Exit the application

## Architecture

The StrideRunner consists of:

- **Program.cs**: Entry point that loads JSON analysis data and starts the Stride game
- **VisualizeGame.cs**: Stride game class that sets up the 3D scene and camera
- **SkyscraperVisualizer.cs**: Builds the 3D scene from analysis data
- **BlockDescriptorComponent.cs**: Component that stores block size and color information

## Dependencies

- **Stride.Engine**: 3D game engine for rendering
- **CodeChangeVisualizer.Stride**: Contains the visualization logic
- **System.Text.Json**: For parsing analysis data

## Building

```bash
dotnet build CodeChangeVisualizer.slnx
```

## Comparison with Viewer Project

While the Viewer project creates 2D PNG images, the StrideRunner provides an interactive 3D experience:

- **Viewer**: Static 2D horizontal color stacks
- **StrideRunner**: Interactive 3D vertical towers

Both use the same color scheme and represent the same data, but offer different visualization approaches. 

## Why Diffuse looked black (and why Emissive fixed it)

- Stride uses a PBR material model. A Diffuse/Albedo color only reflects light; it doesn't emit any.
- With no/insufficient lights, grazing light directions, dark environment lighting, or low exposure/tonemapping, albedo surfaces can render very dark or black.
- Adding an Emissive feature makes the material self-lit, so it shows the intended color regardless of lighting. This is ideal for debugging or flat-color visualization, but not physically accurate if strong emissive is left enabled everywhere.

How to make Diffuse work without Emissive:
- Add/adjust lighting: e.g., a directional light pointing at the towers; optionally add skybox/environment lighting; increase light intensity.
- Adjust exposure/tonemapping so the scene isn’t underexposed.
- Ensure camera framing and face orientation are correct (avoid backface culling hiding faces).
- Alternatively, use an Unlit material for pure color rendering when physical lighting isn’t desired.

In this runner we enable Emissive on block materials to guarantee visibility out-of-the-box. You can tune lights later and reduce/remove Emissive for physically-plausible shading.
