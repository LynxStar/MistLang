using System.Collections.Immutable;
using System.Globalization;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The bridge from today's positional object arguments to slot values. Each argument is rendered
/// to text at report time exactly as composite formatting would have rendered it, so a producer
/// that has not migrated keeps producing the same message; a descriptor that already names its
/// slots takes the arguments in schema order, coerced to each slot's kind.
/// </summary>
internal static class LegacyArguments
{
    public static ImmutableArray<SlotValue> For(MistDiagnostic descriptor, ReadOnlySpan<object?> args)
    {
        if(descriptor.Slots.IsEmpty)
        {
            var positional = ImmutableArray.CreateBuilder<SlotValue>(args.Length);
            foreach(var arg in args)
            {
                positional.Add(arg is null ? new OptionalValue(SlotKind.Text, null) : new TextValue(Text(arg)));
            }

            return positional.MoveToImmutable();
        }

        var named = ImmutableArray.CreateBuilder<SlotValue>(descriptor.Slots.Length);
        for(var i = 0; i < descriptor.Slots.Length; i++)
        {
            var slot = descriptor.Slots[i];
            var arg = i < args.Length ? args[i] : null;
            named.Add(Coerce(slot.Kind, slot.ElementKind, arg));
        }

        return named.MoveToImmutable();
    }

    private static SlotValue Coerce(SlotKind kind, SlotKind? elementKind, object? arg)
    {
        if(arg is SlotValue value)
        {
            return value;
        }

        return kind switch
        {
            SlotKind.Optional => new OptionalValue(elementKind ?? SlotKind.Text, arg is null ? null : Coerce(elementKind ?? SlotKind.Text, null, arg)),
            SlotKind.Sequence => new SequenceValue(
                elementKind ?? SlotKind.Text,
                arg is System.Collections.IEnumerable items and not string
                    ? [.. items.Cast<object?>().Select(item => Coerce(elementKind ?? SlotKind.Text, null, item))]
                    : [Coerce(elementKind ?? SlotKind.Text, null, arg)]),
            SlotKind.Name => new NameValue(Text(arg)),
            SlotKind.Type => new TypeValue(Text(arg), Text(arg)),
            SlotKind.Symbol => new SymbolValue(Text(arg), Text(arg)),
            SlotKind.Path => new PathValue(Text(arg)),
            SlotKind.Quantity => new QuantityValue(Quantity(arg)),
            SlotKind.Boolean => new BooleanValue(arg is bool flag ? flag : bool.TryParse(Text(arg), out var parsedFlag) && parsedFlag),
            SlotKind.Enum => new EnumValue(Text(arg)),
            _ => new TextValue(Text(arg)),
        };
    }

    private static long Quantity(object? arg)
    {
        return arg switch
        {
            string text => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
            IConvertible convertible => convertible.ToInt64(CultureInfo.InvariantCulture),
            _ => 0,
        };
    }

    private static string Text(object? arg)
    {
        return arg switch
        {
            null => "",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => arg.ToString() ?? "",
        };
    }
}