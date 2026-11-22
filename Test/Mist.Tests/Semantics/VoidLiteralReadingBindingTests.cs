using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class VoidLiteralReadingBindingTests
{
    private readonly BindingFragments f = new();
    private readonly ClassType result = new("Result<,>", "Standard", null, null, null);

    public VoidLiteralReadingBindingTests()
    {
        result.TypeParametersList.Add(new TypeParameter("T", 0, result.Scope, result));
        result.TypeParametersList.Add(new TypeParameter("E", 1, result.Scope, result));
        f.Register(result);
    }

    [Theory]
    [InlineData("Void", "String")]
    [InlineData("String", "Void")]
    [InlineData("Int32?", "String")]
    [InlineData("String", "Int32?")]
    public void VoidWhereAResultSideIsOrHoldsVoid_IsTheUnitValueTheResultThenWraps(string success, string error)
    {
        var target = f.Built.Interner.GetOrCreateConstructedType(result, [Named(success), Named(error)]);

        var literal = Assert.IsType<VoidLiteralExpression>(BindVoid(target));

        Assert.Equal(VoidReading.Singleton, literal.Reading);
        Assert.Same(f.Built.Type("Void"), literal.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void VoidWhereAVoidIsExpected_IsTheUnitValue()
    {
        var literal = Assert.IsType<VoidLiteralExpression>(BindVoid(f.Built.Type("Void")));

        Assert.Equal(VoidReading.Singleton, literal.Reading);
        Assert.Same(f.Built.Type("Void"), literal.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void VoidWhereAnOptionalIsExpected_IsAbsenceOfThatOptional()
    {
        var optional = Named("Int32?");

        var literal = Assert.IsType<VoidLiteralExpression>(BindVoid(optional));

        Assert.Equal(VoidReading.Absence, literal.Reading);
        Assert.Same(optional, literal.Type);
    }

    [Theory]
    [InlineData("Result")]
    [InlineData("Type")]
    [InlineData("Int32")]
    [InlineData(null)]
    public void VoidWhereNoVoidIsExpected_StaysTheTypeLiteral(string? targetName)
    {
        var target = targetName == "Result"
            ? f.Built.Interner.GetOrCreateConstructedType(result, [Named("String"), Named("Int32")])
            : targetName is null ? null : Named(targetName);

        var literal = Assert.IsType<TypeLiteralExpression>(BindVoid(target));

        Assert.Same(f.Built.Type("Void"), literal.ReferencedType);
        Assert.Same(f.Built.Type("Type"), literal.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ATypeThatDoesNotResolve_IsAFailedExpressionRatherThanATypeValue()
    {
        var node = BindingFragments.At<TypeLiteralExpressionPrimaryNode>();
        node.TypeRef = BindingFragments.TypeName("Missing");

        using(f.Binding.PushTargetType(f.Built.Type("Type")))
        {
            _ = Assert.IsType<ErrorExpression>(ExpressionBinder.BindPrimary(node, f.Binding, f.Builder));
        }

        _ = Assert.Single(f.Diagnostics.ToImmutableArray());
    }

    private Expression BindVoid(IMistType? target)
    {
        var node = BindingFragments.At<TypeLiteralExpressionPrimaryNode>();
        node.TypeRef = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() };

        using(f.Binding.PushTargetType(target))
        {
            return ExpressionBinder.BindPrimary(node, f.Binding, f.Builder);
        }
    }

    private IMistType Named(string name)
    {
        return name.EndsWith('?')
            ? f.Built.Interner.GetOrCreateUnionType([f.Built.Type(name.TrimEnd('?')), f.Built.Type("Void")])
            : f.Built.Type(name);
    }
}