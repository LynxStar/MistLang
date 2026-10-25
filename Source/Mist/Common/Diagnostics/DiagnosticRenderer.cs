using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The one component that turns an occurrence into message text. It formats under the invariant
/// culture, substitutes by name for a migrated template and by index for one still positional,
/// and never throws: a fault while rendering yields the fallback text, which has no slots and so
/// cannot fault again.
/// </summary>
public static class DiagnosticRenderer
{
    private static readonly ConcurrentDictionary<MistDiagnostic, ImmutableArray<TemplateSyntax.Segment>?> Parsed =
        new(ReferenceEqualityComparer.Instance);

    public static string Render(Diagnostic diagnostic)
    {
        if(diagnostic.LegacyText is not null)
        {
            return diagnostic.LegacyText;
        }

        return diagnostic.RendersFallback
            ? Fallback(diagnostic.Descriptor)
            : Render(diagnostic.Descriptor, diagnostic.Arguments);
    }

    public static string Render(MistDiagnostic descriptor, ImmutableArray<SlotValue> arguments)
    {
        try
        {
            var segments = Parsed.GetOrAdd(descriptor, Parse);
            if(segments is null)
            {
                return Fallback(descriptor);
            }

            var text = new StringBuilder();
            return Append(text, segments.Value, descriptor, arguments) ? text.ToString() : Fallback(descriptor);
        }
        catch(Exception exception) when(exception is not OutOfMemoryException)
        {
            return Fallback(descriptor);
        }
    }

    public static string Fallback(MistDiagnostic descriptor)
    {
        return descriptor.Title + ". The message could not be rendered; an internal compiler error reported alongside says why.";
    }

    public static string Render(SlotValue value)
    {
        return value switch
        {
            NameValue name => name.Name,
            TypeValue type => type.Spelling,
            SymbolValue symbol => symbol.Spelling,
            PathValue path => path.Path,
            QuantityValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            BooleanValue boolean => boolean.Value ? "true" : "false",
            EnumValue token => token.Token,
            TextValue text => text.Text,
            SequenceValue sequence => string.Join(", ", sequence.Items.Select(Render)),
            OptionalValue optional => optional.Value is null ? "" : Render(optional.Value),
            _ => "",
        };
    }

    private static ImmutableArray<TemplateSyntax.Segment>? Parse(MistDiagnostic descriptor)
    {
        return TemplateSyntax.TryParse(descriptor.Template, out var segments, out _) ? segments : null;
    }

    private static bool Append(
        StringBuilder text,
        ImmutableArray<TemplateSyntax.Segment> segments,
        MistDiagnostic descriptor,
        ImmutableArray<SlotValue> arguments)
    {
        foreach(var segment in segments)
        {
            switch(segment)
            {
                case TemplateSyntax.Literal literal:
                    _ = text.Append(literal.Text);
                    break;

                case TemplateSyntax.Placeholder placeholder:
                    if(Argument(placeholder.Name, descriptor, arguments) is not { } value)
                    {
                        return false;
                    }

                    _ = text.Append(Render(value));
                    break;

                case TemplateSyntax.Conditional conditional:
                    if(Argument(conditional.Name, descriptor, arguments) is not { } carrier)
                    {
                        return false;
                    }

                    if(carrier is OptionalValue { Value: null })
                    {
                        break;
                    }

                    if(!Append(text, conditional.Body, descriptor, arguments))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static SlotValue? Argument(string name, MistDiagnostic descriptor, ImmutableArray<SlotValue> arguments)
    {
        if(name.All(char.IsAsciiDigit))
        {
            var index = int.Parse(name, CultureInfo.InvariantCulture);
            return index < arguments.Length ? arguments[index] : null;
        }

        for(var i = 0; i < descriptor.Slots.Length; i++)
        {
            if(descriptor.Slots[i].Name == name)
            {
                return i < arguments.Length ? arguments[i] : null;
            }
        }

        return null;
    }
}