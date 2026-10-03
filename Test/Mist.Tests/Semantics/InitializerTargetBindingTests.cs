using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
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
    public void AFieldOrPropertyIsAssignedWithoutComplaint(string name, Type member)
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named(name)), Widget(f), f.Binding, f.Builder));

        Assert.Equal(name, bound.MemberName);
        Assert.IsType(member, bound.Member);
        Assert.Same(f.Built.Type("Int32"), bound.TargetType);
        Assert.Same(f.Built.Type("Int32"), bound.Value.Type);
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
        Assert.Equal("MIST_PM_INITIALIZER_TARGET_NOT_ASSIGNABLE", diagnostic.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal($"'{name}' on 'Widget' is not a field or a property, so an initializer block cannot assign to it.", diagnostic.Message);
        Assert.Equal(BlockPath, diagnostic.CompilationUnitPath);
        Assert.Equal(TargetSpan, diagnostic.Location);
    }

    [Theory]
    [InlineData("Colour", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("Weight", "MIST_PM_AMBIGUOUS_MEMBER")]
    public void AMissingOrAmbiguousNameIsReportedByLookupAlone(string name, string diagnosticId)
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named(name, 24, BlockPath)), Widget(f), f.Binding, f.Builder));

        Assert.Null(bound.TargetType);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Id.Value);
        Assert.Equal(BlockPath, diagnostic.CompilationUnitPath);
        Assert.Equal(TargetSpan, diagnostic.Location);
    }

    [Fact]
    public void AnInitializerOnAnUnresolvedTypeAddsNoComplaint()
    {
        var f = new BindingFragments();

        var bound = Assert.Single(ObjectInitializerBinder.Bind(Block(Named("Resize")), ErrorType.Instance, f.Binding, f.Builder));

        Assert.Null(bound.TargetType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static ClassType Widget(BindingFragments f)
    {
        var intType = f.Built.Type("Int32");
        var widget = HandBuiltModel.Class("Widget");
        widget.FieldsList.Add(new Field("Size", intType, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = widget });
        widget.PropertiesList.Add(new Property("Count", intType, DeclarationFragments.Public) { DeclaringType = widget });
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

    private static PropertyInitializerBlockNode Block(params NamedPropertyAssignmentPropertyAssignmentNode[] assignments)
    {
        return new PropertyInitializerBlockNode
        {
            Assignments = new PropertyAssignmentListNode { PropertyAssignment = assignments[0], PropertyAssignments = [.. assignments.Skip(1)] },
        };
    }
}