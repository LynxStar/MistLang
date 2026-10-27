using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

/// <summary>
/// Pins the rendered text of every descriptor character for character (diagnostics spec
/// Requirement 7.9). A wording change regenerates the table on purpose with
/// tools/diagnostics/freeze_table.py in its own reviewed change; nothing else may move a row.
/// </summary>
[Trait("Tier", "1")]
public sealed class TextFreezeTests
{
    [Fact]
    public void EveryDescriptorRendersItsFrozenText()
    {
        var problems = new List<string>();

        foreach(var entry in DiagnosticRegistry.Index.Entries)
        {
            foreach(var (key, arguments) in Variants(entry))
            {
                if(!TextFreezeTable.Frozen.TryGetValue(key, out var frozen))
                {
                    problems.Add($"{key}: no frozen row");
                    continue;
                }

                var actual = DiagnosticRenderer.Render(entry.Descriptor, arguments);
                if(actual != frozen)
                {
                    problems.Add($"{key}:{Environment.NewLine}  frozen: {frozen}{Environment.NewLine}  actual: {actual}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryFrozenRowNamesALiveDescriptor()
    {
        var live = DiagnosticRegistry.Index.Entries.SelectMany(entry => Variants(entry).Select(variant => variant.Key)).ToHashSet(StringComparer.Ordinal);

        Assert.All(TextFreezeTable.Frozen.Keys, key => Assert.Contains(key, live));
    }

    private static IEnumerable<(string Key, ImmutableArray<SlotValue> Arguments)> Variants(RegistryEntry entry)
    {
        var descriptor = entry.Descriptor;
        if(entry.Field == "CommonDiagnostics.PackageNotFound")
        {
            yield return (entry.Field,
            [new OptionalValue(SlotKind.Name, null), new OptionalValue(SlotKind.Name, null),
             new OptionalValue(SlotKind.Path, null), new OptionalValue(SlotKind.Boolean, null),
             new OptionalValue(SlotKind.Boolean, null), new OptionalValue(SlotKind.Boolean, new BooleanValue(true)), new TextValue("arg0")]);
            yield break;
        }

        if(entry.Field == "CommonDiagnostics.PackageNetworkError")
        {
            yield return (entry.Field,
            [new OptionalValue(SlotKind.Path, null), new OptionalValue(SlotKind.Path, null),
             new OptionalValue(SlotKind.Path, null), new OptionalValue(SlotKind.Boolean, null),
             new OptionalValue(SlotKind.Boolean, new BooleanValue(true)), new TextValue("arg0")]);
            yield break;
        }

        if(entry.Field == "DriverDiagnostics.ReferenceCycle")
        {
            yield return (entry.Field,
            [new SequenceValue(SlotKind.Name, [new NameValue("arg0")]),
             new OptionalValue(SlotKind.Path, null), new OptionalValue(SlotKind.Name, null)]);
            yield break;
        }

        if(entry.Field == "ProgramAnalysisDiagnostics.InconsistentAccessibility")
        {
            yield return (entry.Field,
            [new EnumValue("arg0"), new OptionalValue(SlotKind.Type, new TypeValue("arg1", "arg1")),
             new OptionalValue(SlotKind.Symbol, null), new EnumValue("arg2"), new NameValue("arg3")]);
            yield return (entry.Field,
            [new EnumValue("arg0"), new OptionalValue(SlotKind.Type, null),
             new OptionalValue(SlotKind.Symbol, new SymbolValue("arg1", "arg1")), new EnumValue("arg2"), new NameValue("arg3")]);
            yield break;
        }

        if(entry.Field is "ProgramAnalysisDiagnostics.ConstraintStrengthened"
            or "ProgramAnalysisDiagnostics.ConstraintNotSatisfied"
            or "ProgramAnalysisDiagnostics.SlotConstraintStrengthened"
            or "ProgramAnalysisDiagnostics.TypeSlotConstraintNotSatisfied"
            or "ProgramAnalysisDiagnostics.DependentArgumentNotAssignable"
            or "ProgramAnalysisDiagnostics.DependentForwardNotConvertible")
        {
            yield return (entry.Field, Alternatives(descriptor, types: true));
            yield return (entry.Field, Alternatives(descriptor, types: false));
            yield break;
        }

        if(descriptor.Slots.IsEmpty)
        {
            Assert.True(TemplateSyntax.TryParse(descriptor.Template, out var segments, out _));
            var count = TemplateSyntax.Placeholders(segments).Select(int.Parse).DefaultIfEmpty(-1).Max() + 1;
            yield return (entry.Field, [.. Enumerable.Range(0, count).Select(index => (SlotValue)new TextValue($"arg{index}"))]);
            yield break;
        }

        yield return (entry.Field, [.. descriptor.Slots.Select((slot, index) => Frozen(slot.Kind, slot.ElementKind, index, present: true))]);

        foreach(var absent in descriptor.Slots.Where(slot => slot.Kind == SlotKind.Optional))
        {
            yield return ($"{entry.Field}|absent:{absent.Name}", [.. descriptor.Slots.Select((slot, index) => Frozen(slot.Kind, slot.ElementKind, index, present: slot.Name != absent.Name))]);
        }
    }

    private static ImmutableArray<SlotValue> Alternatives(MistDiagnostic descriptor, bool types)
    {
        var arguments = ImmutableArray.CreateBuilder<SlotValue>();
        var originalIndex = 0;
        for(var i = 0; i < descriptor.Slots.Length; i++, originalIndex++)
        {
            var slot = descriptor.Slots[i];
            if(slot is { Kind: SlotKind.Optional, ElementKind: SlotKind.Type })
            {
                arguments.Add(Frozen(SlotKind.Optional, SlotKind.Type, originalIndex, present: types));
                arguments.Add(Frozen(SlotKind.Optional, descriptor.Slots[++i].ElementKind, originalIndex, present: !types));
            }
            else
            {
                arguments.Add(Frozen(slot.Kind, slot.ElementKind, originalIndex, present: true));
            }
        }

        return arguments.ToImmutable();
    }

    private static SlotValue Frozen(SlotKind kind, SlotKind? elementKind, int index, bool present)
    {
        return kind switch
        {
            SlotKind.Quantity => new TextValue($"arg{index}"),
            SlotKind.Boolean => new TextValue($"arg{index}"),
            SlotKind.Name => new NameValue($"arg{index}"),
            SlotKind.Type => new TypeValue($"arg{index}", $"arg{index}"),
            SlotKind.Symbol => new SymbolValue($"arg{index}", $"arg{index}"),
            SlotKind.Path => new PathValue($"arg{index}"),
            SlotKind.Enum => new EnumValue($"arg{index}"),
            SlotKind.Sequence => new SequenceValue(elementKind ?? SlotKind.Text, [Frozen(elementKind ?? SlotKind.Text, null, index, true)]),
            SlotKind.Optional => new OptionalValue(elementKind ?? SlotKind.Text, present ? Frozen(elementKind ?? SlotKind.Text, null, index, true) : null),
            _ => new TextValue($"arg{index}"),
        };
    }
}