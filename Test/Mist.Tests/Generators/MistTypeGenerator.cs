using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

public static class MistTypeGenerator
{
    internal static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static readonly PrimitiveTypeNode[] AllPrimitiveTypeNodes =
    [
        new IntTypePrimitiveTypeNode(),
        new UintTypePrimitiveTypeNode(),
        new LongTypePrimitiveTypeNode(),
        new UlongTypePrimitiveTypeNode(),
        new ShortTypePrimitiveTypeNode(),
        new UshortTypePrimitiveTypeNode(),
        new ByteTypePrimitiveTypeNode(),
        new SbyteTypePrimitiveTypeNode(),
        new FloatTypePrimitiveTypeNode(),
        new DoubleTypePrimitiveTypeNode(),
        new DecimalTypePrimitiveTypeNode(),
        new BoolTypePrimitiveTypeNode(),
        new CharTypePrimitiveTypeNode(),
        new StringTypePrimitiveTypeNode(),
    ];

    private static readonly PrimitiveTypeNode[] NumericPrimitiveTypeNodes =
    [
        new IntTypePrimitiveTypeNode(),
        new UintTypePrimitiveTypeNode(),
        new LongTypePrimitiveTypeNode(),
        new UlongTypePrimitiveTypeNode(),
        new ShortTypePrimitiveTypeNode(),
        new UshortTypePrimitiveTypeNode(),
        new ByteTypePrimitiveTypeNode(),
        new SbyteTypePrimitiveTypeNode(),
        new FloatTypePrimitiveTypeNode(),
        new DoubleTypePrimitiveTypeNode(),
        new DecimalTypePrimitiveTypeNode(),
    ];

    public static Gen<PrimitiveTypeNode> GenPrimitiveTypeNode()
    {
        return Gen.Elements(AllPrimitiveTypeNodes);
    }

    public static Gen<PrimitiveTypeNode> GenNumericPrimitiveTypeNode()
    {
        return Gen.Elements(NumericPrimitiveTypeNodes);
    }

    public static Gen<TypeReferenceNode> GenPrimitiveTypeRef()
    {
        return GenPrimitiveTypeNode().Select(pt =>
            new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = pt,
                }
            });
    }

    public static Gen<TypeReferenceNode> GenNumericTypeRef()
    {
        return GenNumericPrimitiveTypeNode().Select(pt =>
            new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = pt,
                }
            });
    }

    public static Gen<TypeReferenceNode> GenVoidTypeRef()
    {
        return Gen.Constant(new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() });
    }

    public static Gen<TypeReferenceNode> GenIntTypeRef()
    {
        return Gen.Constant(new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            }
        });
    }

    public static Gen<TypeReferenceNode> GenBoolTypeRef()
    {
        return Gen.Constant(new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            }
        });
    }

    public static Gen<TypeReferenceNode> GenStringTypeRef()
    {
        return Gen.Constant(new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            }
        });
    }

    public static Gen<TypeReferenceNode> GenNonVoidTypeRef()
    {
        return GenPrimitiveTypeRef();
    }

    public static Gen<TypeReferenceNode> GenReturnTypeRef()
    {
        return Gen.OneOf(GenPrimitiveTypeRef(), GenVoidTypeRef());
    }
}