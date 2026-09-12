// Tier 3 (Component): what a member named through an instantiation binds to. A static resolves with the
// instantiation's substitution, so `Box<int>.Make(3)` is an int, and Standard's combinators resolve at
// the instantiation named rather than at the alias's void. A nested type does not resolve at all: the
// model holds no nested type of an instantiation, and binding to the definition would emit C# naming
// type parameters that are not in scope. See ProgramModelProbe for the harness.

using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ConstructedGenericMemberAccessTests : ProgramModelProbe
{
    private const string RefusalId = "MIST_PM_NESTED_TYPE_THROUGH_INSTANTIATION";

    public ConstructedGenericMemberAccessTests()
        : base("MistConstructedAccess_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Box<T>
        {
            public static string Describe() { return "box"; }

            public static T Make(T value) { return value; }

            public class Wrapper
            {
                public static string Info() { return "wrapper"; }
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AStaticThroughAnInstantiationBindsToTheMember()
    {
        var call = InitializerOf<InvocationExpression>("var described = Box<int>.Describe();");

        Assert.Equal("Describe", call.Method?.Name);
        Assert.Equal(WellKnownTypes.String.FullyQualifiedName, call.Type.FullyQualifiedName);
    }

    [Fact]
    public void TheInstantiationSubstitutesIntoTheMember()
    {
        var call = InitializerOf<InvocationExpression>("var made = Box<int>.Make(3);");

        Assert.Equal("Make", call.Method?.Name);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, call.Type.FullyQualifiedName);
    }

    [Fact]
    public void ANestedTypeThroughAnInstantiationIsRefused()
    {
        var diagnostics = BuildRun("var info = Box<int>.Wrapper.Info();");

        _ = Assert.Single(Errors(diagnostics, RefusalId));
        _ = Assert.Single(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void AStandardCombinatorResolvesAtTheInstantiationNamed()
    {
        var model = StandardModelProbe.Build(
            "Task<int>[] pending = new Task<int>[0];\nvar all = Task<int>.WhenAll(pending);", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var declaration = run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();
        var awaited = Assert.IsType<AwaitExpression>(declaration.Initializer);
        var call = Assert.IsType<InvocationExpression>(awaited.Operand);

        Assert.Equal("WhenAll", call.Method?.Name);
        Assert.Equal("Task<int>[]", TypeDisplay.Mist(call.ParameterTypes![0]));
        Assert.Equal("int[]", TypeDisplay.Mist(awaited.Type));
    }
}