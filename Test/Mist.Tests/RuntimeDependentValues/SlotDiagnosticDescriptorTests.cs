using Mist.Common.Diagnostics;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class SlotDiagnosticDescriptorTests
{
    [Fact]
    public void TheSlotDiagnosticsTookTheLowestNumbersAboveEveryOneIssuedBeforeThem()
    {
        MistDiagnostic[] minted =
        [
            ProgramAnalysisDiagnostics.ClosingNotProven, ProgramModelDiagnostics.SlotValueOutsideBody, null!,
            ProgramModelDiagnostics.SlotReadThroughUnknown, ProgramModelDiagnostics.SlotNotInferable,
            BackendDiagnostics.StaticStorageOnValueRoute, BackendDiagnostics.TypeValueOfValueRoutedClosing,
            BackendDiagnostics.ValueRoutedAbstractAtTwoValues, null!,
            BackendDiagnostics.ValueRoutedConstructorConstraintClosure, null!,
            BackendDiagnostics.CoincidingOverloads, BackendDiagnostics.InferredSlotNestedInArgument,
            BackendDiagnostics.ValueRoutedSlotOutOfReach, BackendDiagnostics.ValueRoutedParameterAsType,
            BackendDiagnostics.CoincidingUnionConstituents, BackendDiagnostics.SlotSensitiveClosingArgument,
            null!,
        ];
        var retired = DiagnosticRegistry.Index.Tombstones.Select(tombstone => tombstone.Number.Value).ToList();
        var issued = DiagnosticRegistry.Index.Descriptors.Select(descriptor => descriptor.Number.Value)
            .Concat(retired)
            .Where(number => string.CompareOrdinal(number, minted[0].Number.Value) < 0)
            .Max(number => int.Parse(number[4..], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(["MIST0488", "MIST0494", "MIST0496", "MIST0503"], retired);
        Assert.Equal(
            Enumerable.Range(1, minted.Length).Select(offset => $"MIST{issued + offset:D4}"),
            minted.Select((descriptor, i) => descriptor?.Number.Value ?? retired.Single(number => number == $"MIST{issued + i + 1:D4}")));
    }

    [Fact]
    public void AnUnprovenClosingNamesBothTypesAndBothValues()
    {
        var text = DiagnosticRenderer.Render(
            ProgramAnalysisDiagnostics.ClosingNotProven,
            [new TypeValue("Standard.Array<Int32, m@1>", "int[m]"), new TypeValue("Standard.Array<Int32, n@2>", "int[n]"), new NameValue("m"), new NameValue("n"), new OptionalValue(SlotKind.Name, null)]);

        Assert.Equal("'int[m]' is not proven to be 'int[n]': 'm' and 'n' are not proven identical.", text);
    }
}