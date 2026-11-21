/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._ST14.ZLevels.Layers;
using Content.Server.Administration;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._ST14.ZLevels.Mapping.Commands;

/// <summary>
/// ST14: saves every level of a z-network (initialized or not) to /ZNetworkSaves/name/ in the server data,
/// one map file per level plus network.txt with the depths. znetwork-load reads it back.
/// </summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class ST14SaveZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ST14ZNetworkSaveSystem _save = default!;

    public override string Command => "znetwork-save";
    public override string Description => "Save every level of a z-network to /ZNetworkSaves/<name>/ (znetwork-load reads it back)";
    public override string Help => "znetwork-save <map id of any level | network net entity> <name>";

    public static ResPath Folder(string name)
    {
        return new ResPath("/ZNetworkSaves") / name;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = new List<CompletionOption>();
            var query = _entities.EntityQueryEnumerator<ST14ZMapComponent, MapComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out _, out var map, out var meta))
            {
                options.Add(new CompletionOption(map.MapId.ToString(), meta.EntityName));
            }

            return CompletionResult.FromHintOptions(options, "map id of any level");
        }

        if (args.Length == 2)
            return CompletionResult.FromHint("save name (for example: `Sector1`)");

        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryResolveLevel(args[0], out var level))
        {
            shell.WriteError($"No map or z-network {args[0]}");
            return;
        }

        var folder = Folder(args[1]);
        if (_save.TrySaveNetwork(level, folder))
            shell.WriteLine($"Saved the z-network of {_entities.ToPrettyString(level)} to {folder}");
        else
            shell.WriteError($"Some levels failed to save to {folder}, see the server log");
    }

    private bool TryResolveLevel(string arg, out EntityUid level)
    {
        level = default;
        var maps = _entities.System<SharedMapSystem>();

        if (int.TryParse(arg, out var id) && maps.TryGetMap(new MapId(id), out var map))
        {
            level = map.Value;
            return true;
        }

        if (NetEntity.TryParse(arg, out var net) &&
            _entities.TryGetEntity(net, out var uid) &&
            _entities.TryGetComponent<ST14ZMapNetworkComponent>(uid, out var network))
        {
            foreach (var (_, map0) in network.ZLevels)
            {
                if (map0 is { } found)
                {
                    level = found;
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// ST14: loads a z-network saved by znetwork-save and links its levels at their depths.
/// </summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class ST14LoadZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private ST14ZNetworkSaveSystem _save = default!;

    public override string Command => "znetwork-load";
    public override string Description => "Load a z-network saved with znetwork-save from /ZNetworkSaves/<name>/";
    public override string Help => "znetwork-load <name>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var folder = ST14SaveZNetworkCommand.Folder(args[0]);
        if (!_save.HasSave(folder))
        {
            shell.WriteError($"No z-network save in {folder}");
            return;
        }

        if (!_save.TryLoadNetwork(folder, out var levels))
        {
            shell.WriteError($"The z-network save {folder} does not load, see the server log");
            return;
        }

        foreach (var (depth, map) in levels)
        {
            shell.WriteLine($"Level {depth}: map {map.Comp.MapId}");
        }
    }
}

/// <summary>
/// ST14: gives a map the levels of a layer set (by default the five levels of a planet sector).
/// </summary>
[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class ST14LayersZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ST14ZLayerSystem _layers = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    public override string Command => "znetwork-layers";
    public override string Description => "Build the levels of a layer set around a map (the map becomes level 0)";
    public override string Help => "znetwork-layers <map id> [layer set, default ST14PlanetSector]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var id) || !_maps.TryGetMap(new MapId(id), out var map))
        {
            shell.WriteError(Help);
            return;
        }

        var set = args.Length > 1 ? args[1] : "ST14PlanetSector";
        foreach (var (depth, level) in _layers.EnsureLayers(map.Value, set))
        {
            shell.WriteLine($"Level {depth}: {_entities.ToPrettyString(level)}");
        }
    }
}
