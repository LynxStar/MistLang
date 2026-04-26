// Generators for Mist type reference AST nodes.
// Used by property-based tests to produce random but structurally valid type references.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist type reference nodes (<see cref="TypeReferenceNode"/>)
/// and primitive type nodes (<see cref="PrimitiveTypeNode"/>).
/// </summary>
public static class MistTypeGenerator
{
    // ─── Token helper ────────────────────────────────────────────────────

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

    // ─── Primitive type nodes ────────────────────────────────────────────

    /// <summary>All concrete primitive type node constructors.</summary>
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

    /// <summary>Numeric primitive type nodes only (for arithmetic/comparison contexts).</summary>
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

    /// <summary>Generates a random primitive type node.</summary>
    public static Gen<PrimitiveTypeNode> GenPrimitiveTypeNode() =>
        Gen.Elements(AllPrimitiveTypeNodes);

    /// <summary>Generates a random numeric primitive type node.</summary>
    public static Gen<PrimitiveTypeNode> GenNumericPrimitiveTypeNode() =>
        Gen.Elements(NumericPrimitiveTypeNodes);

    // ─── Type reference nodes ────────────────────────────────────────────

    /// <summary>Generates a PrimitiveTypeRef type reference (no array rank).</summary>
    public static Gen<TypeReferenceNode> GenPrimitiveTypeRef() =>
        GenPrimitiveTypeNode().Select(pt =>
            (TypeReferenceNode)new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = pt,
            });

    /// <summary>Generates a numeric PrimitiveTypeRef type reference.</summary>
    public static Gen<TypeReferenceNode> GenNumericTypeRef() =>
        GenNumericPrimitiveTypeNode().Select(pt =>
            (TypeReferenceNode)new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = pt,
            });

    /// <summary>Generates a void type reference.</summary>
    public static Gen<TypeReferenceNode> GenVoidTypeRef() =>
        Gen.Constant((TypeReferenceNode)new VoidTypeRefTypeReferenceNode());

    /// <summary>Generates an int type reference.</summary>
    public static Gen<TypeReferenceNode> GenIntTypeRef() =>
        Gen.Constant((TypeReferenceNode)new PrimitiveTypeRefTypeReferenceNode
        {
            PrimitiveType = new IntTypePrimitiveTypeNode(),
        });

    /// <summary>Generates a bool type reference.</summary>
    public static Gen<TypeReferenceNode> GenBoolTypeRef() =>
        Gen.Constant((TypeReferenceNode)new PrimitiveTypeRefTypeReferenceNode
        {
            PrimitiveType = new BoolTypePrimitiveTypeNode(),
        });

    /// <summary>Generates a string type reference.</summary>
    public static Gen<TypeReferenceNode> GenStringTypeRef() =>
        Gen.Constant((TypeReferenceNode)new PrimitiveTypeRefTypeReferenceNode
        {
            PrimitiveType = new StringTypePrimitiveTypeNode(),
        });

    /// <summary>
    /// Generates a random non-void type reference (primitive types only, no arrays).
    /// Suitable for variable declarations, parameters, and return types.
    /// </summary>
    public static Gen<TypeReferenceNode> GenNonVoidTypeRef() =>
        GenPrimitiveTypeRef();

    /// <summary>
    /// Generates a random type reference including void.
    /// Suitable for method return types.
    /// </summary>
    public static Gen<TypeReferenceNode> GenReturnTypeRef() =>
        Gen.OneOf(GenPrimitiveTypeRef(), GenVoidTypeRef());
}
