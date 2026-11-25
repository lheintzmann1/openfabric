# OpenFabric

Original 2D factory game (Factorio / Satisfactory inspired). C# .NET 10 + MonoGame (DesktopGL). Data-driven content in `data/*.json`, icons from Google Material Icons.

Roadmap and design: see the stage list in the project plan (Stage 1 skeleton is done; Stage 2 = tier 0-2 gameplay slice is next).

## Build / run / test
```
export MSBuildEnableWorkloadResolver=false   # only needed if `dotnet` fails with a "Workload set ... missing manifests" error
dotnet run --project src/OpenFabric.Game -- --seed 42
dotnet test
```
Controls: WASD move, Shift sprint, wheel zoom, M minimap, F3 debug, Esc quit.

## Layout
- `src/OpenFabric.Core` - headless simulation + data (no MonoGame dependency)
- `src/OpenFabric.Net` - networking (LiteNetLib; empty until Stage 6)
- `src/OpenFabric.Game` - MonoGame client (`Assets/fonts`: Inter, Material Icons - Apache-2.0 / OFL)
- `data/` - items, resources, (later recipes, buildings, tiers)
- `tests/` - xUnit
