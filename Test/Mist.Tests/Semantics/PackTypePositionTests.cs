using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 3: what a pack resolves to in the type positions that are not a bare spread (variadics
// Requirements 6.4 to 6.6, 7.7, 7.8 and 9.4). `(TArgs...)` is the one-slot tuple of the pack wherever a
// type is written, so a parameter declared with it takes one tuple and never a flat argument list;
// `TArgs[i]` is one element, and only of a pack whose declared size holds the index.
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

        public class Indexed<Type[2]:TArgs>
        {
            public TArgs[0] First;
            public TArgs[1] Second;

            public Indexed(TArgs[0] first, TArgs[1] second) { First = first; Second = second; }

            public TArgs[1] Copy()
            {
                TArgs[1] copy = Second;
                return copy;
            }
        }
        """;

    protected override string Preamble { get; } = """
        var plain = new Plain<int, string>();
        var indexed = new Indexed<int, string>(7, "x");
        """;

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

    [Fact]
    public void AnIndexIntoAFixedSizePack_IsOneElementTypeInEveryTypePosition()
    {
        var model = BuildModel("", "", "", out var diagnostics);
        var indexed = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Indexed<>"]);
        var copy = indexed.MethodsList.Single(method => method.Name == "Copy");
        var second = Assert.IsType<PackElementType>(indexed.FieldsList[1].Type);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Same(indexed.TypeParametersList[0], second.Pack);
        Assert.Equal(1, second.Index);
        Assert.Same(second, indexed.ConstructorsList[0].ParametersList[1].Type);
        Assert.Same(second, copy.ReturnType);
        Assert.Same(second, Assert.Single(copy.Body!.Statements.OfType<LocalDeclarationStatement>()).Local.Type);
        Assert.NotSame(second, indexed.FieldsList[0].Type);
    }

    [Theory]
    [InlineData("var first = indexed.First;", "Int32")]
    [InlineData("var second = indexed.Second;", "String")]
    [InlineData("var copy = indexed.Copy();", "String")]
    public void AnIndexedElement_IsTheTypeTheClosingPutAtThatIndex(string statement, string type)
    {
        Assert.Equal(type, SingleLocal(statement).Type.Name);
    }

    [Theory]
    [InlineData("public class Bad<params TArgs> { public void Use(TArgs[0] first) { } }", "MIST_VAR_0003")]
    [InlineData("public class Bad<Type[]:TArgs> { public TArgs[3] Field; }", "MIST_VAR_0003")]
    [InlineData("public class Bad { public static int Use<params T>(T[0] first) { return 0; } }", "MIST_VAR_0003")]
    [InlineData("public class Bad<Type[2]:TArgs> { public void Use(TArgs[2] third) { } }", "MIST_VAR_0004")]
    [InlineData("public class Bad<Type[2]:TArgs> { public TArgs[5] Field; }", "MIST_VAR_0004")]
    [InlineData("public class Bad<Type[0]:TArgs> { public void Use(TArgs[0] first) { } }", "MIST_VAR_0004")]
    public void AnIndexThePacksDeclaredSizeDoesNotHold_IsRefused(string declaration, string id)
    {
        var diagnostics = BuildRun("", declaration);

        _ = Assert.Single(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        _ = Assert.Single(Errors(diagnostics, id));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    public void AnIndexInsideTheDeclaredSize_IsAdmitted(int size, int index)
    {
        var diagnostics = BuildRun("", $"public class Good<Type[{size}]:TArgs> {{ public void Use(TArgs[{index}] element) {{ }} }}");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
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