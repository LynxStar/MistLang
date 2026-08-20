// Newtype nominal distinctness, member forwarding and construction forwarding, asserted on the
// MODEL. The Newtypes sample cannot prove distinctness -- it never mixes a newtype with its
// source, so a fully TRANSPARENT alias would pass every line of it. The negative cases here are
// what keeps order 86 of backlog/emitter-v2-capability-progression.yaml honest.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class NewtypeResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public type UserId = new int;
        public type OrderId = new int;
        public type Position = new Point;
        public type Actor = new Entity;

        public struct Point
        {
            public int X;
            public int Y;

            public Point(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }

            public string Describe()
            {
                return "pt";
            }
        }

        public class Entity
        {
            private string name;

            public Entity(string name)
            {
                this.name = name;
            }

            public string GetName()
            {
                return this.name;
            }
        }

        public class Sink
        {
            public static int Take(int value) { return 1; }
            public static int Take(string value) { return 2; }

            public static int TakeId(UserId id) { return 1; }
            public static int TakeId(string s) { return 2; }
        }
        """;

    public NewtypeResolutionTests()
        : base("MistNewtype_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        UserId uid = (UserId)42;
        Position pos = new Position(3, 7);
        Actor actor = new Actor("Hero");
        """;

    [Fact]
    public void ANewtypeTypedLocal_KeepsItsNominalIdentity()
    {
        var locals = Locals("int witness = 0;");

        var uidType = TypeOf(locals, "uid");
        _ = Assert.IsType<Newtype>(uidType);
        Assert.Equal("Probe.UserId", uidType.FullyQualifiedName);
    }

    [Fact]
    public void IsWithin_RefusesEveryNewtypeSourceMixing()
    {
        var model = BuildModel("int ok = 1;", extraTypes: "", extraPreamble: "", out _);
        var userId = Assert.IsType<Newtype>(model.TypesByFQN["Probe.UserId"]);
        var orderId = Assert.IsType<Newtype>(model.TypesByFQN["Probe.OrderId"]);
        var source = userId.SourceType;

        Assert.NotNull(source);
        Assert.False(ConversionRelation.IsWithin(userId, source));
        Assert.False(ConversionRelation.IsWithin(source, userId));
        Assert.False(ConversionRelation.IsWithin(userId, orderId));
        Assert.False(ConversionRelation.IsWithin(orderId, userId));
        Assert.True(ConversionRelation.IsWithin(userId, userId));
    }

    /// <summary>
    /// TWO candidates, so applicability actually runs: with one, OverloadResolution returns it
    /// unexamined and a transparent newtype would be invisible.
    /// </summary>
    [Fact]
    public void ANewtypeArgument_MatchesNoSourceTypedOverload()
    {
        var diagnostics = BuildRun("var chosen = Sink.Take(uid);");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVABLE_OVERLOAD"));
    }

    [Fact]
    public void ANewtypeArgument_SelectsTheNewtypeTypedOverload()
    {
        var invocation = InitializerOf<InvocationExpression>("var chosen = Sink.TakeId(uid);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Probe.UserId", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void TheValueAccessor_HasTheSourceType()
    {
        var local = SingleLocal("var raw = uid.Value;");

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void AForwardedField_HasTheSourceMemberType()
    {
        var local = SingleLocal("var x = pos.X;");

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void AForwardedMethod_HasTheSourceReturnType()
    {
        var local = SingleLocal("var d = pos.Describe();");

        Assert.Equal("Standard.String", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void AForwardedClassMethod_HasTheSourceReturnType()
    {
        var local = SingleLocal("var n = actor.GetName();");

        Assert.Equal("Standard.String", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void APrivateSourceMember_DoesNotForward()
    {
        var diagnostics = BuildRun("var n = actor.name;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
    }

    [Fact]
    public void ACastToTheNewtype_TypesAsTheNewtype()
    {
        var local = SingleLocal("var u2 = (UserId)7;");

        Assert.Equal("Probe.UserId", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void ACastBackToTheSource_TypesAsTheSource()
    {
        var local = SingleLocal("var raw = (int)uid;");

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void Construction_ForwardsToTheSourceConstructor()
    {
        var creation = InitializerOf<NewExpression>("var p = new Position(1, 2);");

        Assert.NotNull(creation.Constructor);
        Assert.Equal(2, creation.Constructor.ParametersList.Count);
        Assert.Equal("Probe.Position", creation.Type.FullyQualifiedName);
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