using System.Numerics;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Keeps what the server sent for the battle map: the state of the side and the ground of the sectors seen so far
/// </summary>
public sealed partial class ST14BattleMapClientSystem : EntitySystem
{
    private static readonly Rgba32 GroundColor = new(150, 118, 78, 255);
    private static readonly Rgba32 ObstacleColor = new(44, 34, 28, 255);

    private static readonly TextureLoadParameters Smooth = new()
    {
        SampleParameters = new TextureSampleParameters { Filter = true },
        Srgb = true,
    };

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<int, Texture> _terrain = new();
    private readonly HashSet<int> _asked = new();
    private int _round = -1;
    private ST14GalaxyFaction? _wantedSide;

    public ST14BattleMapStateEvent? State { get; private set; }

    /// <summary>
    /// What moves on the map, null until the server sent it for this round
    /// </summary>
    public ST14BattleMapLiveEvent? Live { get; private set; }

    /// <summary>
    /// The squads of the side, kept whether the map is open or not
    /// </summary>
    public ST14SquadStateEvent? Squads { get; private set; }

    public event Action? SquadsChanged;

    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14BattleMapStateEvent>(OnState);
        SubscribeNetworkEvent<ST14BattleMapTerrainEvent>(OnTerrain);
        SubscribeNetworkEvent<ST14BattleMapLiveEvent>(OnLive);
        SubscribeNetworkEvent<ST14SquadStateEvent>(OnSquads);
    }

    public void Request(ST14GalaxyFaction? side = null)
    {
        _wantedSide = side ?? _wantedSide;
        RaiseNetworkEvent(new ST14BattleMapRequestEvent(_wantedSide));
    }

    public void Closed()
    {
        RaiseNetworkEvent(new ST14BattleMapClosedEvent());
    }

    public void RequestSquads()
    {
        RaiseNetworkEvent(new ST14SquadRequestEvent());
    }

    public void AnswerInvite(bool accept)
    {
        RaiseNetworkEvent(new ST14SquadInviteAnswerEvent(accept));
    }

    public void CreateSquad()
    {
        RaiseNetworkEvent(new ST14SquadCreateEvent());
    }

    public void JoinSquad(int id)
    {
        RaiseNetworkEvent(new ST14SquadJoinEvent(id));
    }

    public void LeaveSquad()
    {
        RaiseNetworkEvent(new ST14SquadLeaveEvent());
    }

    public void AddMark(ST14GalaxyFaction side, ST14BattleMark mark)
    {
        RaiseNetworkEvent(new ST14BattleMarkAddEvent(side, mark));
    }

    /// <summary>
    /// Removes one mark, or all of them with -1
    /// </summary>
    public void RemoveMark(ST14GalaxyFaction side, int index)
    {
        RaiseNetworkEvent(new ST14BattleMarkRemoveEvent(side, index));
    }

    /// <summary>
    /// The ground of a sector, asked from the server the first time it is shown
    /// </summary>
    public Texture? Terrain(int cell)
    {
        if (_terrain.TryGetValue(cell, out var texture))
            return texture;

        if (_asked.Add(cell))
            RaiseNetworkEvent(new ST14BattleMapTerrainRequestEvent(cell));

        return null;
    }

    /// <summary>
    /// Where the player stands in the tiles of their sector, null without a body on a grid
    /// </summary>
    public Vector2? OwnPosition()
    {
        if (_player.LocalEntity is not { } body || Transform(body).GridUid is not { } grid)
            return null;

        return Vector2.Transform(_transform.GetWorldPosition(body), _transform.GetInvWorldMatrix(grid));
    }

    /// <summary>
    /// The map the body is on: the sector of the player changes with it
    /// </summary>
    public EntityUid? OwnMap()
    {
        return _player.LocalEntity is { } body ? Transform(body).MapUid : null;
    }

    private void OnState(ST14BattleMapStateEvent ev)
    {
        if (ev.Round != _round)
        {
            _round = ev.Round;
            _wantedSide = null;
            _asked.Clear();
            foreach (var texture in _terrain.Values)
            {
                (texture as OwnedTexture)?.Dispose();
            }

            _terrain.Clear();
            Live = null;
        }

        State = ev;
        Changed?.Invoke();
    }

    private void OnSquads(ST14SquadStateEvent ev)
    {
        Squads = ev;
        SquadsChanged?.Invoke();
    }

    private void OnLive(ST14BattleMapLiveEvent ev)
    {
        if (ev.Round != _round)
            return;

        Live = ev;
        Changed?.Invoke();
    }

    private void OnTerrain(ST14BattleMapTerrainEvent ev)
    {
        const int size = ST14BattleMapTerrainEvent.Blocks;
        if (ev.Round != _round || ev.Data.Length != size * size)
            return;

        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var block = ev.Data[y * size + x];
                var middle = (new Vector2(x, y) + new Vector2(0.5f)) * ST14BattleMapTerrainEvent.BlockTiles;
                if (block == ST14BattleMapTerrainEvent.Empty || !ST14SectorGeometry.Contains(middle))
                    continue;

                // rows of an image go down, rows of a map go up
                image[x, size - 1 - y] = block == ST14BattleMapTerrainEvent.Obstacle ? ObstacleColor : GroundColor;
            }
        }

        _terrain[ev.Cell] = Texture.LoadFromImage(image, $"st14-battle-map-{ev.Cell}", Smooth);
        Changed?.Invoke();
    }
}
