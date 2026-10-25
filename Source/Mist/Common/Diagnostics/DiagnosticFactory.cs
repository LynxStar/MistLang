using System.Collections.Immutable;
using System.Globalization;
using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// The only construction path for an occurrence. It checks the arguments against the descriptor's
/// slot schema and never throws: a breach yields the intended occurrence rendered through the
/// fallback text plus one internal compiler error naming the descriptor and the producing stage,
/// and a span that did not fit its file yields the occurrence with an absent location plus one
/// internal compiler error naming the producer. No location is ever fabricated.
/// </summary>
public static class DiagnosticFactory
{
    public static Admission Create(
        MistDiagnostic descriptor,
        Location location,
        PipelineStage stage,
        ReadOnlySpan<SlotValue> arguments,
        ImmutableArray<RelatedLocation> related = default,
        ImmutableArray<Frame> frames = default,
        int? cause = null,
        ProviderCode? native = null,
        string? analyzerName = null)
    {
        return Create(descriptor, new OriginResolution(location, frames), stage, arguments, related, cause, native, analyzerName);
    }

    public static Admission Create(
        MistDiagnostic descriptor,
        OriginResolution origin,
        PipelineStage stage,
        ReadOnlySpan<SlotValue> arguments,
        ImmutableArray<RelatedLocation> related = default,
        int? cause = null,
        ProviderCode? native = null,
        string? analyzerName = null)
    {
        origin = origin.BoundsFault is null
            ? DiagnosticHelper.Place(origin.Location, origin.Frames)
            : origin with { Location = new Absent(AbsenceReason.OutOfBounds) };
        var boundsFault = origin.BoundsFault;
        related = ValidateRelated(related, ref boundsFault);
        var values = arguments.ToImmutableArray();
        var reason = Breach(descriptor, values);
        var occurrence = new Diagnostic(
            descriptor, values, descriptor.DefaultSeverity, stage, origin.Location, related, origin.Frames, analyzerName, native,
            rendersFallback: reason is not null, legacyText: null, cause);

        var breaches = ImmutableArray.CreateBuilder<Diagnostic>();
        if(reason is not null)
        {
            breaches.Add(new Diagnostic(
                InternalDiagnostics.ArgumentSchemaBreach,
                [new NameValue(descriptor.Number.Value), new EnumValue(stage.ToString()), new TextValue(reason)],
                DiagnosticSeverity.Error, stage, origin.Location, default, origin.Frames, analyzerName, null,
                rendersFallback: false, legacyText: null));
        }

        if(boundsFault is { } fault)
        {
            breaches.Add(new Diagnostic(
                InternalDiagnostics.LocationOutOfBounds,
                [new EnumValue(stage.ToString()), new NameValue(descriptor.Number.Value), new TextValue(fault)],
                DiagnosticSeverity.Error, stage, origin.Location, default, origin.Frames, analyzerName, null,
                rendersFallback: false, legacyText: null));
        }

        return new Admission(occurrence, breaches.ToImmutable());
    }

    private static ImmutableArray<RelatedLocation> ValidateRelated(ImmutableArray<RelatedLocation> related, ref string? boundsFault)
    {
        if(related.IsDefaultOrEmpty)
        {
            return related;
        }

        var validated = ImmutableArray.CreateBuilder<RelatedLocation>(related.Length);
        foreach(var item in related)
        {
            var placed = DiagnosticHelper.Place(item.Location);
            boundsFault ??= placed.BoundsFault;
            validated.Add(item with { Location = placed.Location });
        }

        return validated.MoveToImmutable();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The span stays in the adapter's signature for the producers Phase 2 migrates; without the text it was computed against it cannot be placed.")]
    internal static Diagnostic Legacy(
        MistDiagnostic descriptor,
        DiagnosticSeverity severity,
        PipelineStage stage,
        string renderedText,
        LinePositionSpan span,
        string compilationUnitPath,
        string? analyzerName = null,
        ProviderCode? native = null)
    {
        return LegacyAt(descriptor, severity, stage, renderedText, LegacyLocation.Map(compilationUnitPath), analyzerName, native);
    }

    internal static Diagnostic LegacyAt(
        MistDiagnostic descriptor,
        DiagnosticSeverity severity,
        PipelineStage stage,
        string renderedText,
        Location location,
        string? analyzerName = null,
        ProviderCode? native = null)
    {
        return new Diagnostic(
            descriptor, [new TextValue(renderedText)], severity, stage, location, default, default, analyzerName, native,
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
            if(highest >= values.Length)
            {
                return $"placeholder {{{highest}}} has no argument ({values.Length} supplied)";
            }

            for(var i = 0; i < values.Length; i++)
            {
                if(Malformed($"argument {i}", values[i]) is { } reason)
                {
                    return reason;
                }
            }

            return null;
        }

        if(values.Length != descriptor.Slots.Length)
        {
            return $"{descriptor.Slots.Length} slots declared but {values.Length} arguments supplied";
        }

        for(var i = 0; i < descriptor.Slots.Length; i++)
        {
            var slot = descriptor.Slots[i];
            var subject = $"slot '{slot.Name}'";
            var value = values[i];
            if(value is null)
            {
                return $"{subject} is null";
            }

            if(value.Kind != slot.Kind)
            {
                return $"{subject} is {slot.Kind} but the argument is {value.Kind}";
            }

            var element = value switch
            {
                SequenceValue sequence => sequence.ElementKind,
                OptionalValue optional => optional.ElementKind,
                _ => (SlotKind?)null,
            };

            if(slot.ElementKind is { } expected && element != expected)
            {
                return $"{subject} carries {expected} but the argument carries {element}";
            }

            if(Malformed(subject, value) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    // A composite's declared element kind is a claim about its payload, which the record types cannot
    // enforce; this walk is what makes "Optional of a kind" and "Sequence of a kind" true of the values.
    private static string? Malformed(string subject, SlotValue? value)
    {
        switch(value)
        {
            case null:
                return $"{subject} is null";

            case OptionalValue { Value: { } payload } optional:
                return Payload(subject, optional.ElementKind, payload, "its value");

            case SequenceValue sequence:
                if(sequence.Items.IsDefault)
                {
                    return $"{subject} has no item list";
                }

                for(var i = 0; i < sequence.Items.Length; i++)
                {
                    if(Payload(subject, sequence.ElementKind, sequence.Items[i], $"item {i}") is { } reason)
                    {
                        return reason;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string? Payload(string subject, SlotKind expected, SlotValue? item, string position)
    {
        if(item is null)
        {
            return $"{subject} has a null {position}";
        }

        if(item.Kind != expected)
        {
            return $"{subject} carries {expected} but {position} is {item.Kind}";
        }

        return Malformed($"{subject} {position}", item);
    }
}