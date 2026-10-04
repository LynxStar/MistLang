// List patterns and slice elements: what each element position is typed by, where the binders live,
// and which shapes are refused. Asserted on the MODEL because the thing that makes the C# correct is
// that a positional element and a slice scrutinee DIFFERENT types -- an element and a sub-sequence
// -- and a slice element typed as an element still emits `.. var middle`, which Roslyn types
// correctly by itself. The negatives are compile failures and so exist nowhere else.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ListPatternResolutionTests : ProgramModelProbe
{
    public ListPatternResolutionTests()
        : base("MistListPattern_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Greeter
        {
            public string Name { get; set; }
        }

        public class Ledger
        {
            public int Count { get { return 2; } }

            public string this[string key] { get { return key; } }

            public int this[int i] { get { return i; } }
        }
        """;

    protected override string Preamble { get; } = """
        int[] numbers = [10, 20, 30, 40, 50];
        Greeter g = new Greeter();
        Ledger ledger = new Ledger();
        unknown u = 42;
        """;

    [Fact]
    public void APositionalListPatternCarriesOneElementPerPosition()
    {
        var pattern = Assert.IsType<ListPattern>(PatternOf("var ok = numbers is [10, 20];"));

        Assert.Equal(2, pattern.Elements.Count);
        Assert.All(pattern.Elements, element => Assert.False(element.IsSlice));
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void EachPositionalElementIsTypedByTheScrutineesElement()
    {
        var pattern = Assert.IsType<ListPattern>(PatternOf("var ok = numbers is [var a, var b];"));

        Assert.Equal(
            ["Standard.Int32", "Standard.Int32"],
            pattern.Elements.Select(element => element.Subpattern!.NarrowedType.FullyQualifiedName));
    }

    [Fact]
    public void APositionalElementIsTypedByTheIndexerAPositionSelects_NotTheFirstDeclared()
    {
        var pattern = Assert.IsType<ListPattern>(PatternOf("var ok = ledger is [var a, var b];"));

        Assert.Equal(
            ["Standard.Int32", "Standard.Int32"],
            pattern.Elements.Select(element => element.Subpattern!.NarrowedType.FullyQualifiedName));
    }

    /// <summary>
    /// The claim this capability turns on: a slice is scrutineed by the SLICE type and not by the
    /// element type, so one pattern narrows against two different types at once. Both are asserted
    /// together because a slice typed as its element is the failure to rule out.
    /// </summary>
    [Fact]
    public void ASliceIsTypedByTheSubsequence_NotByTheElement()
    {
        var pattern = Assert.IsType<ListPattern>(
            PatternOf("var ok = numbers is [var first, .. var middle, var last];"));

        var slice = Assert.Single(pattern.Elements, element => element.IsSlice);
        var sliceType = Assert.IsType<ConstructedType>(slice.Subpattern!.NarrowedType);

        Assert.Equal("Standard.Array<,>", sliceType.Definition.FullyQualifiedName);
        Assert.Equal("Standard.Int32", sliceType.TypeArguments[0].FullyQualifiedName);

        Assert.Equal(
            ["Standard.Int32", "Standard.Int32"],
            pattern.Elements
                .Where(element => !element.IsSlice)
                .Select(element => element.Subpattern!.NarrowedType.FullyQualifiedName));
    }

    /// <summary>
    /// Every binder a list pattern contributes lives on an element, the slice included, which is what
    /// <see cref="Pattern.Binding"/> being singular per node means here -- the grammar spells no trailing
    /// name for a list pattern at all.
    /// </summary>
    [Fact]
    public void TheBindersLiveOnTheElements_IncludingTheSlices()
    {
        var pattern = Assert.IsType<ListPattern>(
            PatternOf("var ok = numbers is [var first, .. var middle, var last];"));

        Assert.Equal(
            ["first", "middle", "last"],
            pattern.Elements.Select(element => element.Subpattern!.Binding?.Name));
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void ADiscardSliceCarriesNoSubpatternAtAll()
    {
        var pattern = Assert.IsType<ListPattern>(PatternOf("var ok = numbers is [10, ..];"));

        var slice = Assert.Single(pattern.Elements, element => element.IsSlice);
        Assert.Null(slice.Subpattern);
    }

    [Fact]
    public void AnElementSubpatternMayBeRelational()
    {
        var pattern = Assert.IsType<ListPattern>(PatternOf("var ok = numbers is [> 0, > 0];"));

        Assert.All(pattern.Elements, element => Assert.IsType<ComparisonPattern>(element.Subpattern));
    }

    [Fact]
    public void MoreThanOneSliceIsRefused()
    {
        var diagnostics = BuildRun("var ok = numbers is [.. var a, 30, .. var b];");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LIST_PATTERN_MULTIPLE_SLICES"));
    }

    [Fact]
    public void AScrutineeThatCannotBeIndexedIsRefused()
    {
        var diagnostics = BuildRun("var ok = g is [var a];");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LIST_PATTERN_SCRUTINEE"));
    }

    /// <summary>
    /// Refused where a tuple pattern would type-test, and the asymmetry is C#'s: a tuple pattern can
    /// name the ValueTuple ahead of itself, but <c>[...]</c> is the only spelling C# has for a list
    /// pattern and it requires a countable scrutinee, so there is no type to name first.
    /// </summary>
    [Fact]
    public void AnUnknownScrutineeIsRefusedRatherThanTypeTested()
    {
        var diagnostics = BuildRun("var ok = u is [var a];");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LIST_PATTERN_SCRUTINEE"));
    }

    [Fact]
    public void AnElementBinderUnderOrIsRefused()
    {
        var diagnostics = BuildRun("var ok = numbers is [var a, 20] or [10, var b];");

        Assert.Equal(2, Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED").Count());
    }

    [Fact]
    public void ASliceBinderUnderNotIsRefused()
    {
        var diagnostics = BuildRun("var ok = numbers is not [.. var middle];");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }
}