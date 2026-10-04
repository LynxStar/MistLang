// Tier 3 (Component): what a member named through an instantiation binds to. A static resolves with the
// instantiation's substitution, so `Box<int>.Make(3)` is an int, and Standard's combinators resolve at
// the instantiation named rather than at the alias's void. A nested type is carried UNDER the
// instantiation -- `Box<int>.Wrapper` is a construction of the nested definition enclosed by `Box<int>`,
// distinct from `Box<string>.Wrapper`, with Box's T bound in its members -- and the same type whether
// inferred from a member or written in a type position. See ProgramModelProbe for the harness.

using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
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

            public class Held<U>
            {
            }
        }

        public class Derived<T> : Base<T>
        {
        }
        """;

    private const string Derivation = """
        public class Swarm : Box<string>
        {
            public Pair<int> Made() { return new Pair<int>(); }

            public int Tagged() { var tagged = Pair<int>.Tag("s"); return tagged.Length; }
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

            public class Pair<U>
            {
                public static T Tag(T value) { return value; }
            }

            public Pair<int> Paired() { return new Pair<int>(); }
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

    [Fact]
    public void ANestedTypeWrittenInTypePositionIsTheCarriedType()
    {
        var declaration = DeclarationIn("Box<int>.Wrapper w = Box<int>.Wrapper.Make();", extraTypes: "");

        Assert.Equal("Box<int>.Wrapper", TypeDisplay.Mist(declaration.Local.Type));
        Assert.Same(declaration.Initializer!.Type, declaration.Local.Type);
    }

    [Fact]
    public void ANestedTypeInTypePositionTargetsANew()
    {
        var creation = InitializerOf<NewExpression>("Box<string>.Wrapper w = new();");

        Assert.Equal("Box<string>.Wrapper", TypeDisplay.Mist(creation.Type));
    }

    [Fact]
    public void ANestedTypeOnAGenericBaseIsNamedThroughTheDeriverInTypePosition()
    {
        var declaration = DeclarationIn("Derived<int>.Inner i = new();", GenericBase);

        Assert.Equal("Base<int>.Inner", TypeDisplay.Mist(declaration.Local.Type));
        Assert.Same(declaration.Initializer!.Type, declaration.Local.Type);
    }

    [Fact]
    public void AGenericNestedTypeWrittenInTypePositionIsTheCarriedType()
    {
        var declaration = DeclarationIn("Box<string>.Pair<int> p = new Box<string>().Paired();", extraTypes: "");
        var type = Assert.IsType<ConstructedType>(declaration.Local.Type);

        Assert.Equal("Box<string>.Pair<int>", TypeDisplay.Mist(type));
        Assert.Equal("Box<string>", TypeDisplay.Mist(type.Enclosing!));
        Assert.Same(declaration.Initializer!.Type, type);
    }

    [Fact]
    public void AGenericNestedTypeOnAGenericBaseIsNamedThroughTheDeriver()
    {
        var declaration = DeclarationIn("Derived<int>.Held<string> h = new();", GenericBase);

        Assert.Equal("Base<int>.Held<string>", TypeDisplay.Mist(declaration.Local.Type));
    }

    [Fact]
    public void AGenericNestedTypeNamedInADerivationIsCarriedUnderTheBase()
    {
        var body = BodyIn("var inherited = new Swarm().Made();\nvar direct = new Box<string>().Paired();", Derivation);
        var types = body.Statements.OfType<LocalDeclarationStatement>().Select(declaration => declaration.Local.Type).ToList();

        Assert.Equal("Box<string>.Pair<int>", TypeDisplay.Mist(types[0]));
        Assert.Same(types[1], types[0]);
    }

    [Fact]
    public void AStaticReadThroughAGenericNestedTypeInADerivationIsSubstituted()
    {
        var model = BuildModel("int unused = 1;", Derivation, extraPreamble: "", out var diagnostics);
        var tagged = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Swarm"]).MethodsList.Single(method => method.Name == "Tagged");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(
            WellKnownTypes.String.FullyQualifiedName,
            tagged.Body!.Statements.OfType<LocalDeclarationStatement>().Single().Local.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Box<int>.Wrapper? maybe = Box<int>.Wrapper.Make();", "Box<int>.Wrapper | void")]
    [InlineData("Box<int>.Wrapper[] many = [Box<int>.Wrapper.Make()];", "Box<int>.Wrapper[]")]
    public void ANestedTypeTakesTheSuffixesANamedOneTakes(string statement, string display)
    {
        Assert.Equal(display, TypeDisplay.Mist(SingleLocal(statement).Type));
    }

    [Fact]
    public void ANestedNameNothingDeclaresIsRefusedAsAType()
    {
        var diagnostics = BuildRun("Box<int>.Missing m = Box<int>.Wrapper.Make();");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
    }

    private const string Projections = """
        public class Sized<N:int> { }

        type Seven = Sized<7>;

        public class Fixed : Sized<7> { }

        public class Forwarding<M:short> : Sized<M> { }
        """;

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("var n = Sized<7>.N;", 0)]
    [InlineData("var n = Seven.N;", 0)]
    [InlineData("var n = Fixed.N;", 1)]
    [InlineData("var n = Forwarding<3>.N;", 1)]
    public void ADependentValueIsReadThroughTheProjectionOfTheTypeDeclaringIt(string statement, int throughBases)
    {
        var access = InitializerOf<MemberAccessExpression>(statement, Projections);
        var property = Assert.IsType<PropertyResult>(access.Member);

        Assert.Equal("N", property.Property.ProjectedParameter?.Name);
        Assert.Equal("Sized", TypeDisplay.Mist(property.Property.DeclaringType!));
        Assert.Equal(throughBases, property.BaseConstructions.Count);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, access.Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ADependentValueIsNotAnInstanceMember()
    {
        var diagnostics = BuildRun("var s = new Sized<7>(); var n = s.N;", Projections);

        Assert.Contains("'Sized<7>' has no member named 'N'", Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER")).Message, StringComparison.Ordinal);
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