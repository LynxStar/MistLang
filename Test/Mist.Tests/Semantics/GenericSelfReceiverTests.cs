// Tier 3 (Component): what `this` binds to inside a generic declaration's own body, asserted on the
// model because the emitted call reads the same whether or not the binder chose anything. A call passing
// `this` selects the same source method as one passing a parameter declared as the self construction,
// with the same closed parameter type; an operator over `this` selects the operator a typed operand does;
// and a pack-generic, nested, struct, constructed-base and abstract owner each type `this` as its own
// construction. See ProgramModelProbe for the harness.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "3")]
public sealed class GenericSelfReceiverTests : ProgramModelProbe
{
    public GenericSelfReceiverTests()
        : base("MistGenericSelfReceiver_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Tag<T>
        {
            public T Held;
            public Tag<T> FromConstructor;

            public Tag(T held) { Held = held; FromConstructor = Same(this); }

            public static Tag<T> Same(Tag<T> other) { return other; }

            public static T operator +(Tag<T> left, Tag<T> right) { return right.Held; }

            public Tag<T> Me() { return Same(this); }

            public Tag<T> Typed(Tag<T> typed) { return Same(typed); }

            public T Twice() { return this + this; }

            public T Mixed(Tag<T> other) { return other + other; }

            public Tag<T> Self { get { return Same(this); } }
        }

        public class Bundle<params Ts>
        {
            public static Bundle<Ts...> Same(Bundle<Ts...> value) { return value; }

            public Bundle<Ts...> Me() { return Same(this); }
        }

        public class Outer<T>
        {
            public class Inner<U>
            {
                public static Inner<U> Same(Inner<U> value) { return value; }

                public Inner<U> Me() { return Same(this); }
            }

            public class Plain
            {
                public static Plain Same(Plain value) { return value; }

                public Plain Me() { return Same(this); }
            }
        }

        public class Base<T>
        {
            public static Base<T> Same(Base<T> value) { return value; }
        }

        public class Child<T> : Base<(T, int)>
        {
            public Base<(T, int)> Me() { return Same(this); }
        }

        public struct Pair<T>
        {
            public static Pair<T> Same(Pair<T> value) { return value; }

            public Pair<T> Me() { return Same(this); }
        }

        public abstract Holder<T>
        {
            T Item();

            T Through() { return Holders.Same(this).Item(); }
        }

        public class Holders
        {
            public static Holder<T> Same<T>(Holder<T> value) { return value; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void ACallPassingThisSelectsWhatATypedParameterSelects()
    {
        var tag = Declared<ClassType>("Probe.Tag<>");
        var typed = SameCallIn(Method(tag, "Typed"));

        var call = SameCallIn(Method(tag, "Me"));

        Assert.Same(typed.Method, call.Method);
        Assert.Same(Assert.Single(typed.ParameterTypes!), Assert.Single(call.ParameterTypes!));
        Assert.Same(Assert.Single(typed.ParameterTypes!), Assert.IsType<ThisExpression>(Assert.Single(call.Arguments)).Type);
    }

    [Fact]
    public void AnOperatorOverThisSelectsWhatATypedOperandSelects()
    {
        var tag = Declared<ClassType>("Probe.Tag<>");
        var mixed = Assert.IsType<BinaryExpression>(Returned(Method(tag, "Mixed")));

        var twice = Assert.IsType<BinaryExpression>(Returned(Method(tag, "Twice")));

        Assert.NotNull(mixed.Operator);
        Assert.Same(mixed.Operator, twice.Operator);
        Assert.Equal(mixed.ParameterTypes, twice.ParameterTypes, ReferenceEqualityComparer.Instance);
        Assert.Same(mixed.ParameterTypes![0], Assert.IsType<ThisExpression>(twice.Left).Type);
    }

    [Fact]
    public void AConstructorAndAnAccessorTypeThisAsAMethodDoes()
    {
        var tag = Declared<ClassType>("Probe.Tag<>");
        var inMethod = ThisIn(Method(tag, "Me").Body!);

        Assert.Same(inMethod.Type, ThisIn(Assert.Single(tag.ConstructorsList).Body!).Type);
        Assert.Same(inMethod.Type, ThisIn(tag.PropertiesList.Single(property => property.Name == "Self").Getter!.Body!).Type);
    }

    [Fact]
    public void ThisInAPackOwnerIsItsSymbolicConstruction()
    {
        var bundle = Declared<ClassType>("Probe.Bundle<>");

        var call = SameCallIn(Method(bundle, "Me"));

        Assert.Same(Assert.Single(call.ParameterTypes!), Assert.IsType<ThisExpression>(Assert.Single(call.Arguments)).Type);
        Assert.Equal("Probe.Bundle<[Ts...]>", Assert.Single(call.Arguments).Type.FullyQualifiedName);
    }

    [Fact]
    public void ThisInANestedGenericIsItsOwnConstructionWithNoEnclosingInstantiation()
    {
        var inner = Declared<ClassType>("Probe.Outer<>.Inner<>");

        var self = Assert.IsType<ConstructedType>(ThisIn(Method(inner, "Me").Body!).Type);

        Assert.Same(inner, self.Definition);
        Assert.Same(inner.TypeParametersList[0], Assert.Single(self.TypeArguments));
        Assert.Null(self.Enclosing);
    }

    [Fact]
    public void ThisInANonGenericNestedTypeIsItsDefinition()
    {
        var plain = Declared<ClassType>("Probe.Outer<>.Plain");

        Assert.Same(plain, ThisIn(Method(plain, "Me").Body!).Type);
    }

    [Fact]
    public void ThisInADeriverReachesAnInheritedMethodThroughItsConstructedBase()
    {
        var model = Model();
        var child = Declared<ClassType>(model, "Probe.Child<>");

        var call = SameCallIn(Method(child, "Me"));

        Assert.Same(Method(Declared<ClassType>(model, "Probe.Base<>"), "Same"), call.Method);
        Assert.Equal("Probe.Base<(T, Int32)>", Assert.Single(call.ParameterTypes!).FullyQualifiedName);
        Assert.Equal("Probe.Child<T>", Assert.Single(call.Arguments).Type.FullyQualifiedName);
    }

    [Fact]
    public void ThisInAGenericStructIsItsStructConstruction()
    {
        var pair = Declared<StructType>("Probe.Pair<>");

        var self = Assert.IsType<ConstructedType>(ThisIn(Method(pair, "Me").Body!).Type);

        Assert.Same(pair, self.Definition);
    }

    [Fact]
    public void ThisInAnAbstractDefaultBodyInfersThroughItsOwnConstruction()
    {
        var model = Model();
        var holder = Declared<AbstractType>(model, "Probe.Holder<>");

        var call = SameCallIn(Method(holder, "Through"));

        Assert.Same(Method(Declared<ClassType>(model, "Probe.Holders"), "Same"), call.Method);
        Assert.Equal("Probe.Holder<T>", Assert.IsType<ThisExpression>(Assert.Single(call.Arguments)).Type.FullyQualifiedName);
        Assert.Equal("Probe.Holder<T>", Assert.Single(call.ParameterTypes!).FullyQualifiedName);
    }

    private T Declared<T>(string fullyQualifiedName)
        where T : MemberType
    {
        return Declared<T>(Model(), fullyQualifiedName);
    }

    private static T Declared<T>(ProgramModelClass model, string fullyQualifiedName)
        where T : MemberType
    {
        return Assert.IsType<T>(model.TypesByFQN[fullyQualifiedName]);
    }

    private ProgramModelClass Model()
    {
        var model = BuildModel("var unused = 1;", "", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return model;
    }

    private static Method Method(MemberType owner, string name)
    {
        return owner.MethodsList.Single(method => method.Name == name);
    }

    private static Expression Returned(Method method)
    {
        var returned = Assert.IsType<ReturnStatement>(method.Body!.Statements.Last());
        Assert.NotNull(returned.Value);
        return returned.Value;
    }

    private static InvocationExpression SameCallIn(Method method)
    {
        return BodyWalker.DescendantsAndSelf(method.Body!).OfType<InvocationExpression>().Single(call => call.Method?.Name == "Same");
    }

    private static ThisExpression ThisIn(object body)
    {
        return BodyWalker.DescendantsAndSelf(body).OfType<ThisExpression>().First();
    }
}