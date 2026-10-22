using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PostfixBinder's chain over hand-built names, asserting what a type named in an expression binds to.
[Trait("Tier", "2")]
public sealed class TypeNameValueBindingTests
{
    private readonly BindingFragments f = new();
    private readonly ClassType widget = HandBuiltModel.Class("Widget");

    public TypeNameValueBindingTests()
    {
        f.Register(widget);
        f.Register(new TypeParameter("T", 0, new Scope(null), null));
        f.Register(new TypeParameter("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed });
        f.Register(new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = f.Built.Type("Int32") });
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("T")]
    public void ATypeNamedAsAWholeExpression_IsTheTypeValueItNames(string name)
    {
        var literal = Assert.IsType<TypeLiteralExpression>(f.Bind(BindingFragments.Identifier(name)));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(name, literal.ReferencedType.Name);
        Assert.Same(f.Built.Type("Type"), literal.Type);
    }

    [Theory]
    [InlineData("TArgs")]
    [InlineData("N")]
    public void APackOrDependentParameter_KeepsTheValueItAlreadyIs(string name)
    {
        var reference = Assert.IsType<TypeReferenceExpression>(f.Bind(BindingFragments.Identifier(name)));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(name, reference.ReferencedType.Name);
    }

    [Fact]
    public void ATypeBeforeAMemberRead_StaysItsStaticReceiver()
    {
        widget.FieldsList.Add(new Field("Count", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = widget,
        });

        var read = Assert.IsType<MemberAccessExpression>(f.Bind(BindingFragments.Identifier("Widget"), new MemberAccessSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("Count"),
        }));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(widget, Assert.IsType<TypeReferenceExpression>(read.Receiver).ReferencedType);
    }
}