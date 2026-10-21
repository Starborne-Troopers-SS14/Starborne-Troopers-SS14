namespace Content.Shared._ST14.ST14Resources.Components;

/// <summary>
/// Предмет можно держать только двумя руками.
/// Замена ванильного MultiHandedItem без клиентского предсказания (оно роняло клиент).
/// Вся логика — в серверной ST14TwoHandedItemSystem.
/// </summary>
[RegisterComponent]
public sealed partial class ST14TwoHandedItemComponent : Component
{
    /// <summary>
    /// Сколько рук нужно, включая руку, в которой лежит сам предмет.
    /// </summary>
    [DataField]
    public int HandsNeeded = 2;
}
