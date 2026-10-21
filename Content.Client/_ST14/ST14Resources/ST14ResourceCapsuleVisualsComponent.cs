using Robust.Shared.Utility;

namespace Content.Client._ST14.ST14Resources;

[RegisterComponent]
public sealed partial class ST14ResourceCapsuleVisualsComponent : Component
{
    [DataField]
    public string Layer = "fill";

    [DataField]
    public int MaxFillLevels = 4;

    [DataField]
    public string FillBaseName = "fill-";

    /// <summary>
    /// Если задано — состояния слоя по порядку от пустой к полной (вместо FillBaseName + номер).
    /// Первое состояние показывается у пустой капсулы.
    /// </summary>
    [DataField]
    public List<string>? FillStates;

    /// <summary>
    /// Красить слой заполнения цветом ресурса. Выключи для уже окрашенных спрайтов.
    /// </summary>
    [DataField]
    public bool ApplyColor = true;

    /// <summary>
    /// RSI для пустой капсулы и для ресурсов, которых нет в <see cref="Sprites"/>.
    /// Путь от /Textures, например _ST14/Structures/ST14Resources/bluegoo/32x32.rsi
    /// </summary>
    [DataField]
    public ResPath? EmptySprite;

    /// <summary>
    /// ID ресурса -> RSI капсулы с этим ресурсом.
    /// </summary>
    [DataField]
    public Dictionary<string, ResPath> Sprites = new();
}
