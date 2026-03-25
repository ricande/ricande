# My3DEngine (net8.0)

## Om detta repo

Det här är **ricande** på GitHub: en liten, modulär **3D-motor i C# (.NET 8)** som byggs vidare stegvis. Målet är tydlig separation mellan runtime, rendering, assets, fysik och ECS — inte en färdig spelmotor, utan en **fungerande bas** att utöka.

**Remote:** [github.com/ricande/ricande](https://github.com/ricande/ricande)

**Manual (aktuellt läge):** [Svenska — docs/MANUAL.md](docs/MANUAL.md) · [English (en-US) — docs/MANUAL.en-US.md](docs/MANUAL.en-US.md)

### Teknikstack (nu)

Minimal, modulär start på en egen 3D-motor i C# med:

- Rendering: OpenGL via Veldrid
- Matematik: System.Numerics
- Assets: AssimpNet + ImageSharp
- Physics: BEPUphysics v2
- ECS: DefaultEcs

## Solution-struktur

- `My3DEngine.App`: körbar demo som kopplar ihop allt
- `My3DEngine.Runtime`: enkel game loop (`update` + `render`)
- `My3DEngine.Rendering`: Veldrid/OpenGL bootstrap + minimal shader pipeline + rendering av färgad cube
- `My3DEngine.Assets`: importer (Assimp) + texturladdning (ImageSharp) till interna asset-typer
- `My3DEngine.Physics`: tunt wrapper-lager runt BEPU `Simulation`
- `My3DEngine.Ecs`: DefaultEcs-komponenter + `TransformSystem` (physics→ecs) + `RenderSystem` (ecs→renderer)

## NuGet-paket

- `My3DEngine.Rendering`
  - `Veldrid` (4.9.0)
  - `Veldrid.SDL2` (4.9.0)
  - `Veldrid.StartupUtilities` (4.9.0)
  - `Veldrid.SPIRV` (1.0.15)
- `My3DEngine.Assets`
  - `AssimpNet` (4.1.0)
  - `SixLabors.ImageSharp` (3.1.12)
- `My3DEngine.Physics`
  - `BepuPhysics` (2.4.0)
- `My3DEngine.Ecs`
  - `DefaultEcs` (0.17.2)

## Bygg

```powershell
dotnet build .\My3DEngine.sln -c Release
```

## Kör demo

```powershell
dotnet run --project .\My3DEngine.App\My3DEngine.App.csproj -c Release
```

## Notes

- Demo-appen:
  - öppnar ett fönster (SDL2 via Veldrid)
  - initierar OpenGL-backend
  - skapar ett ECS-world med kamera + cube-entity
  - initierar BEPU med statisk mark + dynamisk box
  - stegar fysik och synkar pose tillbaka till `TransformComponent`
  - renderar en färgad cube med MVP (System.Numerics)

## Nuvarande begränsningar (minsta fungerande version)

- Rendering är “fast”: en inbyggd cube-mesh och en enkel shader; material/texture är inte inkopplat än.
- Asset-import finns men är inte kopplat till renderern ännu (nästa steg: skapa GPU-mesh/texture från `AssetScene`).
- `RenderSystem` renderar första kameran + första renderbara entity (räcker för demo).
