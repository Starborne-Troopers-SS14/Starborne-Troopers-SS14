using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Shared.Maths;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// The signs of a plan of a command. Grow makes a shape wider on all sides, for the dark edge drawn under it
/// </summary>
public static class ST14MarkShapes
{
    public static void Head(ST14VertexBuffer buffer, Vector2 tip, Vector2 direction, float width, float grow, Color color)
    {
        var wing = new Vector2(-direction.Y, direction.X) * (width * 2.6f + grow * 1.8f);
        var root = tip - direction * grow;
        buffer.Triangle(tip + direction * (width * 3.5f + grow * 2.2f), root + wing, root - wing, color);
    }

    public static void Sign(ST14VertexBuffer buffer, ST14WarMarkKind kind, Vector2 at, float width, float grow, Color color)
    {
        var size = width * 3.2f;
        switch (kind)
        {
            case ST14WarMarkKind.Rally:
                buffer.Line(at + new Vector2(-size * 0.4f, size + grow), at + new Vector2(-size * 0.4f, -size - grow), width * 0.7f + grow * 2, color);
                buffer.Triangle(at + new Vector2(-size * 0.4f, -size - grow * 1.6f), at + new Vector2(size * 0.9f + grow * 2.2f, -size * 0.55f),
                    at + new Vector2(-size * 0.4f, -size * 0.1f + grow * 1.6f), color);
                break;
            case ST14WarMarkKind.Danger:
                buffer.Triangle(at + new Vector2(0, -size - grow * 2f), at + new Vector2(size + grow * 1.8f, size * 0.8f + grow), at + new Vector2(-size - grow * 1.8f, size * 0.8f + grow), color);
                if (grow > 0)
                    break;

                var dark = Color.FromHex("#141A22").WithAlpha(color.A);
                buffer.Line(at + new Vector2(0, -size * 0.45f), at + new Vector2(0, size * 0.2f), width * 0.6f, dark);
                buffer.Circle(at + new Vector2(0, size * 0.48f), width * 0.35f, dark, 8);
                break;
            default:
                buffer.Ring(at, size * 0.75f - grow, size + grow, color, color, 20);
                buffer.Circle(at, size * 0.28f + grow, color, 10);
                break;
        }
    }
}
