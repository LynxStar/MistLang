using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PropertyBackingStorageEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly ClassType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = Public with { IsIntrinsic = true },
    };

    [Fact]
    public void AutoGetterBesideABodiedSetter_ReadsTheDeclaredBackingField()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: true, bodiedSetter: true);

        Assert.Equal(
            "private int __Count_field;\npublic int Count\n{\n    get => __Count_field;\n    set => __Count_field = 7;\n}\n",
            Emit(property));
    }

    [Fact]
    public void BodiedGetterBesideAnAutoSetter_WritesTheDeclaredBackingField()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: false, bodiedSetter: false);

        Assert.Equal(
            "private int __Count_field;\npublic int Count\n{\n    get => __Count_field;\n    set => __Count_field = value;\n}\n",
            Emit(property));
    }

    [Fact]
    public void Initializer_IsCarriedByTheDeclaredBackingField()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: true, bodiedSetter: true);
        property.Initializer = HandBuiltModel.Literal(integer, 5L);

        var text = Emit(property);

        Assert.StartsWith("private int __Count_field = 5;\n", text, StringComparison.Ordinal);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticProperty_DeclaresAStaticBackingField()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: true, bodiedSetter: true, isStatic: true);

        Assert.StartsWith("private static int __Count_field;\n", Emit(property), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyOfAReadonlyType_DeclaresAReadonlyBackingField()
    {
        var holder = HandBuiltModel.Struct("Holder");
        holder.Modifiers = Public with { IsReadonly = true };
        var property = Declare(holder, autoGetter: false, bodiedSetter: false);

        Assert.StartsWith("private readonly int __Count_field;\n", Emit(property), StringComparison.Ordinal);
    }

    [Fact]
    public void NarrowedSetter_KeepsItsAccessibilityOverTheDeclaredBackingField()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: true, bodiedSetter: true, setterAccess: Accessibility.Private);

        Assert.Contains("\n    private set => __Count_field = 7;\n", Emit(property), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoProperty_KeepsTheStorageCSharpSynthesizes()
    {
        var property = Declare(HandBuiltModel.Class("Holder"), autoGetter: true, bodiedSetter: false);

        Assert.Equal("public int Count { get; set; }\n", Emit(property));
    }

    private Property Declare(
        ConcreteType owner, bool autoGetter, bool bodiedSetter, bool isStatic = false, Accessibility setterAccess = Accessibility.Public)
    {
        var property = new Property("Count", integer, Public with { IsStatic = isStatic })
        {
            Getter = new PropertyAccessor { IsAuto = autoGetter },
            Setter = new PropertyAccessor { IsAuto = !bodiedSetter, Accessibility = setterAccess },
            DeclaringType = owner,
        };
        var node = HandBuiltModel.Node();

        if(!autoGetter)
        {
            property.Getter!.ExpressionBody = new BackingFieldReferenceExpression(node, property);
        }

        if(bodiedSetter)
        {
            property.Setter!.ExpressionBody = new AssignmentExpression(
                node, new BackingFieldReferenceExpression(node, property), HandBuiltModel.Literal(integer, 7L), integer);
        }

        property.UsesBackingField = !autoGetter || bodiedSetter;
        owner.PropertiesList.Add(property);
        return property;
    }

    private static string Emit(Property property)
    {
        var writer = new CSharpWriter();
        var bag = new DiagnosticBag();

        PropertyEmitter.Emit(property, emitReadonly: false, readonlyGetter: false, writer, bag);

        Assert.Empty(bag.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}