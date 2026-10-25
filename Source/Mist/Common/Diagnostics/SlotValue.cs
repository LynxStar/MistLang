using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// A snapshot of one argument taken at report time. No live model object, node or exception is
/// ever stored on an occurrence, so later mutation cannot change what was reported.
/// </summary>
public abstract record SlotValue
{
    private protected SlotValue()
    {
    }

    public abstract SlotKind Kind { get; }
}

public sealed record NameValue(string Name) : SlotValue
{
    public override SlotKind Kind => SlotKind.Name;
}

public sealed record TypeValue(string Identity, string Spelling) : SlotValue
{
    public override SlotKind Kind => SlotKind.Type;
}

public sealed record SymbolValue(string Identity, string Spelling) : SlotValue
{
    public override SlotKind Kind => SlotKind.Symbol;
}

public sealed record PathValue(string Path) : SlotValue
{
    public override SlotKind Kind => SlotKind.Path;
}

public sealed record QuantityValue(long Value) : SlotValue
{
    public override SlotKind Kind => SlotKind.Quantity;
}

public sealed record BooleanValue(bool Value) : SlotValue
{
    public override SlotKind Kind => SlotKind.Boolean;
}

public sealed record EnumValue(string Token) : SlotValue
{
    public override SlotKind Kind => SlotKind.Enum;
}

public sealed record TextValue(string Text) : SlotValue
{
    public override SlotKind Kind => SlotKind.Text;
}

public sealed record SequenceValue(SlotKind ElementKind, ImmutableArray<SlotValue> Items) : SlotValue
{
    public override SlotKind Kind => SlotKind.Sequence;
}

public sealed record OptionalValue(SlotKind ElementKind, SlotValue? Value) : SlotValue
{
    public override SlotKind Kind => SlotKind.Optional;
}