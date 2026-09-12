// Tier 3 (Component): what a member named through an instantiation binds to. A static resolves with the
// instantiation's substitution, so `Box<int>.Make(3)` is an int, and Standard's combinators resolve at
// the instantiation named rather than at the alias's void. A nested type is carried UNDER the
// instantiation -- `Box<int>.Wrapper` is a construction of the nested definition enclosed by `Box<int>`,
// distinct from `Box<string>.Wrapper`, with Box's T bound in its members. See ProgramModelProbe for the
// harness.

using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ConstructedGenericMemberAccessTests : ProgramModelProbe
{
    private const string GenericBase = """
        public class Base<T>
        {
            public class Inner
            {
                public static T Pick(T value) { return value; }
            }
        }

        public class Derived<T> : Base<T>
        {
        }
        """;

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

                public static T Echo(T value) { return value; }

                public static Wrapper Make() { return new Wrapper(); }
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
    public void ANestedTypeThroughAnInstantiationIsCarriedUnderIt()
    {
        var call = InitializerOf<InvocationExpression>("var info = Box<int>.Wrapper.Info();");

        Assert.Equal("Info", call.Method?.Name);
        Assert.Equal("Box<int>.Wrapper", TypeDisplay.Mist(ReceiverTypeOf(call)));
        Assert.Equal("Box<int>", TypeDisplay.Mist(ReceiverTypeOf(call).Enclosing!));
    }

    [Fact]
    public void TheInstantiationSubstitutesIntoTheNestedTypesMembers()
    {
        var call = InitializerOf<InvocationExpression>("var echoed = Box<int>.Wrapper.Echo(3);");

        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, call.Type.FullyQualifiedName);
    }

    [Fact]
    public void ANestedTypeReturnedByAMemberIsCarriedUnderTheReceiver()
    {
        var call = InitializerOf<InvocationExpression>("var made = Box<int>.Wrapper.Make();");

        Assert.Equal("Box<int>.Wrapper", TypeDisplay.Mist(call.Type));
        Assert.Same(ReceiverTypeOf(call), call.Type);
    }

    [Fact]
    public void TwoInstantiationsCarryTwoNestedTypes()
    {
        var body = BodyIn("var a = Box<int>.Wrapper.Info();\nvar b = Box<string>.Wrapper.Info();\nvar c = Box<int>.Wrapper.Info();");
        var receivers = body.Statements
            .OfType<LocalDeclarationStatement>()
            .Select(declaration => ReceiverTypeOf(Assert.IsType<InvocationExpression>(declaration.Initializer)))
            .ToList();

        Assert.NotSame(receivers[0], receivers[1]);
        Assert.Same(receivers[0], receivers[2]);
    }

    /// <summary>
    /// The nested type is found on a base, so the instantiation reaches it only through the base's
    /// own construction -- the same composition an inherited field's type needs.
    /// </summary>
    [Fact]
    public void ANestedTypeDeclaredOnAGenericBaseIsCarriedThroughTheBase()
    {
        var call = InitializerOf<InvocationExpression>("var picked = Derived<int>.Inner.Pick(4);", GenericBase);

        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, call.Type.FullyQualifiedName);
        Assert.Equal("Base<int>.Inner", TypeDisplay.Mist(ReceiverTypeOf(call)));
    }

    private static ConstructedType ReceiverTypeOf(InvocationExpression call)
    {
        var access = Assert.IsType<MemberAccessExpression>(call.Receiver);
        var reference = Assert.IsType<TypeReferenceExpression>(access.Receiver);

        return Assert.IsType<ConstructedType>(reference.ReferencedType);
    }

    [Fact]
    public void AStandardCombinatorResolvesAtTheInstantiationNamed()
    {
        var model = StandardModelProbe.Build(
            "Task<int>[] pending = new Task<int>[0];\nvar all = Task<int>.WhenAll(pending);", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var declaration = run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();
        var dropped = Assert.IsType<InvocationExpression>(declaration.Initializer);
        var call = Assert.IsType<InvocationExpression>(TaskResult.Dropped(dropped));

        Assert.Equal("WhenAll", call.Method?.Name);
        Assert.Equal("Task<int>[]", TypeDisplay.Mist(call.ParameterTypes![0]));
        Assert.Equal("int[]", TypeDisplay.Mist(dropped.Type));
    }
}