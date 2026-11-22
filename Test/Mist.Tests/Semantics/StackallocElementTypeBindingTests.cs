using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StackallocElementTypeBindingTests
{
    private readonly BindingFragments f = new();

    public StackallocElementTypeBindingTests()
    {
        foreach(var name in new[] { "UInt64", "UInt16", "SByte", "Single", "Decimal", "Char" })
        {
            f.Register(f.Built.Type(name));
        }

        var span = f.Built.Type("Span<,>");
        span.TypeParametersList.Add(new TypeParameter("T", 0, span.Scope, span));
        span.TypeParametersList.Add(new TypeParameter("Length", 1, span.Scope, span) { IsDependent = true, Domain = f.Built.Type("Int32") });
        f.Register(span);
    }

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("uint", "UInt32")]
    [InlineData("long", "Int64")]
    [InlineData("ulong", "UInt64")]
    [InlineData("short", "Int16")]
    [InlineData("ushort", "UInt16")]
    [InlineData("byte", "Byte")]
    [InlineData("sbyte", "SByte")]
    [InlineData("float", "Single")]
    [InlineData("double", "Double")]
    [InlineData("decimal", "Decimal")]
    [InlineData("bool", "Boolean")]
    [InlineData("char", "Char")]
    [InlineData("string", "String")]
    public void APrimitiveKeywordElementIsTheStandardTypeItAliases(string keyword, string aliased)
    {
        var allocation = Allocate(new PrimitiveElementTypeRefElementTypeRefNode { PrimitiveType = Keyword(keyword) });

        Assert.Same(f.Built.Type(aliased), allocation.ElementType);
        Assert.Same(f.Built.Type(aliased), Assert.IsType<ConstructedType>(allocation.Type).TypeArguments[0]);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFunctionElementIsPureExactlyWhenWrittenPure(bool pure)
    {
        var element = BindingFragments.At<FuncElementTypeRefElementTypeRefNode>();
        element.TypeArgs = BindingFragments.TypeArguments("Int32", "String");
        element.Pure = pure ? BindingFragments.Token("pure") : null;

        var function = Assert.IsAssignableFrom<IFunctionType>(Allocate(element).ElementType);

        Assert.Equal(pure, function.IsPure);
        Assert.Same(f.Built.Type("Int32"), Assert.Single(function.ParameterTypes));
        Assert.Same(f.Built.Type("String"), function.ReturnType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void PureBeforeANamedElementThatIsNoFunctionTypeIsRefused()
    {
        var widget = HandBuiltModel.Class("Widget");
        f.Register(widget);
        var element = BindingFragments.At<NamedElementTypeRefElementTypeRefNode>();
        element.Name = BindingFragments.Name("Widget");
        element.Pure = BindingFragments.Token("pure");

        Assert.Same(widget, Allocate(element).ElementType);

        Assert.Equal("MIST0322", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private StackallocExpression Allocate(ElementTypeRefNode element)
    {
        var size = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        size.IntegerLiteral = BindingFragments.Token("2");
        var node = BindingFragments.At<StackallocExpressionPrimaryNode>();
        node.Type = element;
        node.Size = BindingFragments.Expression(size);

        return Assert.IsType<StackallocExpression>(ExpressionBinder.BindPrimary(node, f.Binding, f.Builder));
    }

    private static PrimitiveTypeNode Keyword(string keyword)
    {
        return keyword switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "uint" => new UintTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "ulong" => new UlongTypePrimitiveTypeNode(),
            "short" => new ShortTypePrimitiveTypeNode(),
            "ushort" => new UshortTypePrimitiveTypeNode(),
            "byte" => new ByteTypePrimitiveTypeNode(),
            "sbyte" => new SbyteTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "decimal" => new DecimalTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(keyword)),
        };
    }
}