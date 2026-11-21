/*
 * Starborne Troopers z-level layers. Part of the z-levels based on CrystallEdge (MIT License),
 * see LICENSE.TXT in the ZLevels folder.
 */

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server._ST14.ZLevels.Core;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._ST14.ZLevels.Layers;

/// <summary>
/// Saves and loads a whole z-network: every level goes to its own map file in one folder, and network.txt lists
/// the depth of each file. Loading reads the list, loads every map and links them into a new network at the same
/// depths. The z-components are not saved (they belong to the network), the list rebuilds them.
/// <code>
/// /folder/network.txt   lines of "depth file", e.g. "-2 z-2.yml"
/// /folder/z-2.yml ... /folder/z2.yml
/// </code>
/// </summary>
public sealed partial class ST14ZNetworkSaveSystem : EntitySystem
{
    public const string ManifestName = "network.txt";

    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private ST14ZLayerSystem _layers = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;

    public static string FileName(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}.yml";
    }

    public bool HasSave(ResPath folder)
    {
        return _resources.UserData.Exists(folder / ManifestName);
    }

    /// <summary>
    /// Writes every level of the network <paramref name="anyLevel"/> is in (or just that map when it has none) to
    /// <paramref name="folder"/> in the user data. False if any level failed; the manifest lists only those saved
    /// </summary>
    public bool TrySaveNetwork(EntityUid anyLevel, ResPath folder, SerializationOptions? options = null)
    {
        var levels = _layers.GetLevels(anyLevel);
        _resources.UserData.CreateDir(folder);

        var manifest = new StringBuilder();
        manifest.AppendLine("# ST14 z-network save: depth file");
        var ok = true;

        foreach (var (depth, map) in levels.OrderBy(kv => kv.Key))
        {
            if (TerminatingOrDeleted(map))
            {
                ok = false;
                continue;
            }

            var file = FileName(depth);
            bool saved;
            try
            {
                saved = options is { } opts
                    ? _mapLoader.TrySaveMap(map, folder / file, opts)
                    : _mapLoader.TrySaveMap(map, folder / file);
            }
            catch (Exception e)
            {
                Log.Error($"Could not save level {depth} of {ToPrettyString(map)}: {e}");
                saved = false;
            }

            if (saved)
                manifest.AppendLine($"{depth.ToString(CultureInfo.InvariantCulture)} {file}");
            else
                ok = false;
        }

        _resources.UserData.WriteAllText(folder / ManifestName, manifest.ToString());
        return ok;
    }

    /// <summary>
    /// Loads a network saved by <see cref="TrySaveNetwork"/> and links its levels at their depths. On failure every
    /// map loaded so far is deleted again
    /// </summary>
    public bool TryLoadNetwork(
        ResPath folder,
        [NotNullWhen(true)] out Dictionary<int, Entity<MapComponent>>? levels,
        DeserializationOptions? options = null)
    {
        levels = null;
        if (!_resources.UserData.TryReadAllText(folder / ManifestName, out var text))
            return false;

        var loaded = new Dictionary<int, Entity<MapComponent>>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth))
            {
                Log.Error($"Bad line in {folder / ManifestName}: {line}");
                continue;
            }

            if (!_mapLoader.TryLoadMap(folder / parts[1], out var map, out _, options))
            {
                Log.Error($"Level {depth} of the z-network save {folder} does not load");
                foreach (var done in loaded.Values)
                {
                    QueueDel(done);
                }

                return false;
            }

            loaded[depth] = map.Value;
        }

        if (!loaded.ContainsKey(0))
        {
            Log.Error($"The z-network save {folder} has no level 0");
            foreach (var done in loaded.Values)
            {
                QueueDel(done);
            }

            return false;
        }

        if (loaded.Count > 1)
        {
            var network = _zLevels.CreateMapNetwork();
            _zLevels.TryAddMapsIntoNetwork(network, loaded.ToDictionary(kv => kv.Value.Owner, kv => kv.Key));
        }

        levels = loaded;
        return true;
    }
}
