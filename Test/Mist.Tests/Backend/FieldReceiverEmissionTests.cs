using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class FieldReceiverEmissionTests
{
    private static readonly MemberModifiers Instance = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Static = new() { Accessibility = Accessibility.Public, IsStatic = true };
    private readonly HandBuiltModel model = new();

    private ClassType Integer => model.Type("Int32");

    [Theory]
    [InlineData(false, "this.count = this.count + 1;", "return this.count;")]
    [InlineData(true, "Counter.count = Counter.count + 1;", "return Counter.count;")]
    public void ABareFieldWrittenAndRead_NamesItsReceiver(bool isStatic, string write, string read)
    {
        var counter = HandBuiltModel.Class("Counter");
        var count = Field(counter, "count", isStatic ? Static : Instance);
        Bump(counter, count, isStatic);

        var text = Emit(counter);

        Assert.Contains(write, text, StringComparison.Ordinal);
        Assert.Contains(read, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticFieldOfAGenericType_IsNamedByTheTypeOverItsOwnParameters()
    {
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        Bump(box, Field(box, "made", Static), isStatic: true);

        var text = Emit(box);

        Assert.Contains("Box<T>.made = Box<T>.made + 1;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticFieldInheritedFromAGenericBase_IsNamedThroughTheClosedBase()
    {
        var generic = new ClassType("Base<>", "Probe", new Scope(null), null, null);
        generic.TypeParametersList.Add(new TypeParameter("T", 0, generic.Scope, generic));
        var made = Field(generic, "made", Static);
        var derived = HandBuiltModel.Class("Derived", model.Interner.GetOrCreateConstructedType(generic, [Integer]));
        Bump(derived, made, isStatic: true);

        var text = Emit(derived);

        Assert.Contains("Probe.Base<int>.made = Probe.Base<int>.made + 1;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticFieldFlattenedIntoADerivedStruct_IsNamedByTheStructHoldingTheCopy()
    {
        var parent = HandBuiltModel.Struct("Parent");
        var made = Field(parent, "made", Static);
        var child = HandBuiltModel.Struct("Child");
        child.BaseType = parent;
        Bump(child, made, isStatic: true);

        var text = Emit(child);

        Assert.Contains("public static int made;", text, StringComparison.Ordinal);
        Assert.Contains("Child.made = Child.made + 1;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Parent.made", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitMemberAccess_KeepsItsOneReceiver()
    {
        var counter = HandBuiltModel.Class("Counter");
        _ = Field(counter, "count", Instance);
        counter.MethodsList.Add(new Method("Read", Integer, [], Instance)
        {
            DeclaringType = counter,
            Body = HandBuiltModel.Block(new ReturnStatement(
                new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), counter), "count", Integer), Integer)),
        });

        var text = Emit(counter);

        Assert.Contains("return this.count;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this.this.", text, StringComparison.Ordinal);
    }

    private Field Field(ConcreteType owner, string name, MemberModifiers modifiers)
    {
        var field = new Field(name, Integer, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private void Bump(ConcreteType owner, Field field, bool isStatic)
    {
        IMistType integer = Integer;
        owner.MethodsList.Add(new Method("Bump", integer, [], isStatic ? Static : Instance)
        {
            DeclaringType = owner,
            Body = HandBuiltModel.Block(
                new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Reference(field),
                    new BinaryExpression(HandBuiltModel.Node(), Reference(field), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1L), integer),
                    integer)),
                new ReturnStatement(Reference(field), integer)),
        });
    }

    private static FieldReferenceExpression Reference(Field field)
    {
        return new FieldReferenceExpression(HandBuiltModel.Node(), field);
    }

    private string Emit(MemberType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model.Model, diagnostics), model.Model.VariadicUses,
            writer, diagnostics, ref anyEmitted);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}