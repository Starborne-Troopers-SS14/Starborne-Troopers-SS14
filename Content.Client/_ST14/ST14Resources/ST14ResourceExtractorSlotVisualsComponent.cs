namespace Content.Client._ST14.ST14Resources;

/// <summary>
/// Рисует вставленные в бур капсулы и их заполнение.
/// Слой для каждого слота задаётся через map в прототипе.
/// </summary>
[RegisterComponent]
public sealed partial class ST14ResourceExtractorSlotVisualsComponent : Component
{
    /// <summary>
    /// Названия слоёв (map) для слота 1 и слота 2.
    /// </summary>
    [DataField]
    public string[] Layers = { "capsule_slot_1", "capsule_slot_2" };

    /// <summary>
    /// Состояния RSI по порядку: от пустой капсулы к полной.
    /// Если спрайты нарисованы в обратном порядке — просто переставь их в прототипе.
    /// </summary>
    [DataField]
    public List<string> FillStates = new() { "base", "filled-1", "filled-2", "filled-3" };
}
