using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 3: what a pack resolves to in the type positions that are not a bare spread (variadics
// Requirements 7.7, 7.8 and 9.4). `(TArgs...)` is the one-slot tuple of the pack wherever a type is
// written, so a parameter declared with it takes one tuple and never a flat argument list.
[Trait("Tier", "3")]
public sealed class PackTypePositionTests : ProgramModelProbe
{
    public PackTypePositionTests()
        : base("MistPackTypePosition_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Plain<params TArgs>
        {
            public (TArgs...) Whole((TArgs...) kept)
            {
                (TArgs...) copy = kept;
                return copy;
            }

            public int Take((TArgs...) whole) { return TArgs.Length; }
            public int Both(params TArgs flat, (TArgs...) whole) { return TArgs.Length; }
            public int Relay(params TArgs args) { return this.Take(args...); }
        }
        """;

    protected override string Preamble { get; } = "var plain = new Plain<int, string>();";

    [Fact]
    public void AParenthesizedSpread_IsTheTupleOfThePackInEveryTypePosition()
    {
        var plain = Declared();
        var whole = plain.MethodsList.Single(method => method.Name == "Whole");
        var pack = plain.TypeParametersList[0];
        var copy = Assert.Single(whole.Body!.Statements.OfType<LocalDeclarationStatement>()).Local;

        AssertTupleOf(pack, whole.ParametersList[0].Type);
        AssertTupleOf(pack, whole.ReturnType);
        AssertTupleOf(pack, copy.Type);
        Assert.False(whole.ParametersList[0].IsFlattened);
    }

    [Theory]
    [InlineData("var n = plain.Take((2, \"x\"));", 1)]
    [InlineData("var n = plain.Both(1, \"x\", (2, \"y\"));", 3)]
    public void ATupleParameterOverAPack_TakesOneTupleOfThePacksElements(string statement, int arguments)
    {
        var call = InitializerOf<InvocationExpression>(statement);
        var slot = Assert.IsType<TupleType>(call.ParameterTypes![^1], exactMatch: false);

        Assert.Equal(arguments, call.Arguments.Count);
        Assert.Equal(["Int32", "String"], slot.ElementTypes.Select(element => element.Name));
    }

    [Theory]
    [InlineData("var n = plain.Take(2, \"x\");")]
    [InlineData("var n = plain.Both(1, \"x\", 2, \"y\");")]
    public void ATupleParameterOverAPack_TakesNoFlatArgumentList(string statement)
    {
        _ = Assert.Single(Errors(BuildRun(statement), "MIST_PM_NO_APPLICABLE_OVERLOAD"));
    }

    [Fact]
    public void ASpreadForwardedIntoATupleParameterOverTheSamePack_IsTheTupleItself()
    {
        var plain = Declared();
        var relay = plain.MethodsList.Single(method => method.Name == "Relay");
        var returned = Assert.Single(relay.Body!.Statements.OfType<ReturnStatement>()).Value;
        var argument = Assert.Single(Assert.IsType<InvocationExpression>(returned).Arguments);

        Assert.IsNotType<SpreadExpression>(argument);
        AssertTupleOf(plain.TypeParametersList[0], argument.Type);
    }

    private static void AssertTupleOf(TypeParameter pack, IMistType type)
    {
        var element = Assert.Single(Assert.IsType<TupleType>(type).ElementTypes);

        Assert.Same(pack, Assert.IsType<SpreadType>(element).Operand);
    }

    private ClassType Declared()
    {
        var model = BuildModel("", "", "", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return Assert.IsType<ClassType>(model.TypesByFQN["Probe.Plain<>"]);
    }
}