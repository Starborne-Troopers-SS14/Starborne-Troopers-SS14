using Content.Shared._ST14.Construction;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Construction;

[RegisterComponent]
public sealed partial class ST14BlueprintVisualsComponent : Component
{
    [DataField]
    public float EdgeRows = 2f;

    [DataField]
    public Color EdgeColor = Color.FromHex("#bfffff");

    [DataField]
    public float HologramAlpha = 0.8f;

    [DataField]
    public bool Sparks = true;

    [ViewVariables]
    public ProtoId<ST14EngineerBuildablePrototype>? Buildable;

    [ViewVariables]
    public int MainLayer = -1;

    [ViewVariables]
    public ShaderInstance? Shader;
}
