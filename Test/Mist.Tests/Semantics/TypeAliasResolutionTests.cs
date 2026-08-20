// Alias transparency, chain completion, cycle detection and constructible aliases, asserted on
// the MODEL -- an alias that leaked nominal identity would still print the right answers, so only
// the model can prove erasure. Keeps orders 84-85 of
// backlog/emitter-v2-capability-progression.yaml honest.

using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class TypeAliasResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public type UserId = int;

        public type Chained = Middle;
        public type Middle = int;

        public type IntBox = GenericBox<int>;
        public type Box<T> = GenericBox<T>;

        public class GenericBox<T>
        {
            public T value;

            public GenericBox(T v)
            {
                this.value = v;
            }
        }

        public class Sink
        {
            public static int Take(int value) { return 1; }
            public static int Take(string value) { return 2; }
        }
        """;

    public TypeAliasResolutionTests()
        : base("MistAlias_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        UserId id = 42;
        int plain = 7;
        """;

    [Fact]
    public void AnAliasTypedLocal_IsTheUnderlyingTypeItself()
    {
        var locals = Locals("int witness = 0;");

        Assert.Same(TypeOf(locals, "plain"), TypeOf(locals, "id"));
        Assert.Equal("Standard.Int32", TypeOf(locals, "id").FullyQualifiedName);
    }

    /// <summary>
    /// TWO candidates, so applicability actually runs: with one, OverloadResolution returns it
    /// unexamined and a nominally-distinct alias would still "resolve".
    /// </summary>
    [Fact]
    public void AnAliasArgument_SelectsTheUnderlyingOverload()
    {
        var invocation = InitializerOf<InvocationExpression>("var chosen = Sink.Take(id);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int32", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnAliasChain_BottomsOutRegardlessOfDeclarationOrder()
    {
        // Chained is declared BEFORE Middle in the fixtures, so this expansion only bottoms
        // out if the builder completes the later alias on demand.
        var locals = Locals("Chained c = 7;");

        Assert.Equal("Standard.Int32", TypeOf(locals, "c").FullyQualifiedName);
    }

    [Fact]
    public void AnAliasCycle_ReportsExactlyOnce()
    {
        var diagnostics = BuildRun(
            "int ok = 1;",
            extraTypes: """
                type CycleA = CycleB;
                type CycleB = CycleA;
                """);

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_CYCLIC_TYPE_ALIAS"));
    }

    [Fact]
    public void ASelfCycle_ReportsExactlyOnce()
    {
        var diagnostics = BuildRun("int ok = 1;", extraTypes: "type SelfLoop = SelfLoop;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_CYCLIC_TYPE_ALIAS"));
    }

    [Fact]
    public void AGenericAliasReference_IsTheSameConstructedTypeAsTheTarget()
    {
        var locals = Locals(
            "Box<string> viaAlias = new GenericBox<string>(\"a\");",
            extraPreamble: "GenericBox<string> direct = new GenericBox<string>(\"b\");");

        Assert.Same(TypeOf(locals, "direct"), TypeOf(locals, "viaAlias"));
    }

    [Fact]
    public void AClosedGenericAlias_IsConstructible()
    {
        var creation = InitializerOf<NewExpression>("var boxed = new IntBox(42);");

        Assert.NotNull(creation.Constructor);
        Assert.Equal("Probe.GenericBox<Int32>", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AGenericAlias_IsConstructibleWithTypeArguments()
    {
        var creation = InitializerOf<NewExpression>("var boxed = new Box<string>(\"m\");");

        Assert.NotNull(creation.Constructor);
        Assert.Equal("Probe.GenericBox<String>", creation.Type.FullyQualifiedName);
    }

    // ── Harness ──────────────────────────────────────────────────────────

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