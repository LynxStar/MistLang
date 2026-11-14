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

/// ElementKind names the payload kind for Sequence and Optional slots.
public readonly record struct Slot(string Name, SlotKind Kind, SlotKind? ElementKind = null);