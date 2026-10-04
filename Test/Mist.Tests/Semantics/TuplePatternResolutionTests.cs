// Tuple positional patterns: which grammar alternative a spelling takes, what each element position
// is typed by, and whether matching type-tests or only deconstructs. Asserted on the MODEL because
// output cannot tell these apart -- a program that prints the right answer says only that SOME match
// happened, where the thing that makes the C# correct is whether the tuple type is tested at
// runtime, which is a different construct and not a different spelling. The negatives are compile
// failures and so exist nowhere else.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class TuplePatternResolutionTests : ProgramModelProbe
{
    public TuplePatternResolutionTests()
        : base("MistTuplePattern_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Greeter
        {
            public string Name { get; set; }
        }
        """;

    protected override string Preamble { get; } = """
        unknown u = 42;
        (int, int) coords = (1, 2);
        (int, string) pair = (1, "a");
        (unknown, unknown) mystery = (1, "a");
        ((int, int), string) nested = ((1, 2), "label");
        Greeter g = new Greeter();
        """;

    /// <summary>
    /// The four spellings below take THREE different grammar alternatives, and nothing but the trailing
    /// name decides which: <c>(int, string)</c> is a valid type reference, so the type alternative claims
    /// any tuple that is followed by one. Recorded as the model each reading produces, since that is the
    /// consequence -- a type pattern narrows to one type and keeps one name, a tuple pattern carries an
    /// element pattern per position.
    /// </summary>
    [Fact]
    public void ATupleWithNoTrailingName_ReadsAsATuplePattern()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = mystery is (int, string);"));

        Assert.Equal(2, pattern.Elements.Count);
        Assert.All(pattern.Elements, element => Assert.IsType<TypeTestPattern>(element));
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void ATupleWithNamedElementsAndNoTrailingName_ReadsAsATuplePattern()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = mystery is (int i, string s);"));

        Assert.Equal(["i", "s"], pattern.Elements.Select(element => element.Binding?.Name));
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void AnUnnamedTupleWithATrailingName_ReadsAsAPlainTypeTest()
    {
        var pattern = Assert.IsType<TypeTestPattern>(PatternOf("var ok = u is (int, string) whole;"));

        Assert.Equal("whole", pattern.Binding?.Name);
        _ = Assert.IsType<TupleType>(pattern.NarrowedType);
    }

    [Fact]
    public void ANamedTupleWithATrailingName_LiftsItsElementNamesIntoLocals()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = u is (int x, int y) whole;"));

        Assert.Equal(["x", "y"], pattern.Elements.Select(element => element.Binding?.Name));
        Assert.Equal("whole", pattern.Binding?.Name);
    }

    [Fact]
    public void TheWholeValueBindingIsTypedByTheUNNAMEDTuple_SoElementNamesAreNotAlsoMembers()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = u is (int x, int y) whole;"));

        _ = Assert.IsType<TupleType>(pattern.Binding?.Type);
        BindsCleanly("if (u is (int x, int y) whole) { var probe = $\"{x}{y}{whole.Item1}\"; }");

        var diagnostics = BuildRun("if (u is (int x, int y) whole) { var probe = whole.x; }");
        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
    }

    [Fact]
    public void EachElementIsTypedByTheScrutineesElementAtThatPosition()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = pair is (var n, var s);"));

        Assert.Equal(
            ["Standard.Int32", "Standard.String"],
            pattern.Elements.Select(element => element.NarrowedType.FullyQualifiedName));
    }

    [Fact]
    public void ANestedTuplePatternScrutineesTheElementTheOuterOneHeld()
    {
        var outer = Assert.IsType<TuplePattern>(PatternOf("var ok = nested is ((var a, var b), \"label\");"));
        var inner = Assert.IsType<TuplePattern>(outer.Elements[0]);

        Assert.Equal(
            ["Standard.Int32", "Standard.Int32"],
            inner.Elements.Select(element => element.NarrowedType.FullyQualifiedName));
    }

    [Fact]
    public void ATupleScrutineeIsOnlyDECONSTRUCTED_WithNoRuntimeTypeTest()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = coords is (var a, var b);"));

        Assert.Null(pattern.TestedType);
    }

    [Fact]
    public void AnUnknownScrutineeIsTYPETESTED_AgainstTheTupleItsElementsName()
    {
        var pattern = Assert.IsType<TuplePattern>(PatternOf("var ok = u is (int, string);"));

        Assert.Equal("(Int32, String)", pattern.TestedType?.Name);
    }

    [Fact]
    public void AnUnknownScrutineeWhoseElementsTestVALUES_ParksWithoutADiagnostic()
    {
        _ = InitializerOf<UnresolvedExpression>("var ok = u is (0, 0);");
        BindsCleanly("var ok = u is (0, 0);");

        _ = Assert.IsType<TuplePattern>(PatternOf("var ok = u is (int, string);"));
    }

    [Fact]
    public void AnArityOtherThanTheScrutineesIsRefused()
    {
        var diagnostics = BuildRun("var ok = coords is (var a, var b, var c);");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_TUPLE_PATTERN_ARITY"));
    }

    [Fact]
    public void AScrutineeThatIsNeitherATupleNorUnknownIsRefused()
    {
        var diagnostics = BuildRun("var ok = g is (var a, var b);");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_TUPLE_PATTERN_SCRUTINEE"));
    }

    [Fact]
    public void AnElementBinderUnderOr_IsRefused()
    {
        var diagnostics = BuildRun("var ok = coords is (var a, 2) or (1, var b);");

        Assert.Equal(2, Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED").Count());
    }

    [Fact]
    public void AnElementBinderUnderNot_IsRefused()
    {
        var diagnostics = BuildRun("var ok = coords is not (var a, var b);");

        Assert.Equal(2, Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED").Count());
    }

    [Fact]
    public void ALiftedElementNameUnderNot_IsRefused()
    {
        var diagnostics = BuildRun("var ok = u is not (int x, int y) whole;");

        Assert.Equal(3, Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED").Count());
    }

    [Fact]
    public void AnElementBinderInAConjunction_IsPermitted()
    {
        BindsCleanly("var ok = coords is (1, 2) and (var a, var b);");
    }

    [Fact]
    public void EveryElementBinder_IsReadableInTheGuardedBranch()
    {
        BindsCleanly("if (pair is (var n, var s)) { var probe = $\"{n}{s}\"; }");
    }

    [Fact]
    public void AnElementBinder_IsNotReadableAfterTheIf()
    {
        var diagnostics = BuildRun("var probe = n;", extraPreamble: "if (pair is (var n, var s)) { }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }

    private void BindsCleanly(string statement)
    {
        Assert.Empty(BuildRun(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}