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

    private static SlotValue Frozen(SlotKind kind, SlotKind? elementKind, int index, bool present)
    {
        return kind switch
        {
            SlotKind.Quantity => new QuantityValue(7),
            SlotKind.Boolean => new BooleanValue(true),
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