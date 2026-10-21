using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class FieldReceiverTests
{
    private static readonly MemberModifiers Instance = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Static = new() { Accessibility = Accessibility.Public, IsStatic = true };
    private readonly HandBuiltModel model = new();

    [Fact]
    public void AnInstanceField_IsReachedThroughThis()
    {
        var counter = HandBuiltModel.Class("Counter");

        Assert.Equal("this.", FieldReceiver.Of(Field(counter, "count", Instance)));
    }

    [Fact]
    public void AStaticFieldTheHolderCarries_IsNamedByTheHolderNotItsDeclaringType()
    {
        var source = HandBuiltModel.Class("Source");
        var made = Field(source, "made", Static);

        using(FieldReceiver.Holding(source, "Copy", [made]))
        {
            Assert.Equal("Copy.", FieldReceiver.Of(made));
        }
    }

    [Fact]
    public void AStaticFieldOfAGenericBase_IsNamedThroughTheBaseTheHolderClosed()
    {
        var generic = Generic("Base");
        var made = Field(generic, "made", Static);
        var derived = HandBuiltModel.Class("Derived", model.Interner.GetOrCreateConstructedType(generic, [model.Type("Int32")]));

        using(FieldReceiver.Holding(derived, "Derived", []))
        {
            Assert.Equal("Probe.Base<int>.", FieldReceiver.Of(made));
        }
    }

    [Fact]
    public void AStaticFieldOfAnEnclosingType_IsNamedByThatEnclosingHolder()
    {
        var outer = HandBuiltModel.Class("Outer");
        var made = Field(outer, "made", Static);
        var inner = HandBuiltModel.Class("Inner");

        using(FieldReceiver.Holding(outer, "Outer", [made]))
        using(FieldReceiver.Holding(inner, "Inner", []))
        {
            Assert.Equal("Outer.", FieldReceiver.Of(made));
        }
    }

    [Fact]
    public void AStaticFieldOutsideEveryHolder_IsNamedByItsOpenDeclaringType()
    {
        var made = Field(Generic("Box"), "made", Static);

        Assert.Equal("Probe.Box<T>.", FieldReceiver.Of(made));
    }

    [Fact]
    public void AHolderStopsHoldingWhenItsScopeEnds()
    {
        var source = HandBuiltModel.Class("Source");
        var made = Field(source, "made", Static);

        using(FieldReceiver.Holding(source, "Copy", [made]))
        {
        }

        Assert.Equal("Source.", FieldReceiver.Of(made));
    }

    [Theory]
    [InlineData(false, "@this.")]
    [InlineData(true, "")]
    public void UnderACarrier_TheCarriersQualificationIsKept(bool declaredElsewhere, string expected)
    {
        var carried = HandBuiltModel.Class("Carried");
        var field = Field(declaredElsewhere ? HandBuiltModel.Class("Elsewhere") : carried, "count", Instance);

        using(ReceiverSpelling.Enter(carried))
        {
            Assert.Equal(expected, FieldReceiver.Of(field));
        }
    }

    private static ClassType Generic(string name)
    {
        var type = new ClassType(name + "<>", "Probe", new Scope(null), null, null);
        type.TypeParametersList.Add(new TypeParameter("T", 0, type.Scope, type));
        return type;
    }

    private Field Field(ConcreteType owner, string name, MemberModifiers modifiers)
    {
        var field = new Field(name, model.Type("Int32"), OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }
}