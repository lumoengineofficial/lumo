<p align="center">
  <img src="lumologo.png" width="140" alt="Lumo Engine logo"/>
</p>

<h1 align="center">Lumo Engine</h1>

<p align="center">
  A C#/.NET game engine with a full Avalonia editor, visual scripting, asset import and a standalone game runtime.
</p>

<p align="center">
  <a href="https://github.com/lumoengineofficial/lumo/actions/workflows/dotnet.yml"><img src="https://github.com/lumoengineofficial/lumo/actions/workflows/dotnet.yml/badge.svg" alt="Build"/></a>
  <img src="https://img.shields.io/badge/tests-164%20passed-brightgreen" alt="Tests"/>
  <a href="https://github.com/lumoengineofficial/lumo/releases"><img src="https://img.shields.io/github/v/release/lumoengineofficial/lumo" alt="Release"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License"/></a>
</p>

## Highlights

- **Scene editor** — scene tree, entity inspector, play mode, undo/redo (Ctrl+Z), history panel
- **Transform tools** — Move / Rotate / Scale gizmos with grid snapping, camera focus (`F`), Copy / Paste / Reset per actor
- **Rendering** — OpenGL 3.3 (Silk.NET) with an automatic software-renderer fallback
- **Asset import** — OBJ and glTF 2.0 models (huge models are auto-decimated so the editor stays smooth), textures and sprites
- **Visual scripting** — node-graph system with a palette, wires and typed properties
- **Scripting** — attach C# scripts to actors directly from the editor
- **Project system** — create / open / save projects, FileSystem asset browser, one-click build & run
- **Runtime** — standalone game player (`Lumo.Runtime`) plus plugins support
- **Quality** — 164 xUnit tests, GitHub Actions CI on every push

## Download

Grab the latest **Windows zip** from the [Releases](https://github.com/lumoengineofficial/lumo/releases) page, extract it and run `Lumo.Editor.exe` — no .NET installation required.

## Build from source

Requirements: [.NET SDK](https://dotnet.microsoft.com/download) 10 or 11, Windows (for the editor UI).

```bash
git clone https://github.com/lumoengineofficial/lumo.git
cd lumo
dotnet build LumoEngine.sln
```

Run the tests:

```bash
dotnet test tests/Lumo.Tests
```

Run the editor:

```bash
dotnet run --project src/Lumo.Editor
```

## Packaging (release zips)

```bash
powershell -ExecutionPolicy Bypass -File tools/pack.ps1
```

Produces self-contained `dist/LumoEngine-<version>-win-x64.zip` (editor) and
`dist/Wildlands-<version>-win-x64.zip` (sample game) — no .NET install needed
on the target machine. Use `-SkipEditor` / `-SkipGame` for one of the two.

Known limitations are tracked in [KNOWN_BUGS.md](KNOWN_BUGS.md).

### Installer (setuplumo.exe)

```bash
powershell -ExecutionPolicy Bypass -File tools/mkesetup.ps1
```

Produces `dist/setuplumo.exe` — a Windows installer (Inno Setup 6) that installs
the editor + bundled Wildlands project, optionally the standalone game player,
and creates Start Menu / desktop shortcuts. One-shot: runs `pack.ps1 -KeepStage`
then compiles `tools/setup.iss`. Requires Inno Setup 6
(`winget install JRSoftware.InnoSetup`).

## Projects

| Project | Description |
|---------|-------------|
| `Lumo.Engine` | Core engine — scene, rendering, physics, audio, scripting |
| `Lumo.Editor` | Avalonia-based editor application |
| `Lumo.Runtime` | Game runtime player |
| `Lumo.Tools` | Asset / logo processing tools |
| `Lumo.Examples` | Example scenes |
| `Lumo.Plugins` | Plugin API |
| `Lumo.Tests` | Unit tests |

## License

MIT — see [LICENSE](LICENSE).
