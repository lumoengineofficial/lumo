# Known issues (beta 0.1.0)

Listed with file references so they are easy to pick up. Report new ones at
<https://github.com/lumoengineofficial/lumo/issues>.

## Visual

1. **Wireframe/edge overlay on models** — the software renderer strokes every
   triangle edge (`src/Lumo.Runtime/GameView.cs:678`,
   `src/Lumo.Editor/Rendering/SoftwareViewport.cs:641`), so meshes look
   wireframed in both the editor viewport and the game. Style choice for the
   CPU path so silhouettes read clearly; a per-material edge toggle is planned
   and the GL renderer has no edges.
2. **Editor scene camera starts below the terrain** — the orbit camera targets
   the origin at y≈2.5 (`SoftwareViewport.cs:31`, pitch 25°, dist 6) while the
   ground sits at y≈5+, so a fresh project opens under the ground until you
   orbit/pan (or select an entity and press `F`). Fix: auto-frame the scene on
   open.
3. **Loading bar is a fixed 2.2 s timer, not real progress**
   (`GameView.cs:44`, `GameView.cs:446-448`). Assets are actually loaded before
   the window opens, so the bar only animates; if the window is occluded while
   loading it can appear frozen mid-way.

## Editor / play mode

4. **Play window does not always come to the front** — `StartPlay` only calls
   `Show()` (`src/Lumo.Editor/Views/WorkView.cs:246`) and Windows foreground
   rules can leave the game behind the editor. Click its taskbar entry.
5. **Loading-screen hint always says "Left click shoot"**
   (`GameView.cs:482`) even for games with no shooting (e.g. Wildlands).
6. ~~**Game Export requires a dev checkout**~~ **Fixed**: the exporter now falls
   back to an installed/published Lumo.Runtime output when no source checkout is
   present, so installed or zipped editors can export standalone games.
7. **Editor UI is Windows-only** (Avalonia works on Linux/macOS but is only
   tested on Windows); Linux is supported for headless export and tests.

## Performance

8. **Software (CPU) renderer is the default** — roughly 40–60 FPS with a scene
   of a few hundred entities; the automatic GL path depends on the driver
   (see `register_gl_icd.ps1` for the SwiftShader fallback used on machines
   without a working OpenGL driver).

## Not bugs (by design)

- Play mode keeps the editor in edit mode; the game runs in its own window.
- HUD text is positioned top-down (Y grows downward) in normalized
  coordinates — author HUD lines accordingly.
- The first spawned apple is not named `Apple_1`; entity names share one
  sequence counter, so match with `StartsWith("Apple_")`.
