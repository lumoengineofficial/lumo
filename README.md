<div align="center">

  <img src="lumologo.png" width="150" alt="Lumo Engine Logo" />

  # Lumo Engine

  ### Create Worlds. Build Games. Bring Ideas to Life.

  **An open-source C#/.NET game engine with a powerful desktop editor,
  visual scripting, asset importing, and a standalone game runtime.**

  <br />

  <p>
    <a href="https://github.com/lumoengineofficial/lumo/actions/workflows/dotnet.yml">
      <img src="https://github.com/lumoengineofficial/lumo/actions/workflows/dotnet.yml/badge.svg" alt="Build Status" />
    </a>
    <img src="https://img.shields.io/badge/Tests-164%20Passed-22c55e?style=flat-square" alt="Tests" />
    <a href="https://github.com/lumoengineofficial/lumo/releases">
      <img src="https://img.shields.io/github/v/release/lumoengineofficial/lumo?style=flat-square&label=Latest%20Release" alt="Latest Release" />
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="MIT License" />
    </a>
  </p>

  <p>
    <a href="#-features">Features</a> •
    <a href="#-download">Download</a> •
    <a href="#-getting-started">Getting Started</a> •
    <a href="#-architecture">Architecture</a> •
    <a href="#-roadmap">Roadmap</a>
  </p>

</div>

---

## ✦ About Lumo

**Lumo Engine** is a C#/.NET game engine designed to make game development
more accessible through a dedicated editor, flexible scripting, and a
standalone runtime.

Built around a modular architecture, Lumo brings essential game development
tools together in one environment. Create scenes, manipulate objects,
import 3D assets, write C# scripts, experiment with visual scripting,
and build playable projects.

Whether you're experimenting with game development or building your own
interactive worlds, Lumo aims to provide a practical foundation for
creating games with C#.

### What Lumo offers

- **A dedicated editor** powered by Avalonia UI.
- **Real-time scene editing** with object manipulation tools.
- **3D rendering** powered by OpenGL and Silk.NET.
- **Visual scripting** through a node-based graph editor.
- **C# scripting** for customizable game behavior.
- **Asset importing** for supported 3D models, textures, and sprites.
- **A standalone runtime** for running games outside the editor.
- **A modular project structure** with support for plugins.

---

## ✦ Features

### Scene Editor

Build and organize your game worlds with an editor designed around
a familiar scene-based workflow.

- Scene tree and entity inspector.
- Interactive play mode.
- Undo and redo support.
- History panel for editing operations.
- Object selection and manipulation.
- Keyboard shortcuts for common actions.

### Transform Tools

Manipulate objects directly in the editor.

- Move, Rotate, and Scale gizmos.
- Grid snapping.
- Camera focus with the `F` key.
- Copy and Paste operations.
- Reset individual actor transforms.

### Rendering

Lumo uses a graphics pipeline built around OpenGL.

- OpenGL 3.3 rendering through Silk.NET.
- Automatic software-renderer fallback.
- Support for imported 3D models and textures.
- Sprite asset support.

> Rendering capabilities depend on the available graphics environment
> and the current implementation.

### Asset Importing

Bring external assets into your projects.

- OBJ model importing.
- glTF 2.0 model importing.
- Texture and sprite assets.
- Automatic decimation for supported large models.
- FileSystem-based asset browsing.

### Visual Scripting

Create gameplay logic using a node-based visual scripting system.

- Node graph interface.
- Node palette.
- Visual connections and wires.
- Typed node properties.

Visual scripting provides an alternative workflow for constructing
game logic without writing every operation directly in C#.

### C# Scripting

Extend gameplay behavior using C#.

- Attach C# scripts to actors through the editor.
- Build custom gameplay logic.
- Organize scripts within game projects.
- Use the engine's supported scripting interfaces.

### Project Management

Manage projects through the Lumo editor.

- Create, open, and save projects.
- Browse project assets.
- Build and run projects with a single action.
- Organize game resources and scenes.

### Standalone Runtime

Run games independently of the editor using `Lumo.Runtime`.

- Dedicated game player.
- Separate runtime project.
- Plugin API support.
- Packaged sample game.

### Development Quality

Lumo includes automated testing and continuous integration.

- 164 reported xUnit tests passed.
- GitHub Actions build workflow.
- Automated build checks on pushes.

> Test counts and workflow status may change as development continues.

---

## ✦ Download

Ready to try Lumo?

**Download the latest Windows release:**

<div align="center">

  <a href="https://github.com/lumoengineofficial/lumo/releases">
    <img src="https://img.shields.io/badge/Download-Lumo%20Engine-7C3AED?style=for-the-badge&logo=windows&logoColor=white" alt="Download Lumo Engine" />
  </a>

  <br />
  <br />

  <sub>Windows x64 • Self-contained release package</sub>

</div>

### Installation

1. Open the [Releases](https://github.com/lumoengineofficial/lumo/releases) page.
2. Download the latest Windows ZIP package.
3. Extract the archive.
4. Run `Lumo.Editor.exe`.

The self-contained editor package is intended to run without a separate
.NET installation.

For the exact requirements of each release, consult its release notes.

---

## ✦ Getting Started

You can also build Lumo from source.

### Prerequisites

- Windows for the desktop editor.
- A compatible .NET SDK version.
- Git.
- A development environment such as Visual Studio or Visual Studio Code.

The repository documentation specifies .NET SDK 10 or 11. Use the SDK
version supported by the current source and CI configuration.

### 1. Clone the Repository

```bash
git clone https://github.com/lumoengineofficial/lumo.git
cd lumo