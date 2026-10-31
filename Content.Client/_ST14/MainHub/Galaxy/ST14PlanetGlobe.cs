using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
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

    // light comes from the upper left and a bit from the viewer, like on the map of the original game
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.55f, 0.45f, 0.7f));

    private static readonly Vector3[] MeshPoints;
    private static readonly Vector2[] MeshUv;
    private static readonly ushort[] MeshIndices;

    private readonly ST14GalaxyTextures _textures;
    private readonly ST14VertexBuffer _hatch = new();
    private readonly ST14VertexBuffer _overlay = new();
    private readonly ST14VertexBuffer _glow = new();
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

    /// <summary>
    /// Radius of the planet relative to the half of the smaller side of the control
    /// </summary>
    public float Zoom = 0.86f;

    public float MinZoom = 0.86f;
    public float MaxZoom = 0.86f;

    public event Action<int?>? OnCellSelected;
    public event Action<int?>? OnCellHovered;

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

        var target = _war.Grid.Centers[cell];
        // yaw the cell to the front, then tilt it a little towards the light
        var yaw = MathF.Atan2(target.X, target.Z);
        var pitch = MathF.Asin(Math.Clamp(target.Y, -1, 1));
        _rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * 0.8f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
        _spin = Vector2.Zero;
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

        if (_dragging)
            return;

        // the push of the last drag runs out, then the planet slowly turns on its own axis
        _idle += dt;
        _spin *= MathF.Exp(-dt * 2.5f);
        if (_idle > 2.5f)
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

            if (_dragging)
            {
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

    private float Lighting(Vector3 normal)
    {
        var diffuse = MathF.Max(0, Vector3.Dot(normal, Light));
        // soft terminator: the night side keeps a little light
        return 0.1f + 0.95f * MathF.Pow(diffuse, 0.85f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_planet == null || _texture == null)
            return;

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
    private Vector2[] _hexCorners = new Vector2[6];

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
            var landing = TargetCell == null ? cell.Faction == ST14GalaxyFaction.Contested : i == TargetCell;
            var color = landing ? ST14HubStyle.GoldLight : ST14GalaxyColors.Of(cell.Faction);
            var light = MathF.Min(1, Lighting(center) + 0.25f);
            // cells near the limb fade out, the edge of the planet stays clean
            var edge = MathF.Min(1, center.Z * 4);

            var fill = cell.Faction switch
            {
                ST14GalaxyFaction.Federation => 0.2f,
                ST14GalaxyFaction.Contested => 0.3f + 0.2f * pulse,
                _ => 0.12f,
            };

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
            }

            if (landing)
                _landings.Add(centerPos);

            var lineColor = i == _selected || cell.Front ? ST14HubStyle.GoldLight : i == _hovered ? Color.White : color;
            var lineAlpha = (i == _selected || i == _hovered || landing ? 0.95f : 0.4f) * edge;
            var lineWidth = i == _selected || landing ? width * 2.5f : i == _hovered ? width * 2f : width;
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

        // the landing zones get a marker that stands over the planet
        foreach (var pos in _landings)
        {
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
    }
}
