namespace Mist.Common.Diagnostics;

public enum SlotKind
{
    Name,
    Type,
    Symbol,
    Path,
    Quantity,
    Boolean,
    Enum,
    Text,
    Sequence,
    Optional,
}

/// <summary>
/// One named, typed argument position of a descriptor. <paramref name="ElementKind"/> is the item
/// kind of a <see cref="SlotKind.Sequence"/> or the carried kind of a <see cref="SlotKind.Optional"/>.
/// </summary>
public readonly record struct Slot(string Name, SlotKind Kind, SlotKind? ElementKind = null);