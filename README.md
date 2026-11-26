# OpenFabric

[![CI](https://github.com/lheintzmann1/openfabric/actions/workflows/ci.yml/badge.svg)](https://github.com/lheintzmann1/openfabric/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

An original 2D top-down factory game inspired by Factorio and Satisfactory. Mine, smelt, belt, power, research and
automate your way through **13 tiers** — from a hand drill and stone furnace to arc furnaces, oil, nuclear fission,
fusion and an orbital megaproject. Procedurally generated infinite maps, solo or small-group co-op.

C# .NET 10 + MonoGame (DesktopGL). All art is PNG, one file per item/building/terrain/…, ready to be replaced with your own
(see [Assets/README.md](src/OpenFabric.Game/Assets/README.md); F9 reloads them in game).
All content (items, recipes, buildings, milestones, research) lives in `data/*.json`; see [data/SCHEMA.md](data/SCHEMA.md).

## Run / build / test
```
export MSBuildEnableWorkloadResolver=false   # only needed if `dotnet` fails with "Workload set ... missing manifests"
dotnet run --project src/OpenFabric.Game                 # main menu
dotnet run --project src/OpenFabric.Game -- --seed 42    # straight into a new world
dotnet test
```
Other flags: `--load` (autosave), `--cheat` (unlock everything + items), `--demo` (small pre-built factory),
`--host --port 27015 --name Alice`, `--join 192.168.1.20:27015 --name Bob`.

## Controls
| Key | Action |
|---|---|
| WASD / arrows, Shift | move, sprint |
| LMB on ore (nothing held) | hand-mine (the "pocket drill") |
| Palette (bottom) / 1-9 / `,` `.` | pick a building / category |
| LMB (drag for lines) / RMB | place / cancel or demolish |
| R | rotate (Shift+R reverse) |
| Q | pipette (copy building under cursor) |
| Ctrl+drag, Ctrl+V | copy an area as a blueprint, paste it (R rotates) |
| E | inventory + hand crafting |
| T | milestones (deliver items at the HQ) + research tree |
| F | production / power statistics · G recipe book |
| hover | inspector at the top: machine status, recipe, buffers, power, ore amount, terrain |
| click a building | open its panel (recipes, buffers, power) |
| M / O / F3 / F5 / F9 / Esc | minimap / power overlay / debug / save / reload PNG art / menu |
| Enter (multiplayer) | chat |

## How it plays
* **Tier 0 – Landfall**: mine by hand, smelt in stone furnaces, craft gears/rods, place the free HQ and deliver the first milestones.
* Delivering milestone items at the HQ unlocks buildings and recipes; a tier opens when the previous tier is finished.
* **Research** (labs fed with science packs 1-5) unlocks alternate recipes, belt/drill/craft speed, inventory slots.
* Machines take items from belts/loaders facing into them and push outputs to adjacent belts, chests or machines.
  Fluids flow through pipes/tanks that touch fluid machines (one fluid per network).
* Power: poles link generators and consumers; brown-outs slow machines down; batteries buffer.

## Multiplayer (host-authoritative, 2-8 players)
The host runs the real simulation and orders every command into a tick, broadcasting each tick to clients, which replay
them on their own deterministic copy (state hashes are compared every 2 s; a mismatch triggers a snapshot resync).
Join over UDP (default port 27015, port-forward on the host for internet play). Co-op: shared factory, research, milestones;
each player has an own inventory and crafting queue.

## Layout
- `src/OpenFabric.Core` — headless simulation: world gen, belts, machines, power, fluids, commands, save/load
- `src/OpenFabric.Net` — LiteNetLib host/client + protocol
- `src/OpenFabric.Game` — MonoGame client (screens, immediate-mode UI, windows, renderers)
- `data/` — game content; `tests/` — xUnit (simulation, content consistency, scripted playthrough, loopback multiplayer)

## Downloads & releases
Tagged releases (`v*`) are built by GitHub Actions for **Linux (x64), Windows (x64) and macOS (Apple Silicon + Intel)** as
self-contained archives — unzip and run `OpenFabric` / `OpenFabric.exe`. The macOS builds are unsigned: right-click → Open the
first time (or `xattr -dr com.apple.quarantine OpenFabric-osx-*`). Every push and pull request builds and tests on all three
platforms (`.github/workflows/ci.yml`). To cut a release: `git tag v0.3.0 && git push origin v0.3.0`.

## License
MIT — see [LICENSE](LICENSE). Bundled fonts/icons and dependencies are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
