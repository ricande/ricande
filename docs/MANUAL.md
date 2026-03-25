# My3DEngine — projektmanual

*Version enligt kodbasen idag. Uppdatera detta dokument när beteende eller arkitektur ändras.*

---

## 1. Vad projektet är

**My3DEngine** är en experimentell, modulär **3D-motor i C# (.NET 8)**. Den är uppdelad i separata bibliotek (rendering, assets, fysik, ECS, runtime) och en **demo-app** som visar en minimal vertikal slice: fönster, OpenGL via Veldrid, en färgad kub, enkel BEPU-fysik och DefaultEcs.

Det är **inte** en färdig motor för produktion, utan en **startpunkt** att bygga vidare på.

**Källkod:** [github.com/ricande/ricande](https://github.com/ricande/ricande)

---

## 2. Förutsättningar

| Krav | Kommentar |
|------|-----------|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | `dotnet --version` ska visa 8.x |
| Windows (nuvarande utvecklingsmiljö) | SDL2/Veldrid-native levereras via NuGet för flera plattformar; demo är primärt verifierad på Windows |
| OpenGL-kompatibel drivrutin | Veldrid startar med `GraphicsBackend.OpenGL` |

---

## 3. Bygga och köra

### Bygga hela lösningen

```powershell
dotnet build .\My3DEngine.sln -c Release
```

### Köra demo

```powershell
dotnet run --project .\My3DEngine.App\My3DEngine.App.csproj -c Release
```

### Stänga programmet

Stäng fönstret (standard SDL2-fönster).

---

## 4. Solution-struktur och ansvar

| Projekt | Roll |
|---------|------|
| **My3DEngine.App** | Körbar entry point: skapar `Renderer`, `PhysicsWorld`, ECS-`World`, entiteter, kopplar `GameLoop`. |
| **My3DEngine.Runtime** | `GameLoop`: fast tidssteg för `update`, sedan `render` per bildruta. |
| **My3DEngine.Rendering** | Veldrid: SDL2-fönster, OpenGL-`GraphicsDevice`, swapchain, en `CommandList` per frame, en enkel grafikpipeline (vertex/index buffers, shader via SPIR-V-kompilering), färgad kub-mesh inbyggd i `Renderer`. |
| **My3DEngine.Assets** | AssimpNet (`AssetImporter.ImportScene`) och ImageSharp (`LoadTexture`) → interna typer `AssetScene`, `AssetMesh`, `AssetMaterial`, `AssetTexture`. |
| **My3DEngine.Physics** | `PhysicsWorld`: tunt lager över BEPUphysics v2 `Simulation`; statisk låda, dynamisk låda/kula, `StepSimulation`. |
| **My3DEngine.Ecs** | DefaultEcs: komponenter + `TransformSystem` (fysik → position) + `RenderSystem` (första kamera + första mesh-entity → `Renderer`). |

**Projektreferenser (relevanta):**

- `App` → Runtime, Rendering, Assets, Physics, Ecs  
- `Ecs` → Rendering, Physics  

---

## 5. Körflöde (en frame)

1. **`GameLoop.Run`** kollar `shouldContinue` (fönster finns).
2. **`pumpEvents`:** `Renderer.PumpEvents()` — SDL-events, musläge, ev. swapchain-resize.
3. **`update(dt)`** (fast `dt`, standard 1/60 s):
   - `PhysicsWorld.StepSimulation(dt)`
   - `TransformSystem.Update(dt)` — skriver **position** från fysik till `TransformComponent` för entiteter med både `RigidBodyComponent` och `TransformComponent`.
   - Om vänster musknapp är nedtryckt: uppdateras kubens **rotation** (yaw/pitch) från `MouseDelta`.
4. **`render()`:** `RenderSystem.Update` hittar första entitet med `CameraComponent` och första med `TransformComponent` + `MeshComponent`, bygger `Camera` + `Transform` och anropar `Renderer.Render`.

Render anropas **utan** fast fysik-dt (kan köras oftare än simulering i framtida varianter).

---

## 6. Rendering (`My3DEngine.Rendering`)

### `Renderer`

- Skapar fönster och enhet med **`VeldridStartup.CreateWindowAndGraphicsDevice`** med explicit **`GraphicsBackend.OpenGL`**.
- **Shader:** GLSL `#version 450` med Vulkan-liknande `layout(set = 0, binding = 0)` för uniform block `Camera` (View, Projection, Model). Kompileras via **Veldrid.SPIRV** (`CreateFromSpirv`).
- **Geometri:** En **inbyggd** vertex/index-buffer för en **färgad kub** (vertex position + färg per hörn). ECS `MeshComponent` styr bara *vad* som ritas i denna slice — själva mesh-datan ägs av `Renderer`.
- **MVP:** `Camera` (look-at + perspektiv) och `Transform` (System.Numerics: scale × rotation × translation) skickas som tre matriser i en std140-liknande uniform buffer.
- **Djup:** Ingen depth buffer i swapchain i nuvarande slice (`SwapchainDepthFormat = null`).

### Publika signaler för input

- `MouseDelta` — ackumulerad musrörelse sedan senaste `PumpEvents`.
- `IsLeftMouseDown` — från senaste `InputSnapshot` efter `PumpEvents`.

---

## 7. ECS (`My3DEngine.Ecs`)

### Komponenter (nu)

| Komponent | Innebörd |
|-----------|----------|
| `NameComponent` | Namn (sträng). |
| `TransformComponent` | `Position`, `Rotation`, `Scale` (System.Numerics). |
| `MeshComponent` | Markerare: “denna entity ska ritas” (ingen egen mesh-data än). |
| `MaterialComponent` | Markerare (shader/material är fast i `Renderer` idag). |
| `CameraComponent` | `Position`, `Target`, FOV, near/far. |
| `RigidBodyComponent` | Håller `PhysicsBody` (adapter mot fysikvärlden). |

### System

- **`TransformSystem`:** För entiteter med `RigidBodyComponent` + `TransformComponent`: sätter **`Position`** från fysikens pose. **Rotation** från fysik skrivs **inte** över (så musrotation i demo behålls).
- **`RenderSystem`:** Första kamera + första `TransformComponent` som också har `MeshComponent` → `Renderer.Render`.

---

## 8. Fysik (`My3DEngine.Physics`)

- **`PhysicsWorld`** skapar BEPU `Simulation` med enkel gravitation (vektor), narrow phase callbacks och pose-integrator.
- **API:** `AddStaticBox`, `AddDynamicBox`, `AddDynamicSphere`, `StepSimulation(dt)`.
- **`PhysicsBody`:** Läser pose via `GetPose()` (position + orientation) från dynamisk eller statisk kropp.

I demot: stor statisk “mark” under origo och en dynamisk låda som faller.

---

## 9. Assets (`My3DEngine.Assets`)

- **`AssetImporter.ImportScene(path)`** — läser modell (Assimp), bygger `AssetScene` med noder, meshes (positioner, normaler, UV om finns, triangulerade index), material med valfri diffuse-texturväg (resolverad relativt modellfil).
- **`AssetImporter.LoadTexture(path)`** — laddar bild till `AssetTexture` (RGBA-pixeldata).

**Viktigt idag:** dessa data används **inte** av `Renderer` eller demo-loopen. De finns för nästa steg (GPU-mesh / material från `AssetScene`).

---

## 10. Styrning och input (demo)

| Handling | Beteende |
|----------|----------|
| **Vänster musknapp + dra** | Roterar kuben runt Y (yaw) och X (pitch), med begränsad pitch för att undvika “flip”. |
| Släpp knappen | Rotationen stannar kvar; position uppdateras fortfarande av fysik. |

Känslighet styrs i `Program.cs` (`mouseSensitivity`).

---

## 11. Kända begränsningar (som läget är nu)

- En **endast inbyggd** kub i renderern; ingen koppling från `AssetMesh` till GPU.
- **En** enkel shader-pipeline; material/texture i ECS är platshållare.
- **Ingen** depth buffer på swapchain.
- **`RenderSystem`** väljer alltid **första** matchande kamera och **första** mesh-entity — ingen full scenlista.
- **Rotation** från fysik används inte visuellt på kuben (medvetet för musdemo); position synkas däremot.

---

## 12. Felsökning

| Problem | Möjlig orsak |
|---------|----------------|
| `SpirvCompilationException` / shaderfel | GLSL måste följa Veldrid.SPIRV-krav (t.ex. `set`/`binding` på uniform blocks). |
| Svart skärm men inget krasch | Kamera/mesh hittas inte; eller fönsterstorlek 0 vid resize. |
| Assimp / native DLL | Kontrollera att rätt runtime-native från NuGet-packarna finns för din RID vid publish (för `dotnet run` brukar det lösa sig automatiskt). |

---

## 13. Vidare utveckling (förslag)

- Koppla `AssetMesh` → `DeviceBuffer` / pipeline per layout.
- Dela upp kamera- respektive objekt-uniforms; flera draw calls.
- Depth buffer + fler entiteter i `RenderSystem`.
- Synka fysikrotation om du vill att kroppen ska rotera med grafiken, eller separera “visual offset” från fysikpose.

---

*Slut på manual.*
