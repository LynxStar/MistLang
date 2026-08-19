// Union canonicalization, widening and the optional representation, asserted on the MODEL --
// emitted output cannot distinguish a chosen conversion from Roslyn re-deriving one, so these
// are what keeps orders 76-78 of backlog/emitter-v2-capability-progression.yaml honest.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class UnionTypeResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public type Maybe<T> = T | void;

        public class Pick
        {
            public static int | string Choose(bool useInt)
            {
                if (useInt)
                {
                    return 1;
                }
                return "s";
            }
        }

        public class Sink
        {
            public static int Take(int | string value) { return 1; }
            public static int Take(Pick marker) { return 2; }

            public static int Exact(int value) { return 1; }
            public static int Exact(int | string value) { return 2; }

            public static int Wide(int | string | bool value) { return 1; }
            public static int Wide(Pick marker) { return 2; }

            public static int Opt(Maybe<int> value) { return 1; }
            public static int Opt(Pick marker) { return 2; }
        }
        """;

    public UnionTypeResolutionTests()
        : base("MistUnion_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        int | string u = 42;
        byte small = 5;
        """;

    [Fact]
    public void SpellingOrder_DoesNotAffectUnionIdentity()
    {
        var locals = Locals("string | int b = \"x\";", "int | string a = 1;");

        Assert.Same(TypeOf(locals, "a"), TypeOf(locals, "b"));
        Assert.Equal("Standard.Int32 | Standard.String", TypeOf(locals, "a").FullyQualifiedName);
    }

    [Fact]
    public void ANestedUnion_FlattensIntoItsConstituents()
    {
        var locals = Locals("(int | string) | bool c = true;");
        var union = Assert.IsAssignableFrom<IUnionType>(TypeOf(locals, "c"));

        Assert.Equal(3, union.Constituents.Count);
        Assert.Equal("Standard.Boolean | Standard.Int32 | Standard.String", union.FullyQualifiedName);
    }

    [Fact]
    public void ADuplicateConstituent_CollapsesToTheBareType()
    {
        var locals = Locals("int | int d = 1;");

        Assert.Equal("Standard.Int32", TypeOf(locals, "d").FullyQualifiedName);
    }

    /// <summary>
    /// Every overload test here supplies TWO candidates: with one, OverloadResolution returns
    /// it without running applicability, and the conversion under test would be invisible.
    /// </summary>
    [Fact]
    public void AConstituentArgument_MakesTheUnionOverloadApplicable()
    {
        var invocation = SingleInvocation("var chosen = Sink.Take(42);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int32 | Standard.String", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnExactOverload_BeatsTheUnionOverload()
    {
        var invocation = SingleInvocation("var chosen = Sink.Exact(42);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int32", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void NumericWidening_ReachesIntoTheConstituentTest()
    {
        var invocation = SingleInvocation("var chosen = Sink.Take(small);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int32 | Standard.String", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void AUnion_WidensIntoAUnionCoveringItsConstituents()
    {
        var invocation = SingleInvocation("var chosen = Sink.Wide(u);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(
            "Standard.Boolean | Standard.Int32 | Standard.String", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void Var_InfersTheUnionReturnType()
    {
        var local = SingleLocal("var inferred = Pick.Choose(true);");
        var union = Assert.IsAssignableFrom<IUnionType>(local.Type);

        Assert.Equal("Standard.Int32 | Standard.String", union.FullyQualifiedName);
    }

    [Fact]
    public void TheOptionalSuffix_DesugarsToTheUnionWithVoid()
    {
        var locals = Locals("int? x = 3;");

        Assert.Equal("Standard.Int32 | Standard.Void", TypeOf(locals, "x").FullyQualifiedName);
    }

    [Fact]
    public void TheGenericSpelling_IsTheSameTypeAsTheSuffix()
    {
        var locals = Locals("Maybe<int> q = 3;", "int? p = 3;");

        Assert.Same(TypeOf(locals, "p"), TypeOf(locals, "q"));
    }

    [Fact]
    public void AnOptionalArgument_SelectsTheGenericAliasOverload()
    {
        var invocation = SingleInvocation("var chosen = Sink.Opt(x);", extraPreamble: "int? x = 3;");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int32 | Standard.Void", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void TheCSharpSpelling_SplitsOptionalsByConstituentKind()
    {
        var locals = Locals(
            "int | string carried = 1;",
            """
            int? valueKind = 3;
            string? referenceKind = "x";
            """);

        Assert.Equal("int?", TypeNameEmitter.Emit(TypeOf(locals, "valueKind")));
        Assert.Equal("string", TypeNameEmitter.Emit(TypeOf(locals, "referenceKind")));
        Assert.Equal("Union_Int32_String", TypeNameEmitter.Emit(TypeOf(locals, "carried")));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private InvocationExpression SingleInvocation(string statement, string extraPreamble = "")
    {
        return InitializerOf<InvocationExpression>(statement, extraTypes: "", extraPreamble);
    }

    private IReadOnlyList<LocalDeclarationStatement> Locals(string statement, string extraPreamble = "")
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble, out _);
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(m => m.Name == "Run");

        Assert.NotNull(run.Body);
        return run.Body.Statements.OfType<LocalDeclarationStatement>().ToList();
    }

    private static IMistType TypeOf(IReadOnlyList<LocalDeclarationStatement> locals, string name)
    {
        return locals.Single(local => local.Local.Name == name).Local.Type;
    }
}