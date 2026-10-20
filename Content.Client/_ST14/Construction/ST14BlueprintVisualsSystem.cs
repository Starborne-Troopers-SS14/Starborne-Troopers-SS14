using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14BlueprintVisualsSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private ST14MultitoolSystem _multitool = default!;

    private static readonly ProtoId<ShaderPrototype> GhostShader = "ST14BlueprintGhost";

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ST14BlueprintVisualsComponent, ST14BlueprintComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var blueprint, out var sprite))
        {
            if (visuals.Buildable != blueprint.Buildable)
                CopyResultSprite((uid, visuals, sprite), blueprint.Buildable);

            visuals.Shader?.SetParameter("progress", _multitool.GetProgress(blueprint));
        }
    }

    private void CopyResultSprite(Entity<ST14BlueprintVisualsComponent, SpriteComponent> ent, ProtoId<ST14EngineerBuildablePrototype>? buildableId)
    {
        var (uid, visuals, sprite) = ent;

        visuals.Buildable = buildableId;
        visuals.MainLayer = -1;
        visuals.Shader = null;

        if (buildableId is not { } id || !ProtoMan.TryIndex(id, out var buildable))
            return;

        var dummy = Spawn(buildable.Result, MapCoordinates.Nullspace);
        if (TryComp<SpriteComponent>(dummy, out var resultSprite))
        {
            _appearance.OnChangeData(dummy, resultSprite);
            _sprite.CopySprite((dummy, resultSprite), (uid, sprite));
        }

        Del(dummy);

        var shader = ProtoMan.Index(GhostShader).InstanceUnique();
        shader.SetParameter("progress", 0f);
        shader.SetParameter("edgeRows", visuals.EdgeRows);
        shader.SetParameter("edgeColor", visuals.EdgeColor);
        shader.SetParameter("holoAlpha", visuals.HologramAlpha);

        var index = 0;
        foreach (var spriteLayer in sprite.AllLayers)
        {
            if (spriteLayer is SpriteComponent.Layer { ActualRsi: { } rsi } layer)
            {
                if (visuals.MainLayer < 0 && layer.Visible)
                {
                    visuals.MainLayer = index;
                    shader.SetParameter("pixelHeight", (float) rsi.Size.Y);
                }

                sprite.LayerSetShader(index, shader, GhostShader.Id);
            }

            index++;
        }

        visuals.Shader = shader;
    }
}
