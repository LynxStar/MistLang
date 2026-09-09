// Tier 3 (Component): the parameter a `where T : default` declaration carries and the argument its
// call sites fill it with, over a real ProgramModel. No Roslyn and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DefaultConstraintWitnessTests : ProgramModelProbe
{
    public DefaultConstraintWitnessTests() : base("mist-default-witness-")
    {
    }

    protected override string Fixtures => "";

    protected override string Preamble => "";

    [Fact]
    public void ConstrainedParameter_DeclaresAFactoryParameter()
    {
        Assert.Equal("T Create<T>(System.Func<T> __default_T)", Signature("Create"));
    }

    [Fact]
    public void Witness_ComesAheadOfTheDeclaredParameters()
    {
        Assert.Equal("string Tag<T>(System.Func<T> __default_T, string label)", Signature("Tag"));
    }

    [Fact]
    public void UnconstrainedMethod_CarriesNoWitness()
    {
        Assert.Equal(
            "int Plain<T>(T value)",
            Signature("Plain", "public static int Plain<T>(T value) { return 1; }"));
    }

    [Fact]
    public void DefaultOfAConstrainedParameter_CallsTheWitness()
    {
        Assert.Contains("return __default_T();", Body("Create"), StringComparison.Ordinal);
    }

    [Fact]
    public void CallSite_FillsTheWitnessFromTheTypeArgument()
    {
        Assert.Contains(
            "Probe.Factory.Create<Probe.Point>(() => Probe.Point.__CreateDefault())",
            CallBody("var p = Factory.Create<Point>();"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void IntrinsicTypeArgument_IsWitnessedByItsLiteralDefault()
    {
        Assert.Contains(
            "Create<int>(() => default(int))",
            CallBody("var i = Factory.Create<int>();"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForwardingCall_PassesItsOwnWitnessThrough()
    {
        Assert.Contains("Create<T>(() => __default_T())", Body("Forward"), StringComparison.Ordinal);
    }

    [Fact]
    public void CallWithDeclaredArguments_SeparatesTheWitnessFromThem()
    {
        Assert.Contains(
            """Tag<int>(() => default(int), "x")""",
            CallBody("""var t = Factory.Tag<int>("x");"""),
            StringComparison.Ordinal);
    }

    private string Signature(string methodName, string extraMembers = "")
    {
        return MethodEmitter.EmitSignature(MethodNamed(methodName, extraMembers), new DiagnosticBag());
    }

    private string Body(string methodName)
    {
        var method = MethodNamed(methodName);
        var writer = new CSharpWriter();

        Assert.NotNull(method.Body);
        BodyEmitter.EmitBlock(method.Body, writer, new DiagnosticBag());

        return writer.ToString();
    }

    private string CallBody(string statement)
    {
        var writer = new CSharpWriter();

        BodyEmitter.EmitBlock(BodyIn(statement, Fixture()), writer, new DiagnosticBag());

        return writer.ToString();
    }

    private Method MethodNamed(string methodName, string extraMembers = "")
    {
        var model = Build($"namespace Probe;\n\n{Fixture(extraMembers)}\n", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var factory = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Factory"]);
        return factory.MethodsList.Single(method => method.Name == methodName);
    }

    private static string Fixture(string extraMembers = "")
    {
        return $$"""
            public class Point { public int X; }

            public class Factory
            {
                public static T Create<T>() where T : default { return default(T); }

                public static string Tag<T>(string label) where T : default { return label; }

                public static T Forward<T>() where T : default { return Create<T>(); }

                {{extraMembers}}
            }
            """;
    }
}