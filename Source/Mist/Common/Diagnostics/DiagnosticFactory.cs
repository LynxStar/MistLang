using System.Collections.Immutable;
using System.Globalization;
using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The only construction path for an occurrence. It checks the arguments against the descriptor's
/// slot schema and never throws: a breach yields the intended occurrence rendered through the
/// fallback text plus one internal compiler error naming the descriptor and the producing stage.
/// </summary>
public static class DiagnosticFactory
{
    public static Admission Create(
        MistDiagnostic descriptor,
        LinePositionSpan location,
        string compilationUnitPath,
        PipelineStage stage,
        ReadOnlySpan<SlotValue> arguments,
        string? analyzerName = null,
        ProviderCode? native = null)
    {
        var values = arguments.ToImmutableArray();
        var reason = Breach(descriptor, values);
        var occurrence = new Diagnostic(
            descriptor, values, descriptor.DefaultSeverity, stage, location, compilationUnitPath, analyzerName, native,
            rendersFallback: reason is not null, legacyText: null);

        if(reason is null)
        {
            return new Admission(occurrence, null);
        }

        var breach = new Diagnostic(
            InternalDiagnostics.ArgumentSchemaBreach,
            [new NameValue(descriptor.Number.Value), new EnumValue(stage.ToString()), new TextValue(reason)],
            DiagnosticSeverity.Error, stage, location, compilationUnitPath, analyzerName, null,
            rendersFallback: false, legacyText: null);

        return new Admission(occurrence, breach);
    }

    internal static Diagnostic Legacy(
        MistDiagnostic descriptor,
        DiagnosticSeverity severity,
        PipelineStage stage,
        string renderedText,
        LinePositionSpan location,
        string compilationUnitPath,
        string? analyzerName = null,
        ProviderCode? native = null)
    {
        return new Diagnostic(
            descriptor, [new TextValue(renderedText)], severity, stage, location, compilationUnitPath, analyzerName, native,
            rendersFallback: false, legacyText: renderedText);
    }

    private static string? Breach(MistDiagnostic descriptor, ImmutableArray<SlotValue> values)
    {
        if(!TemplateSyntax.TryParse(descriptor.Template, out var segments, out var error))
        {
            return $"the template does not parse: {error}";
        }

        var placeholders = TemplateSyntax.Placeholders(segments).ToList();
        if(placeholders.Count == 0)
        {
            return null;
        }

        if(TemplateSyntax.IsPositional(segments))
        {
            var highest = placeholders.Max(name => int.Parse(name, CultureInfo.InvariantCulture));
            return highest < values.Length ? null : $"placeholder {{{highest}}} has no argument ({values.Length} supplied)";
        }

        if(values.Length != descriptor.Slots.Length)
        {
            return $"{descriptor.Slots.Length} slots declared but {values.Length} arguments supplied";
        }

        for(var i = 0; i < descriptor.Slots.Length; i++)
        {
            var slot = descriptor.Slots[i];
            var value = values[i];
            if(value.Kind != slot.Kind)
            {
                return $"slot '{slot.Name}' is {slot.Kind} but the argument is {value.Kind}";
            }

            var element = value switch
            {
                SequenceValue sequence => sequence.ElementKind,
                OptionalValue optional => optional.ElementKind,
                _ => (SlotKind?)null,
            };

            if(slot.ElementKind is { } expected && element != expected)
            {
                return $"slot '{slot.Name}' carries {expected} but the argument carries {element}";
            }
        }

        return null;
    }
}