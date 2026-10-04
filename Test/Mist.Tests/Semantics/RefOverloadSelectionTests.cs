using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Selecting between a by-ref and a by-value overload of one name. Asserted on the RECORDED choice
/// rather than on output: emission re-writes the call the way source
/// spelled it and lets Roslyn resolve it again, so a program printing the right answer is equally
/// consistent with Mist having chosen nothing. The model is the only place the choice can be seen.
/// <para>
/// Before this, both calls bound to the by-value candidate and the program printed its answer twice
/// with zero diagnostics -- a wrong answer, not a refusal. Two separate faults produced it: the
/// candidate set keyed a signature on parameter TYPES alone, so the two overloads collapsed into one
/// entry before selection ran at all; and applicability compared types without passing mode, so it
/// could not have told them apart either.
/// </para>
/// </summary>
public sealed class RefOverloadSelectionTests : ProgramModelProbe
{
    public RefOverloadSelectionTests()
        : base("MistRefOverload_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Ov
        {
            public static int Pick(int v)
            {
                return 1;
            }

            public static int Pick(ref int v)
            {
                return 2;
            }

            public static int Only(ref int v)
            {
                return 3;
            }
        }
        """;

    protected override string Preamble { get; } = "int arg = 1;";

    private bool SelectedIsByRef(string call)
    {
        var invocation = InitializerOf<InvocationExpression>($"var chosen = {call};");
        Assert.NotNull(invocation.Method);
        return invocation.Method!.ParametersList[0].Modifier.IsRef;
    }

    [Fact]
    public void ValueArgument_SelectsTheByValueOverload()
    {
        Assert.False(SelectedIsByRef("Ov.Pick(arg)"));
    }

    [Fact]
    public void RefArgument_SelectsTheByRefOverload()
    {
        Assert.True(SelectedIsByRef("Ov.Pick(ref arg)"));
    }

    /// <summary>
    /// A lone candidate is returned without consulting applicability at all, so mode matching cannot
    /// reach it. Pinned because it is the reason this change could not regress a single-candidate
    /// call, which is every by-ref call site in the banked suite.
    /// </summary>
    [Fact]
    public void SoleByRefCandidate_IsStillSelected()
    {
        Assert.True(SelectedIsByRef("Ov.Only(ref arg)"));
    }
}