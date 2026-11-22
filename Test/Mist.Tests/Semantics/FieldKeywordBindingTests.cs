using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class FieldKeywordBindingTests
{
    private readonly BindingFragments f = new();

    [Fact]
    public void FieldOutsideAPropertyAccessorIsRefusedAtTheKeyword()
    {
        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.At<FieldKeywordExpressionPrimaryNode>()));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0343", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void FieldInsideAnAccessorIsTheEnclosingPropertysBackingStorage()
    {
        var property = new Property("Count", f.Built.Type("Int32"), DeclarationFragments.Public);
        f.Binding.EnclosingProperty = property;

        var reference = Assert.IsType<BackingFieldReferenceExpression>(f.Bind(BindingFragments.At<FieldKeywordExpressionPrimaryNode>()));

        Assert.Same(property, reference.Property);
        Assert.True(property.UsesBackingField);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}