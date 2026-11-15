# ST14 ZLevels — Architecture (based on CrystallEdge, MIT — see LICENSE.TXT)

## Concept
Fake 3D via a vertical stack of **Map entities**. Each z-level = a full RobustToolbox `Map`.
Levels are ordered by integer `Depth`: 0 = the main map, negative = below, positive = above.
Levels line up by **world tile**: a level is either a planet map (the map entity is its grid, used for every
layer ST14 builds) or a map with its ground as a separate grid on it (a sector's main level).
`ST14SharedZLevelsSystem.TryGetLayerGrid(map)` returns the ground grid of a level either way.
Client rendering stacks eyes/sprites with a fixed per-level screen offset to fake height.

## Planet sector layers (see README.md and docs/ST14-ZLevels-Layers.md)
Every sector of the galaxy war gets five levels from `st14ZLayerSet` `ST14PlanetSector`
(`Resources/Prototypes/_ST14/ZLevels/Layers/layers.yml`, referenced by `st14SectorSettings.layers`):
-2 bug tunnels (FloorCave), -1 trenches (FloorDirt), 0 the sector map, +1 roofs (open air), +2 sky (open air).
Filled levels are tiles only plus an `ST14ZLayerEdge` rock on the inner ring of the sandstorm.
- Server `ST14ZLayerSystem.EnsureLayers(mainMap, set, existing)` builds the missing levels and links the network.
- Server `ST14ZNetworkSaveSystem` saves/loads a whole network: one folder, `z{depth}.yml` per level + `network.txt`.
  Sectors save to `/st14_sectors/{planet}_{cell}/`; the old one-file save is read once and replaced.
- Commands: `znetwork-layers <mapId> [set]`, `znetwork-save <mapId|net> <name>`, `znetwork-load <name>`.
- Weather (`Content.Server/_ST14/Weather`) keeps one sky per sector on its main level and mirrors
  `ST14SectorWeatherComponent` to every level; who is exposed is decided by roofs.
- Day/night sets the same `MapLight` on every level; roofs keep covered tiles dark.

## Core data — Content.Shared/_ST14/ZLevels/Core/Components
- `ST14ZMapComponent` (each z-map, networked, unsaved): `int Depth`, `EntityUid NetworkUid`,
  `EntityUid? MapAbove`, `EntityUid? MapBelow`.
- `ST14ZMapNetworkComponent` (nullspace manager, networked, PVS-forced to every client):
  `Dictionary<int,EntityUid?> ZLevels`, `Dictionary<EntityUid,int> ZLevelByEntity`, sorted list, components registry.
- `ST14ZLevelViewerComponent` (players, networked): `bool LookUp`, `HashSet<EntityUid> Eyes`.
- `ST14ZPhysicsComponent`: vertical physics of a body, `ST14ZLevelHighGroundComponent`: stairs/ladders.

## Systems
- `ST14SharedZLevelsSystem` — partial: `.Maps .Grids .View .Update .Movement .Activation .Constants`.
- Server `ST14ZLevelsSystem` (`.cs .Maps .View`): network editing, PVS eyes per viewer.
- Client `ST14ClientZLevelsSystem`: eye offset, draw depth, cached "is the level below visible" scan.
- Roofs: `ST14SharedZLevelsRoofSystem` / server `.Maps` keep vanilla `RoofComponent` of every level in sync with
  the opaque tiles above it (grids marked `ST14ZLevelRoofComponent`). Roofs keep sky light and weather off tiles.
- Kept gameplay: Audio, Chat (voices from above/below), Climbing, Damage (fall damage), Ghost mover, Pulling,
  Throwing, AutoTilePlacement (ladders), Mapping commands, debug overlay (`showzleveldebug`).
- Removed in the ST14 pass (unused by ST14): flight, CE day cycle, CE weather, zGrid (grid-on-grid networks),
  station z-levels, auto grid gravity, fall-damage modifiers / safe-fall amulet / soft landing, chasm map,
  tile tool, ladder cache, zMap prototypes and their mapping commands, ST14Filter, z navmap control.

## Key API (ST14SharedZLevelsSystem.Maps.cs / .Grids.cs)
- `TryGetMapNetwork(mapUid, out Entity<ST14ZMapNetworkComponent>)`
- `TryMapOffset(ent, offset, out ...)`, `TryMapUp`, `TryMapDown`
- `GetAllMapsAbove(...)`, `GetAllMapsBelow(...)` — nearest-first
- `TryGetZLevelOffset(mapA, mapB, out int)` — O(1) depth diff
- `TryGetLayerGrid(map)`, `GridTileToWorldTile(gridUid, grid, tile)`, `TryGetGridZDepth(gridUid)`
- `GetVisibleZLevelsAbove(ent)`, `HasOpaqueAbove(worldPos, map)` — `.View.cs`

## Constants — ST14SharedZLevelsSystem.Constants.cs
`ZLevelOffset = 0.7f`, `MaxZLevelsBelowRendering = 5`, `MaxZLevelsAboveRendering = 3`, gravity/velocity tuning.

## Events
- `ST14ZLevelMapNetworkUpdatedEvent` (class, on network entity) — maps added/removed (roofs recalculate).
- `ST14MapAddedIntoZNetworkEvent` (ByRefEvent struct `{ Network, Depth }`, on the map).

## Performance rules (ST14 optimisation pass, 2026-11-15)
- **Ground cache.** `ST14ZPhysicsComponent.CachedGroundHeight` is reused while the body stays on the
  same tile of the same grid/map (`CachedTile`, `CachedTileParent`) and `CachedGroundEpoch` matches.
  High ground (stairs) is never reused (`CachedGroundDynamic`). Invalidation: tile changes on a z-map or on a
  grid standing on a z-map (bodies on that level and on the level above), high ground MapInit/anchor/move (that
  tile), high ground shutdown, z-map add/remove/state and network edits (`InvalidateGroundCache`), tile changes
  on any other grid (epoch bump). Anything new that changes what is under a body must invalidate too.
- **Active bodies.** `_activeBodiesSet` is the truth, `_activeBodies` is the order and is compacted
  lazily once per step. `ActiveBodies` exposes the set.
- **Dirtying.** Velocity/LocalPosition changed during substeps are dirtied once per `Update`
  (`FlushPendingDirty`). Setters skip `DirtyField` when the value did not change.
- **Rotation-only moves** are ignored by the z-physics (`OnMoveEvent`).
- **Client.** The viewport's "is the level below visible" scan is cached per map
  (`ST14ClientZLevelsSystem.AnyEmptyOrTransparentTile`, keyed by tile bounds and `TileVersion`).
- Measure with `Content.IntegrationTests/Tests/_ST14/ST14ZLevelPerfTest.cs` (`[Explicit]` tests);
  layers are tested in `ST14ZLayersTest.cs`.
