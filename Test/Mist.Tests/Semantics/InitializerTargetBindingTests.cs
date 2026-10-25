using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class InitializerTargetBindingTests
{
    private const string BlockPath = "Probe/Initializer.mist";

    private static readonly LinePositionSpan TargetSpan = new(new LinePosition(1, 25), new LinePosition(1, 26));

    [Theory]
    [InlineData("Size", typeof(FieldResult))]
    [InlineData("Count", typeof(PropertyResult))]
    [InlineData("PrivateSize", typeof(FieldResult))]
    [InlineData("ClosedCount", typeof(PropertyResult))]
    public void AFieldOrPropertyIsAssignedWithoutComplaint(string name, Type member)
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named(name)), Widget(f), f.Binding, f.Builder));

        Assert.Equal(name, bound.MemberName);
        Assert.IsType(member, bound.Member);
        Assert.Same(f.Built.Type("Int32"), bound.TargetType);
        Assert.Same(f.Built.Type("Int32"), bound.Value.Type);
        Assert.False(bound.IsTargetFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("Resize", typeof(MethodGroupResult))]
    [InlineData("Part", typeof(TypeResult))]
    public void AMethodOrNestedTypeIsRefusedOnceAtItsOwnAssignment(string name, Type found)
    {
        var f = new BindingFragments();
        var refused = Named(name, 24, BlockPath);

        var bound = ObjectInitializerBinder.Bind(Block(Named("Size", 10), refused), Widget(f), f.Binding, f.Builder);

        Assert.Equal(2, bound.Count);
        Assert.Same(f.Built.Type("Int32"), bound[0].TargetType);
        Assert.Same(refused, bound[1].Node);
        Assert.Equal(name, bound[1].MemberName);
        Assert.IsType(found, bound[1].Member);
        Assert.Null(bound[1].TargetType);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0381", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal($"'{name}' on 'Widget' is not a field or a property, so an initializer block cannot assign to it.", diagnostic.Message);
        Assert.Equal(BlockPath, diagnostic.Location.Path);
        Assert.Equal(TargetSpan, diagnostic.Position());
    }

    [Theory]
    [InlineData("Colour", "MIST0301")]
    [InlineData("Weight", "MIST0302")]
    public void AMissingOrAmbiguousNameIsReportedByLookupAlone(string name, string diagnosticId)
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named(name, 24, BlockPath)), Widget(f), f.Binding, f.Builder));

        Assert.Null(bound.TargetType);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(BlockPath, diagnostic.Location.Path);
        Assert.Equal(TargetSpan, diagnostic.Position());
    }

    [Fact]
    public void AnInitializerOnAnUnresolvedTypeAddsNoComplaint()
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named("Resize")), ErrorType.Instance, f.Binding, f.Builder));

        Assert.Null(bound.TargetType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("GlobalSize", typeof(FieldResult), "MIST0382")]
    [InlineData("GlobalCount", typeof(PropertyResult), "MIST0382")]
    [InlineData("Reset", typeof(MethodGroupResult), "MIST0381")]
    public void ADeclaredStaticMemberIsRetainedAndRefusedAtItsEntry(string name, Type member, string diagnosticId)
    {
        var f = new BindingFragments();
        var entry = Named(name, 24, BlockPath);

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(entry), Widget(f), f.Binding, f.Builder));

        Assert.IsType(member, bound.Member);
        Assert.Null(bound.TargetType);
        Assert.True(bound.IsTargetFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(BlockPath, diagnostic.Location.Path);
        Assert.Equal(TargetSpan, diagnostic.Position());
    }

    [Fact]
    public void AnEnumVariantIsRetainedAndRefusedAsStorage()
    {
        var f = new BindingFragments();
        var colour = HandBuiltModel.Enum("Colour", f.Built.Type("Int32"));
        colour.MembersList.Add(new EnumMember("Red", false, null));

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named("Red", 24, BlockPath)), colour, f.Binding, f.Builder));

        _ = Assert.IsType<EnumMemberResult>(bound.Member);
        Assert.Null(bound.TargetType);
        Assert.True(bound.IsTargetFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0381", diagnostic.Number.Value);
        Assert.Equal(TargetSpan, diagnostic.Position());
    }

    [Fact]
    public void ACollectionElementRecordsItsAddAndTheParameterTypesClosedOverItsOwner()
    {
        var f = new BindingFragments();
        var bag = HandBuiltModel.Class("Bag");
        var item = new TypeParameter("T", 0, bag.Scope, bag);
        bag.TypeParametersList.Add(item);
        var add = BindingFragments.Member(bag, "Add", f.Built.Type("Void"), item);
        var closed = f.Built.Interner.GetOrCreateConstructedType(bag, [f.Built.Type("Int32")]);

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Element()), closed, f.Binding, f.Builder));

        Assert.Same(add, bound.Add);
        Assert.Same(f.Built.Type("Int32"), Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<IMistType>>(bound.AddParameterTypes)));
        Assert.Null(bound.MemberName);
        Assert.False(bound.IsTargetFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false, "MIST0301")]
    [InlineData(true, "MIST0363")]
    public void AnElementWithNoAddMethodKeepsItsValueBehindOneRefusal(bool addIsAField, string diagnosticId)
    {
        var f = new BindingFragments();
        var bag = HandBuiltModel.Class("Bag");
        if(addIsAField)
        {
            bag.FieldsList.Add(new Field("Add", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = bag });
        }

        var element = Element(24, BlockPath);

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(element), bag, f.Binding, f.Builder));

        Assert.Same(element, bound.Node);
        Assert.True(bound.IsTargetFailed);
        Assert.Null(bound.Add);
        Assert.Same(f.Built.Type("Int32"), bound.Value.Type);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(TargetSpan, diagnostic.Position());
    }

    private static ClassType Widget(BindingFragments f)
    {
        var intType = f.Built.Type("Int32");
        var widget = HandBuiltModel.Class("Widget");
        widget.FieldsList.Add(new Field("Size", intType, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = widget });
        widget.PropertiesList.Add(new Property("Count", intType, DeclarationFragments.Public) { DeclaringType = widget });
        widget.FieldsList.Add(new Field("PrivateSize", intType, OwnershipKind.Unrestricted,
            DeclarationFragments.Public with { Accessibility = Accessibility.Private, IsReadonly = true })
        { DeclaringType = widget });
        widget.PropertiesList.Add(new Property("ClosedCount", intType, DeclarationFragments.Public)
        {
            DeclaringType = widget,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Private },
        });
        var staticModifiers = DeclarationFragments.Public with { IsStatic = true };
        widget.FieldsList.Add(new Field("GlobalSize", intType, OwnershipKind.Unrestricted, staticModifiers) { DeclaringType = widget });
        widget.PropertiesList.Add(new Property("GlobalCount", intType, staticModifiers) { DeclaringType = widget });
        widget.MethodsList.Add(new Method("Reset", f.Built.Type("Void"), [], staticModifiers) { DeclaringType = widget });
        widget.FieldsList.Add(new Field("Weight", intType, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = widget });
        _ = BindingFragments.Member(widget, "Weight", intType);
        _ = BindingFragments.Member(widget, "Resize", f.Built.Type("Void"));
        widget.NestedTypes.Add(new ClassType("Part", "Probe", new Scope(null), widget, null) { Modifiers = DeclarationFragments.Public });
        return widget;
    }

    private static NamedPropertyAssignmentPropertyAssignmentNode Named(string name, int offset = 10, string path = "hand-built.mist")
    {
        var location = HandBuiltModel.Node(offset, path);
        var value = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        value.IntegerLiteral = BindingFragments.Token("1");
        return new NamedPropertyAssignmentPropertyAssignmentNode
        {
            Span = location.Span,
            Origin = location.Origin,
            Name = BindingFragments.Token(name),
            Value = BindingFragments.Expression(value),
        };
    }

    private static CollectionElementAssignmentPropertyAssignmentNode Element(int offset = 10, string path = "hand-built.mist")
    {
        var location = HandBuiltModel.Node(offset, path);
        var value = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        value.IntegerLiteral = BindingFragments.Token("1");
        return new CollectionElementAssignmentPropertyAssignmentNode
        {
            Span = location.Span,
            Origin = location.Origin,
            Value = BindingFragments.Expression(value),
        };
    }

    private static PropertyInitializerBlockNode Block(params PropertyAssignmentNode[] assignments)
    {
        return new PropertyInitializerBlockNode
        {
            Assignments = new PropertyAssignmentListNode { PropertyAssignment = assignments[0], PropertyAssignments = [.. assignments.Skip(1)] },
        };
    }
}