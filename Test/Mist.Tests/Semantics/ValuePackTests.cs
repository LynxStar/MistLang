using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: the value side of a pack -- a flat argument list collapsing into one parameter slot, and
/// the spread that puts one back.
/// <para>
/// ARITY STOPS BEING A NUMBER AND BECOMES A FLOOR, which is what every assertion here turns on.
/// Overload resolution filtered candidates by an exact count, so a method taking a pack matched
/// only at the one arity where its slots happened to equal its arguments -- and a spread, being one
/// expression, counted as one argument however many it stood for.
/// </para>
/// </summary>
public sealed class ValuePackTests : ProgramModelProbe
{
    public ValuePackTests()
        : base("MistValuePack_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Calls
        {
            public static int Describe<params TArgs>(params TArgs args) { return TArgs.Length; }
            public static int Middle<TFirst, params TMid, TLast>(TFirst f, params TMid m, TLast l) { return TMid.Length; }
            public static int Add2(int a, int b) { return a + b; }
        }
        """;

    protected override string Preamble { get; } = "";

    private ImmutableArray<Diagnostic> Run(string statement)
    {
        return BuildRun(statement);
    }

    [Theory]
    [InlineData("var n = Calls.Describe(1, \"x\", true);")]
    [InlineData("var n = Calls.Describe(1);")]
    [InlineData("var n = Calls.Describe();")]
    public void FlatArgumentsCollapseIntoThePackSlot(string statement)
    {
        Assert.Empty(Run(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void FixedParametersBindInwardFromBothEnds()
    {
        Assert.Empty(
            Run("var n = Calls.Middle(1, \"a\", \"b\", true);")
                .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// The floor still holds: a pack absorbing nothing does not excuse an unfilled fixed slot.
    /// </summary>
    [Fact]
    public void TooFewForTheFixedParameters_IsStillRefused()
    {
        _ = Assert.Single(Errors(Run("var n = Calls.Middle(1);"), "MIST_PM_NO_APPLICABLE_OVERLOAD"));
    }

    [Fact]
    public void TupleSpread_ExpandsIntoArgumentSlots()
    {
        var body = BodyIn("var pair = (10, 20); var sum = Calls.Add2(pair...);");
        var call = body.Statements
            .OfType<LocalDeclarationStatement>()
            .Select(d => d.Initializer)
            .OfType<InvocationExpression>()
            .Single();

        // Two arguments from one written expression, which is the whole of what a spread does.
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void SpreadOfSomethingWithNoKnownLength_IsRefused()
    {
        _ = Assert.Single(
            Errors(Run("var n = 5; var s = Calls.Add2(n...);"), "MIST_VAR_0011"));
    }
}