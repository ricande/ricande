# My3DEngine — project manual (en-US)

*Reflects the codebase as it stands today. Update this document when behavior or architecture changes.*

**Other language:** [Swedish (sv)](MANUAL.md)

---

## 1. What this project is

**My3DEngine** is an experimental, modular **3D engine in C# (.NET 8)**. It is split into separate libraries (rendering, assets, physics, ECS, runtime) plus a **demo app** that shows a minimal vertical slice: window, OpenGL via Veldrid, a colored cube, basic BEPU physics, and DefaultEcs.

It is **not** a production-ready engine; it is a **starting point** to build on.

**Source:** [github.com/ricande/ricande](https://github.com/ricande/ricande)

---

## 2. Prerequisites

| Requirement | Notes |
|-------------|--------|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | `dotnet --version` should report 8.x |
| Windows (current dev environment) | SDL2/Veldrid native binaries ship via NuGet for several platforms; the demo is primarily verified on Windows |
| OpenGL-capable driver | Veldrid is started with `GraphicsBackend.OpenGL` |

---

## 3. Build and run

### Build the full solution

```powershell
dotnet build .\My3DEngine.sln -c Release
```

### Run the demo

```powershell
dotnet run --project .\My3DEngine.App\My3DEngine.App.csproj -c Release
```

### Exit

Close the window (standard SDL2 window).

---

## 4. Solution layout and responsibilities

| Project | Role |
|---------|------|
| **My3DEngine.App** | Executable entry point: creates `Renderer`, `PhysicsWorld`, ECS `World`, entities, wires `GameLoop`. |
| **My3DEngine.Runtime** | `GameLoop`: fixed timestep for `update`, then `render` each frame. |
| **My3DEngine.Rendering** | Veldrid: SDL2 window, OpenGL `GraphicsDevice`, swapchain, one `CommandList` per frame, a simple graphics pipeline (vertex/index buffers, shaders via SPIR-V compilation), colored cube mesh built into `Renderer`. |
| **My3DEngine.Assets** | AssimpNet (`AssetImporter.ImportScene`) and ImageSharp (`LoadTexture`) → internal types `AssetScene`, `AssetMesh`, `AssetMaterial`, `AssetTexture`. |
| **My3DEngine.Physics** | `PhysicsWorld`: thin layer over BEPUphysics v2 `Simulation`; static box, dynamic box/sphere, `StepSimulation`. |
| **My3DEngine.Ecs** | DefaultEcs: components + `TransformSystem` (physics → position) + `RenderSystem` (first camera + first mesh entity → `Renderer`). |

**Project references (relevant):**

- `App` → Runtime, Rendering, Assets, Physics, Ecs  
- `Ecs` → Rendering, Physics  

---

## 5. Per-frame flow

1. **`GameLoop.Run`** checks `shouldContinue` (window still exists).
2. **`pumpEvents`:** `Renderer.PumpEvents()` — SDL events, mouse state, optional swapchain resize.
3. **`update(dt)`** (fixed `dt`, default 1/60 s):
   - `PhysicsWorld.StepSimulation(dt)`
   - `TransformSystem.Update(dt)` — writes **position** from physics to `TransformComponent` for entities that have both `RigidBodyComponent` and `TransformComponent`.
   - If the left mouse button is held: updates the cube **rotation** (yaw/pitch) from `MouseDelta`.
4. **`render()`:** `RenderSystem.Update` finds the first entity with `CameraComponent` and the first with `TransformComponent` + `MeshComponent`, builds `Camera` + `Transform`, and calls `Renderer.Render`.

`render` is invoked **without** tying it to the physics timestep (future variants could render more often than simulation).

---

## 6. Rendering (`My3DEngine.Rendering`)

### `Renderer`

- Creates the window and device with **`VeldridStartup.CreateWindowAndGraphicsDevice`** using **`GraphicsBackend.OpenGL`** explicitly.
- **Shader:** GLSL `#version 450` with Vulkan-style `layout(set = 0, binding = 0)` for uniform block `Camera` (View, Projection, Model). Compiled via **Veldrid.SPIRV** (`CreateFromSpirv`).
- **Geometry:** A **built-in** vertex/index buffer for a **colored cube** (per-vertex position + color). The ECS `MeshComponent` only marks *what* to draw in this slice — mesh data itself lives in `Renderer`.
- **MVP:** `Camera` (look-at + perspective) and `Transform` (System.Numerics: scale × rotation × translation) are uploaded as three matrices in a std140-style uniform buffer.
- **Depth:** No swapchain depth buffer in the current slice (`SwapchainDepthFormat = null`).

### Public input helpers

- `MouseDelta` — accumulated mouse movement since the last `PumpEvents`.
- `IsLeftMouseDown` — from the latest `InputSnapshot` after `PumpEvents`.

---

## 7. ECS (`My3DEngine.Ecs`)

### Components (current)

| Component | Meaning |
|-----------|---------|
| `NameComponent` | Name (string). |
| `TransformComponent` | `Position`, `Rotation`, `Scale` (System.Numerics). |
| `MeshComponent` | Tag: “this entity should be drawn” (no mesh payload yet). |
| `MaterialComponent` | Tag (shader/material are fixed in `Renderer` today). |
| `CameraComponent` | `Position`, `Target`, FOV, near/far. |
| `RigidBodyComponent` | Holds `PhysicsBody` (adapter to the physics world). |

### Systems

- **`TransformSystem`:** For entities with `RigidBodyComponent` + `TransformComponent`: sets **`Position`** from the physics pose. **Rotation** from physics is **not** overwritten (so mouse rotation in the demo is preserved).
- **`RenderSystem`:** First camera + first `TransformComponent` that also has `MeshComponent` → `Renderer.Render`.

---

## 8. Physics (`My3DEngine.Physics`)

- **`PhysicsWorld`** constructs a BEPU `Simulation` with simple gravity (vector), narrow-phase callbacks, and pose integration.
- **API:** `AddStaticBox`, `AddDynamicBox`, `AddDynamicSphere`, `StepSimulation(dt)`.
- **`PhysicsBody`:** Reads pose via `GetPose()` (position + orientation) for dynamic or static bodies.

In the demo: a large static “ground” under the origin and a dynamic box that falls.

---

## 9. Assets (`My3DEngine.Assets`)

- **`AssetImporter.ImportScene(path)`** — loads a model (Assimp), builds an `AssetScene` with nodes, meshes (positions, normals, UVs when present, triangulated indices), materials with optional diffuse texture path (resolved relative to the model file).
- **`AssetImporter.LoadTexture(path)`** — loads an image into `AssetTexture` (RGBA pixel data).

**Important today:** this data is **not** consumed by `Renderer` or the demo loop. It exists for the next step (GPU mesh / materials from `AssetScene`).

---

## 10. Controls and input (demo)

| Action | Behavior |
|--------|----------|
| **Left mouse button + drag** | Rotates the cube around Y (yaw) and X (pitch), with clamped pitch to avoid gimbal-style flip. |
| Release the button | Rotation stays; position is still updated by physics. |

Sensitivity is set in `Program.cs` (`mouseSensitivity`).

---

## 11. Known limitations (current state)

- A **single built-in** cube in the renderer; no path from `AssetMesh` to GPU yet.
- **One** simple shader pipeline; ECS material/texture components are placeholders.
- **No** depth buffer on the swapchain.
- **`RenderSystem`** always picks the **first** matching camera and **first** mesh entity — no full scene list.
- **Rotation** from physics is not used visually on the cube (intentional for the mouse demo); position is still synced.

---

## 12. Troubleshooting

| Issue | Likely cause |
|-------|----------------|
| `SpirvCompilationException` / shader errors | GLSL must satisfy Veldrid.SPIRV rules (e.g. `set`/`binding` on uniform blocks). |
| Black screen, no crash | Camera/mesh not found; or zero-sized window during resize. |
| Assimp / native DLL | Ensure the correct runtime natives from NuGet exist for your RID when publishing (`dotnet run` usually resolves this automatically). |

---

## 13. Next steps (suggestions)

- Wire `AssetMesh` → `DeviceBuffer` / pipeline per vertex layout.
- Split camera vs object uniforms; multiple draw calls.
- Depth buffer + more entities in `RenderSystem`.
- Sync physics rotation if the body should rotate with the visuals, or separate a “visual offset” from the physics pose.

---

*End of manual.*
