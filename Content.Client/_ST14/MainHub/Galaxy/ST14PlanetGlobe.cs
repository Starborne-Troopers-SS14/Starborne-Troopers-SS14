using System.Linq;
using System.Numerics;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Weather;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Planet drawn as a lit sphere that the mouse turns around. On a war planet the hex sectors lie over the surface
/// and can be hovered and picked
/// </summary>
public sealed class ST14PlanetGlobe : Control
{
    private const int LonSegments = 48;
    private const int LatSegments = 24;
    private const float DragThreshold = 4f;
    private const int MarkSegments = 24;

    // until the star is known the light comes from the upper left and a bit from the viewer
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.55f, 0.45f, 0.7f));

    private static readonly Vector3[] MeshPoints;
    private static readonly Vector2[] MeshUv;
    private static readonly ushort[] MeshIndices;

    private readonly IConfigurationManager _cfg = IoCManager.Resolve<IConfigurationManager>();
    private readonly ST14GalaxyTextures _textures;

    /// <summary>
    /// The direction to the star in view space: the planet turns under it, the half facing it has the day. The same
    /// star the sector rounds take their daylight from
    /// </summary>
    private Vector3 _light = Light;
    private readonly ST14VertexBuffer _hatch = new();
    private readonly ST14VertexBuffer _overlay = new();
    private readonly ST14VertexBuffer _glow = new();
    private readonly ST14VertexBuffer _marks = new();
    private readonly DrawVertexUV2DColor[] _projected = new DrawVertexUV2DColor[MeshPoints.Length];
    private readonly float[] _depth = new float[MeshPoints.Length];
    private readonly ushort[] _visible = new ushort[MeshIndices.Length];

    private ST14GalaxyPlanetPrototype? _planet;
    private ST14PlanetWar? _war;
    private Texture? _texture;

    private Quaternion _rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f);
    private Vector2 _spin = new(0.12f, 0);
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;
    private Vector2 _lastPos;
    private float _idle;
    private float _time;
    private int? _hovered;
    private int? _selected;
    private float _introFrom;
    private float _introTo;
    private float _intro = 1;
    private Quaternion _faceFrom;
    private Quaternion _faceTo;
    private float _faceProgress = 1;

    /// <summary>
    /// Radius of the planet relative to the half of the smaller side of the control
    /// </summary>
    public float Zoom = 0.86f;

    public float MinZoom = 0.86f;
    public float MaxZoom = 0.86f;

    /// <summary>
    /// Marks of the commands on the surface, each with its side and its index on the server
    /// </summary>
    public IReadOnlyList<(ST14GalaxyFaction Side, int Index, ST14WarMark Mark)> Marks =
        Array.Empty<(ST14GalaxyFaction, int, ST14WarMark)>();

    public ST14GalaxyFaction ToolSide = ST14GalaxyFaction.Federation;

    /// <summary>
    /// The mark the command is placing. While a tool is in hand the planet stands still and clicks do not pick sectors
    /// </summary>
    public ST14WarMarkKind? Tool;

    public bool Eraser;

    /// <summary>
    /// The planet stops the clicks that land on it, so the right button has to be passed on to the screen
    /// </summary>
    public event Action? OnRightClicked;

    public event Action<ST14WarMark>? OnMarkPlaced;
    public event Action<ST14GalaxyFaction, int>? OnMarkErased;

    private bool Drawing => Tool != null || Eraser;

    public event Action<int?>? OnCellSelected;
    public event Action<int?>? OnCellHovered;

    /// <summary>
    /// The wheel turned on to zoom in once the planet was as close as it gets
    /// </summary>
    public event Action? OnZoomPastMax;

    static ST14PlanetGlobe()
    {
        var points = new List<Vector3>();
        var uv = new List<Vector2>();
        for (var y = 0; y <= LatSegments; y++)
        {
            var v = (float) y / LatSegments;
            var lat = MathF.PI / 2 - v * MathF.PI;
            for (var x = 0; x <= LonSegments; x++)
            {
                var u = (float) x / LonSegments;
                points.Add(ST14Sphere.Point(u * MathF.Tau, lat));
                uv.Add(new Vector2(u, v));
            }
        }

        var indices = new List<ushort>();
        for (var y = 0; y < LatSegments; y++)
        {
            for (var x = 0; x < LonSegments; x++)
            {
                var a = (ushort) (y * (LonSegments + 1) + x);
                var b = (ushort) (a + 1);
                var c = (ushort) (a + LonSegments + 1);
                var d = (ushort) (c + 1);
                indices.AddRange(new[] { a, c, b, b, c, d });
            }
        }

        MeshPoints = points.ToArray();
        MeshUv = uv.ToArray();
        MeshIndices = indices.ToArray();
    }

    public ST14PlanetGlobe(ST14GalaxyTextures textures)
    {
        _textures = textures;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = false;
    }

    public ST14PlanetWar? War => _war;

    public int? SelectedCell
    {
        get => _selected;
        set => _selected = value;
    }

    public int? HoveredCell => _hovered;

    /// <summary>
    /// Sector the High Command sent the troopers to, it carries the landing marker
    /// </summary>
    public int? TargetCell;

    /// <summary>
    /// Sector the attacker gathers in for the battle over the target
    /// </summary>
    public int? StagingCell;

    public Color StagingColor = ST14HubStyle.GoldLight;

    /// <summary>
    /// The cells the current decision is about: they light up and the rest of the planet sinks into the dark
    /// </summary>
    public HashSet<int>? Focus;

    public Color FocusColor = ST14HubStyle.GoldLight;

    /// <summary>
    /// Whether the planet slowly turns by itself when left alone. On a table with a picked sector it stays put
    /// </summary>
    public bool AutoSpin = true;

    /// <summary>
    /// Captions that float over cells. Need the resource cache for their font
    /// </summary>
    public readonly List<(int Cell, string Text, Color Color)> Tags = new();

    public IResourceCache? TagCache;

    /// <summary>
    /// The sign drawn in the target instead of the marker that stands over it
    /// </summary>
    public Texture? TargetIcon;

    public Color TargetIconColor = Color.White;

    /// <summary>
    /// Whether the target carries the marker that stands over the planet. The table marks it with its own sign
    /// </summary>
    public bool TargetMarker = true;

    /// <summary>
    /// The caption of the cell under the cursor
    /// </summary>
    public Func<int, string?>? HoverText;

    public void SetPlanet(ST14GalaxyPlanetPrototype planet, ST14PlanetWar? war)
    {
        if (_planet != planet)
        {
            _rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f) *
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, planet.Seed * 1.3f);
            _spin = new Vector2(0.12f, 0);
        }

        _planet = planet;
        _war = war;
        _texture = _textures.Surface(planet);
        _selected = null;
        _hovered = null;
        BuildRegionEdges();

        // a war planet turns its front towards the viewer
        if (war != null)
            FaceCell(Front(war));
    }

    /// <summary>
    /// The planet grows from a small disc to its size, picking up the dive of the galaxy map
    /// </summary>
    public void Intro(float from, float to)
    {
        _introFrom = from;
        _introTo = to;
        _intro = 0;
        Zoom = from;
    }

    /// <summary>
    /// Turns the planet so the cell looks at the viewer
    /// </summary>
    public void FaceCell(int cell)
    {
        if (_war == null)
            return;

        _rotation = RotationFacing(cell);
        _spin = Vector2.Zero;
        _faceProgress = 1;
    }

    /// <summary>
    /// Turns the planet so the cell looks at the viewer, smoothly
    /// </summary>
    public void FaceCellSmooth(int cell)
    {
        if (_war == null)
            return;

        _faceFrom = _rotation;
        _faceTo = RotationFacing(cell);
        _faceProgress = 0;
        _spin = Vector2.Zero;
    }

    private Quaternion RotationFacing(int cell)
    {
        var target = _war!.Grid.Centers[cell];
        // yaw the cell to the front, then tilt it a little towards the light
        var yaw = MathF.Atan2(target.X, target.Z);
        var pitch = MathF.Asin(Math.Clamp(target.Y, -1, 1));
        return Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * 0.8f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
    }

    private static int Front(ST14PlanetWar war)
    {
        var sum = Vector3.Zero;
        for (var i = 0; i < war.Cells.Length; i++)
        {
            if (war.Cells[i].Front)
                sum += war.Grid.Centers[i];
        }

        return war.Grid.Nearest(sum.LengthSquared() > 0.0001f ? Vector3.Normalize(sum) : Vector3.UnitZ);
    }

    /// <summary>
    /// The sides of the cells that run along the border of a region: finding the cell behind a side takes a look
    /// through its neighbours, so it is done once
    /// </summary>
    private void BuildRegionEdges()
    {
        _regionEdges.Clear();
        if (_war == null)
            return;

        var grid = _war.Grid;
        for (var i = 0; i < grid.Count; i++)
        {
            var corners = grid.Corners[i];
            for (var c = 0; c < corners.Length; c++)
            {
                var from = corners[c];
                var to = corners[(c + 1) % corners.Length];
                var middle = Vector3.Normalize(from + to);
                var behind = grid.Neighbours[i].OrderByDescending(n => Vector3.Dot(grid.Centers[n], middle)).First();
                // every border is seen from both sides, one drawing is enough
                if (behind > i && _war.Cells[behind].Region != _war.Cells[i].Region)
                    _regionEdges.Add((from, to));
            }
        }
    }

    private float Radius => MathF.Min(PixelSize.X, PixelSize.Y) / 2 * Zoom;

    private Vector2 Center => PixelSize / 2;

    private Vector2 Project(Vector3 view)
    {
        return Center + new Vector2(view.X, -view.Y) * Radius;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        var dt = args.DeltaSeconds;
        _time += dt;

        if (_intro < 1)
        {
            _intro = MathF.Min(1, _intro + dt / 0.6f);
            var ease = 1 - MathF.Pow(1 - _intro, 3);
            Zoom = _introFrom + (_introTo - _introFrom) * ease;
        }

        if (_faceProgress < 1)
        {
            _faceProgress = MathF.Min(1, _faceProgress + dt / 0.55f);
            var eased = 1 - MathF.Pow(1 - _faceProgress, 3);
            _rotation = Quaternion.Normalize(Quaternion.Slerp(_faceFrom, _faceTo, eased));
            return;
        }

        if (_dragging || Drawing)
            return;

        // the push of the last drag runs out, then the planet slowly turns on its own axis
        _idle += dt;
        _spin *= MathF.Exp(-dt * 2.5f);
        if (AutoSpin && _idle > 2.5f)
            _spin.X += (0.1f - _spin.X) * MathF.Min(1, dt * 0.5f);

        if (_spin.LengthSquared() > 0.00001f)
            Turn(_spin.X * dt, _spin.Y * dt);
    }

    private void Turn(float yaw, float pitch)
    {
        var delta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
        _rotation = Quaternion.Normalize(delta * _rotation);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            OnRightClicked?.Invoke();
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _pressed = true;
        _dragging = false;
        _pressPos = args.RelativePixelPosition;
        _lastPos = _pressPos;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_pressed)
            return;

        _pressed = false;
        if (Drawing)
        {
            ReleaseTool(args.RelativePixelPosition);
            _dragging = false;
            args.Handle();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            _idle = 0;
            return;
        }

        if (_war == null)
            return;

        var cell = Pick(args.RelativePixelPosition);
        if (cell == null)
            return;

        _selected = cell;
        OnCellSelected?.Invoke(cell);
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pos = args.RelativePixelPosition;
        if (_pressed)
        {
            if (!_dragging && (pos - _pressPos).Length() > DragThreshold)
                _dragging = true;

            if (_dragging && !Drawing)
            {
                _faceProgress = 1;
                var delta = (pos - _lastPos) / MathF.Max(Radius, 1);
                Turn(delta.X, delta.Y);
                // remember the speed for the push after release
                _spin = Vector2.Lerp(_spin, delta * 30f, 0.5f);
                _idle = 0;
            }

            _lastPos = pos;
            return;
        }

        if (_war == null)
            return;

        var cell = Pick(pos);
        if (cell == _hovered)
            return;

        _hovered = cell;
        OnCellHovered?.Invoke(cell);
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        if (_hovered == null)
            return;

        _hovered = null;
        OnCellHovered?.Invoke(null);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (MaxZoom <= MinZoom)
            return;

        if (args.Delta.Y > 0 && Zoom >= MaxZoom - 0.001f)
            OnZoomPastMax?.Invoke();

        Zoom = Math.Clamp(Zoom * MathF.Pow(1.1f, args.Delta.Y), MinZoom, MaxZoom);
        args.Handle();
    }

    protected override bool HasPoint(Vector2 point)
    {
        // only the disc reacts, so the map below stays usable around the planet
        var radius = Radius / UIScale * 1.05f;
        return (point - Size / 2).LengthSquared() <= radius * radius;
    }

    /// <summary>
    /// Cell under a point of the control, null outside the planet
    /// </summary>
    private int? Pick(Vector2 pixel)
    {
        if (_war == null)
            return null;

        var d = (pixel - Center) / MathF.Max(Radius, 1);
        var lengthSq = d.LengthSquared();
        if (lengthSq > 1)
            return null;

        var view = new Vector3(d.X, -d.Y, MathF.Sqrt(1 - lengthSq));
        var local = Vector3.Transform(view, Quaternion.Inverse(_rotation));
        return _war.Grid.Nearest(local);
    }

    /// <summary>
    /// Point of the unit sphere under a point of the control, null outside the planet
    /// </summary>
    private Vector3? PointAt(Vector2 pixel)
    {
        var d = (pixel - Center) / MathF.Max(Radius, 1);
        var lengthSq = d.LengthSquared();
        if (lengthSq > 1)
            return null;

        return Vector3.Transform(new Vector3(d.X, -d.Y, MathF.Sqrt(1 - lengthSq)), Quaternion.Inverse(_rotation));
    }

    private static Vector2 ToLonLat(Vector3 point)
    {
        return new Vector2(MathF.Atan2(point.Z, point.X), MathF.Asin(Math.Clamp(point.Y, -1, 1)));
    }

    private void ReleaseTool(Vector2 pixel)
    {
        if (Eraser)
        {
            if (MarkAt(pixel) is { } index)
                OnMarkErased?.Invoke(Marks[index].Side, Marks[index].Index);
            return;
        }

        if (Tool is not { } kind || PointAt(_pressPos) is not { } from)
            return;

        var start = ToLonLat(from);
        if (kind is not (ST14WarMarkKind.Attack or ST14WarMarkKind.Defend))
        {
            OnMarkPlaced?.Invoke(new ST14WarMark(kind, start, start, true));
            return;
        }

        // a press that came back to where it started draws nothing
        if (_dragging && PointAt(pixel) is { } to && Vector3.Distance(from, to) > 0.03f)
            OnMarkPlaced?.Invoke(new ST14WarMark(kind, start, ToLonLat(to), true));
    }

    /// <summary>
    /// Mark of the current side under the cursor for the eraser, an arrow is hit anywhere along it
    /// </summary>
    private int? MarkAt(Vector2 pixel)
    {
        var range = Radius * 0.06f;
        int? best = null;
        var bestDistance = range;
        for (var i = 0; i < Marks.Count; i++)
        {
            if (Marks[i].Side != ToolSide)
                continue;

            var mark = Marks[i].Mark;
            var steps = mark.IsArrow ? MarkSegments : 0;
            for (var s = 0; s <= steps; s++)
            {
                if (MarkPoint(mark, steps == 0 ? 0 : s / (float) steps) is not { } view || view.Z <= 0.02f)
                    continue;

                var distance = (Project(view) - pixel).Length();
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// A point along a mark as the viewer sees it. An arrow follows the shortest way over the planet
    /// </summary>
    private Vector3? MarkPoint(ST14WarMark mark, float t)
    {
        var a = ST14Sphere.Point(mark.From.X, mark.From.Y);
        var b = ST14Sphere.Point(mark.To.X, mark.To.Y);
        var sum = Vector3.Lerp(a, b, t);
        return sum.LengthSquared() < 0.0001f ? null : Vector3.Transform(Vector3.Normalize(sum), _rotation);
    }

    private void DrawMarks(DrawingHandleScreen handle)
    {
        if (Marks.Count == 0 && !(_pressed && _dragging && Tool is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend))
            return;

        var width = MathF.Max(3f, Radius / 85f);
        _marks.Clear();
        foreach (var (side, _, mark) in Marks)
        {
            AddMark(side, mark, width, 1f);
        }

        // the arrow being drawn follows the cursor
        if (_pressed && _dragging && Tool is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend &&
            PointAt(_pressPos) is { } from && PointAt(_lastPos) is { } to)
        {
            AddMark(ToolSide, new ST14WarMark(Tool!.Value, ToLonLat(from), ToLonLat(to), true), width, 0.55f);
        }

        _marks.Draw(handle, Texture.White);
    }

    private void AddMark(ST14GalaxyFaction side, ST14WarMark mark, float width, float alpha)
    {
        // the dark edge keeps a mark readable on any ground, red on the red planet too
        AddMarkShape(mark, width, MathF.Max(1.5f, width * 0.35f), Color.Black.WithAlpha(0.8f * alpha));
        AddMarkShape(mark, width, 0, ST14GalaxyMapView.MarkColor(side, mark.Kind).WithAlpha(alpha));
    }

    /// <summary>
    /// A mark in one color. Grow makes it wider on all sides, for the edge drawn under it
    /// </summary>
    private void AddMarkShape(ST14WarMark mark, float width, float grow, Color color)
    {
        if (mark.IsArrow)
        {
            Vector2? last = null;
            Vector2? before = null;
            for (var s = 0; s <= MarkSegments; s++)
            {
                if (MarkPoint(mark, s / (float) MarkSegments) is not { } view || view.Z <= 0.02f)
                {
                    last = null;
                    continue;
                }

                var pos = Project(view);
                if (last is { } previous)
                {
                    _marks.Line(previous, pos, width + grow * 2, color);
                    _marks.Circle(pos, width * 0.5f + grow, color, 8);
                    before = previous;
                }

                last = pos;
            }

            // the head only shows when the end of the arrow is on this side of the planet
            if (last is { } tip && before is { } back && (tip - back).LengthSquared() > 0.01f)
            {
                var dir = Vector2.Normalize(tip - back);
                ST14MarkShapes.Head(_marks, tip, dir, width, grow, color);
            }

            return;
        }

        if (MarkPoint(mark, 0) is not { } center || center.Z <= 0.02f)
            return;

        ST14MarkShapes.Sign(_marks, mark.Kind, Project(center), width, grow, color);
    }

    private float Lighting(Vector3 normal)
    {
        var diffuse = MathF.Max(0, Vector3.Dot(normal, _light));
        // soft terminator: the night side keeps enough light to read the sectors
        return 0.16f + 0.9f * MathF.Pow(diffuse, 0.85f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_planet == null || _texture == null)
            return;

        var phase = ST14PlanetDay.Phase(DateTime.UtcNow, _cfg.GetCVar(ST14CVars.DayLength), _cfg.GetCVar(ST14CVars.DayPhaseOffset));
        _light = Vector3.Normalize(Vector3.Transform(ST14PlanetDay.StarDirection(phase), _rotation));

        var center = Center;
        var radius = Radius;

        // atmosphere halo behind the disc
        _glow.Clear();
        var atmosphere = _planet.Atmosphere;
        _glow.Ring(center, radius * 0.97f, radius * 1.13f, atmosphere.WithAlpha(0.45f), atmosphere.WithAlpha(0f), 64);
        _glow.Draw(handle, Texture.White);

        // surface
        for (var i = 0; i < MeshPoints.Length; i++)
        {
            var view = Vector3.Transform(MeshPoints[i], _rotation);
            var light = Lighting(view);
            // the limb gets a touch of the atmosphere colour
            var rim = MathF.Pow(1 - MathF.Max(0, view.Z), 3) * 0.5f;
            var color = new Color(
                light + atmosphere.R * rim,
                light + atmosphere.G * rim,
                light + atmosphere.B * rim);
            _projected[i] = new DrawVertexUV2DColor(Project(view), MeshUv[i], Color.FromSrgb(color));
            _depth[i] = view.Z;
        }

        // the far side is culled, the sphere is convex so nothing else overlaps
        var count = 0;
        for (var i = 0; i < MeshIndices.Length; i += 3)
        {
            var a = MeshIndices[i];
            var b = MeshIndices[i + 1];
            var c = MeshIndices[i + 2];
            if (_depth[a] + _depth[b] + _depth[c] <= -0.05f)
                continue;

            _visible[count++] = a;
            _visible[count++] = b;
            _visible[count++] = c;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _texture, _visible.AsSpan(0, count), _projected);

        if (_war != null)
            DrawHexes(handle);

        // thin bright edge of the atmosphere over the disc
        _glow.Clear();
        _glow.Ring(center, radius * 0.9f, radius, atmosphere.WithAlpha(0f), atmosphere.WithAlpha(0.35f), 64);
        _glow.Draw(handle, Texture.White);
    }

    // drawn every frame, so the buffers are kept between frames instead of allocated per cell
    private readonly List<Vector2> _landings = new();
    private readonly List<(Vector3 From, Vector3 To)> _regionEdges = new();
    private Vector2[] _hexCorners = new Vector2[6];

    /// <summary>
    /// A solid arrow from the sector the attackers gather in to their target
    /// </summary>
    private void AddAttackArrow()
    {
        if (TargetCell is not { } target || StagingCell is not { } staging || _war == null)
            return;

        var from = Vector3.Transform(_war.Grid.Centers[staging], _rotation);
        var to = Vector3.Transform(_war.Grid.Centers[target], _rotation);
        if (from.Z <= 0.05f || to.Z <= 0.05f)
            return;

        var start = Project(from);
        var end = Project(to);
        var length = (end - start).Length();
        if (length < 1)
            return;

        var direction = (end - start) / length;
        var side = new Vector2(-direction.Y, direction.X);
        var tail = start + direction * length * 0.1f;
        var tip = start + direction * length * 0.8f;
        var neck = tip - direction * Radius * 0.08f;
        var head = Radius * 0.06f;
        var shaft = Radius * 0.026f;
        var outline = MathF.Max(2f, Radius * 0.008f);
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3.5f);

        _overlay.Line(tail, neck, shaft + outline * 2, Color.Black.WithAlpha(0.8f));
        _overlay.Triangle(tip + direction * outline * 1.8f, neck + side * (head + outline), neck - side * (head + outline), Color.Black.WithAlpha(0.8f));

        var color = Color.White.WithAlpha(0.82f + 0.18f * pulse);
        _overlay.Line(tail, neck, shaft, color);
        _overlay.Triangle(tip, neck + side * head, neck - side * head, color);
    }

    /// <summary>
    /// Plates with text that float over cells, and the one that follows the cursor
    /// </summary>
    private void DrawTags(DrawingHandleScreen handle)
    {
        if (TagCache == null || _war == null)
            return;

        if (TargetIcon != null && TargetCell is { } target)
        {
            var center = Vector3.Transform(_war.Grid.Centers[target], _rotation);
            if (center.Z > 0.05f)
            {
                var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);
                var iconSize = Radius * (0.15f + 0.012f * pulse) * MathF.Min(1, center.Z + 0.25f);
                var drawn = new Vector2(TargetIcon.Width, TargetIcon.Height) * (iconSize / MathF.Max(TargetIcon.Width, TargetIcon.Height));
                handle.DrawTextureRect(TargetIcon, UIBox2.FromDimensions(Project(center) - drawn / 2, drawn), TargetIconColor);
            }
        }

        var font = ST14HubStyle.Font(TagCache, ST14HubFont.SemiBold, Math.Max(8, (int) MathF.Round(Radius / 26f)));

        // two captions move apart along the line between their cells, a lone one hangs under its cell
        var away = Vector2.UnitY;
        if (Tags.Count == 2)
        {
            var a = Project(Vector3.Transform(_war.Grid.Centers[Tags[0].Cell], _rotation));
            var b = Project(Vector3.Transform(_war.Grid.Centers[Tags[1].Cell], _rotation));
            if ((b - a).LengthSquared() > 1)
                away = Vector2.Normalize(a - b);
        }

        for (var i = 0; i < Tags.Count; i++)
        {
            var (cell, text, color) = Tags[i];
            DrawTag(handle, font, cell, text, color, i == 0 ? away : -away);
        }

        if (_hovered is { } hovered && !_dragging && HoverText?.Invoke(hovered) is { } hover)
            DrawTag(handle, font, hovered, hover, Color.White, -Vector2.UnitY);
    }

    private void DrawTag(DrawingHandleScreen handle, Font font, int cell, string text, Color color, Vector2 direction)
    {
        var center = Vector3.Transform(_war!.Grid.Centers[cell], _rotation);
        if (center.Z <= 0.12f)
            return;

        var size = handle.GetDimensions(font, text, 1f);
        var pad = new Vector2(size.Y * 0.45f, size.Y * 0.18f);
        var pos = Project(center);

        // the caption stands clear of the cell, on the side it was told to
        var cellRadius = (Project(Vector3.Transform(_war.Grid.Corners[cell][0], _rotation)) - pos).Length();
        var half = size / 2 + pad;
        var reach = cellRadius + MathF.Abs(direction.X) * half.X + MathF.Abs(direction.Y) * half.Y;
        var middle = pos + direction * reach;
        var box = new UIBox2(middle - size / 2 - pad, middle + size / 2 + pad);
        handle.DrawRect(box, Color.Black.WithAlpha(0.82f));
        handle.DrawRect(box, color, false);
        handle.DrawString(font, middle - size / 2, text, color);
    }

    /// <summary>
    /// A carrier hangs over the centre of every region, the conquest of the region starts under it
    /// </summary>
    private void DrawCarriers(DrawingHandleScreen handle, ST14PlanetWar war)
    {
        var texture = _textures.Carrier;
        var aspect = (float) texture.Height / texture.Width;
        foreach (var cell in war.RegionCenters)
        {
            var center = Vector3.Transform(war.Grid.Centers[cell], _rotation);
            if (center.Z <= 0.25f)
                continue;

            var width = Radius * 0.17f * (0.55f + 0.45f * center.Z);
            var size = new Vector2(width, width * aspect);
            var pos = Project(center) + new Vector2(0, -size.Y * 1.05f);
            var alpha = MathF.Min(1, (center.Z - 0.25f) * 5);
            handle.DrawTextureRect(texture, UIBox2.FromDimensions(pos - size / 2, size), Color.White.WithAlpha(alpha));
        }
    }

    private void DrawHexes(DrawingHandleScreen handle)
    {
        var war = _war!;
        var grid = war.Grid;
        var width = MathF.Max(1f, Radius / 260f);
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);

        var period = MathF.Max(10f, Radius / 30f);
        _landings.Clear();

        _overlay.Clear();
        _hatch.Clear();
        for (var i = 0; i < grid.Count; i++)
        {
            var center = Vector3.Transform(grid.Centers[i], _rotation);
            if (center.Z <= 0.02f)
                continue;

            var cell = war.Cells[i];
            // the target gets the marker, without one every open landing zone does
            var landing = i == TargetCell;
            var staging = i == StagingCell;
            var focused = Focus != null && Focus.Contains(i);
            var color = focused ? FocusColor : landing ? ST14HubStyle.GoldLight : staging ? StagingColor : ST14GalaxyColors.Of(cell.Faction);
            var light = MathF.Min(1, Lighting(center) + 0.25f);
            // cells near the limb fade out, the edge of the planet stays clean
            var edge = MathF.Min(1, center.Z * 4);

            var fill = cell.Faction switch
            {
                ST14GalaxyFaction.Federation => 0.2f,
                ST14GalaxyFaction.Contested => 0.3f + 0.2f * pulse,
                _ => 0.12f,
            };

            if (landing || staging)
                fill = 0.38f + 0.14f * pulse;
            else if (focused)
                fill = 0.2f + 0.12f * pulse;

            if (i == _hovered)
                fill += 0.12f;
            if (i == _selected)
                fill += 0.15f;

            var centerPos = Project(center);
            var corners = grid.Corners[i];
            var count = corners.Length;
            if (_hexCorners.Length < count)
                _hexCorners = new Vector2[count];

            var projected = _hexCorners;
            for (var c = 0; c < count; c++)
            {
                projected[c] = Project(Vector3.Transform(corners[c], _rotation));
            }

            var fillColor = new Color(color.R * light, color.G * light, color.B * light, fill * edge);
            // bug ground is hatched like enemy space on the galaxy map
            var hatched = cell.Faction == ST14GalaxyFaction.Arachnid;
            var hatchColor = new Color(color.R * light, color.G * light, color.B * light, 0.3f * edge);
            for (var c = 0; c < count; c++)
            {
                var next = projected[(c + 1) % count];
                _overlay.Triangle(centerPos, projected[c], next, fillColor);
                if (hatched)
                    _hatch.PatternTriangle(centerPos, projected[c], next, period, hatchColor);
                if (Focus != null && !focused && !landing && !staging)
                    _overlay.Triangle(centerPos, projected[c], next, Color.Black.WithAlpha(0.45f * edge));
            }

            if (landing)
                _landings.Add(centerPos);

            var front = Focus == null && cell.Front;
            var lineColor = i == _selected ? Color.White : focused || landing || staging ? color : front ? ST14HubStyle.GoldLight : i == _hovered ? Color.White : color;
            var lineAlpha = (i == _selected || i == _hovered || landing || staging || focused ? 0.95f : 0.4f) * edge;
            var lineWidth = i == _selected ? width * 3f : landing || staging ? width * 2.5f : focused || i == _hovered ? width * 2f : width;
            // a little inside the cell, neighbours do not draw over each other
            for (var c = 0; c < count; c++)
            {
                var a = Vector2.Lerp(centerPos, projected[c], 0.94f);
                var b = Vector2.Lerp(centerPos, projected[(c + 1) % count], 0.94f);
                _overlay.Line(a, b, lineWidth, lineColor.WithAlpha(lineAlpha));
            }

            // marks: a hive is a red dot, a Federation base is a blue diamond
            var size = Radius * 0.018f * (0.6f + 0.4f * center.Z);
            if (cell.Hive)
            {
                _overlay.Circle(centerPos, size * 1.4f, Color.FromHex("#300800").WithAlpha(0.8f * edge), 12);
                _overlay.Circle(centerPos, size, Color.FromHex("#FF4A1A").WithAlpha((0.6f + 0.4f * pulse) * edge), 12);
            }
            else if (cell.Base)
            {
                _overlay.Triangle(centerPos + new Vector2(0, -size * 1.5f), centerPos + new Vector2(size * 1.2f, 0),
                    centerPos + new Vector2(0, size * 1.5f), ST14GalaxyColors.Federation.WithAlpha(0.95f * edge));
                _overlay.Triangle(centerPos + new Vector2(0, -size * 1.5f), centerPos + new Vector2(0, size * 1.5f),
                    centerPos + new Vector2(-size * 1.2f, 0), ST14GalaxyColors.Federation.WithAlpha(0.95f * edge));
            }
        }

        // the regions stand out from the honeycomb with a bright border
        foreach (var (from, to) in _regionEdges)
        {
            var a = Vector3.Transform(from, _rotation);
            var b = Vector3.Transform(to, _rotation);
            if (a.Z <= 0.02f || b.Z <= 0.02f)
                continue;

            var alpha = MathF.Min(1, MathF.Min(a.Z, b.Z) * 6) * (Focus != null ? 0.6f : 1f);
            var color = Color.White.WithAlpha(alpha);
            var from2 = Project(a);
            var to2 = Project(b);
            var line = MathF.Max(1.5f, width * 2.2f);
            _overlay.Line(from2, to2, line, color);
            // round joints: without them the corners of the border show notches
            _overlay.Circle(from2, line / 2, color, 8);
            _overlay.Circle(to2, line / 2, color, 8);
        }

        AddAttackArrow();

        // the landing zones get a marker that stands over the planet
        foreach (var pos in _landings)
        {
            if (!TargetMarker)
                break;

            var size = Radius * 0.035f;
            var lift = pos + new Vector2(0, -size * 2.2f);
            _overlay.Line(pos, lift, MathF.Max(1, width * 1.5f), ST14HubStyle.GoldLight.WithAlpha(0.9f));
            _overlay.Circle(lift, size, ST14HubStyle.GoldLight, 6);
            _overlay.Circle(lift, size * 0.55f, Color.FromHex("#141A22"), 16);
            _overlay.Circle(lift, size * 0.22f, ST14HubStyle.GoldLight, 10);
            var wave = (_time * 0.7f) % 1f;
            _overlay.Ring(pos, size * (0.6f + wave * 1.8f), size * (0.6f + wave * 1.8f) + MathF.Max(1, width * 1.5f),
                ST14HubStyle.GoldLight.WithAlpha(1 - wave), ST14HubStyle.GoldLight.WithAlpha(1 - wave), 24);
        }

        _hatch.Draw(handle, _textures.Hatch);
        _overlay.Draw(handle, Texture.White);
        DrawCarriers(handle, war);
        DrawMarks(handle);
        DrawTags(handle);
    }
}
