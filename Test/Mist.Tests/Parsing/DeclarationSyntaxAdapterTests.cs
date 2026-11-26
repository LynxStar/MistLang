using Mist.Generated;
using Mist.Tests.Generators;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class DeclarationSyntaxAdapterTests
{
    [Fact]
    public void TypedForeachReturnsWrittenTypeAndOriginalParts()
    {
        var type = new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode { INT = SourceToken(TokenKind.INT, "int", 9) },
            },
        };
        var name = SourceToken(TokenKind.DISCARD, "_", 13);
        var collection = MistAstGenerator.WrapPrimary(new IdentifierExpressionPrimaryNode
        {
            Name = SourceToken(TokenKind.IDENTIFIER, "items", 18),
        });
        var body = new BlockNode
        {
            Statements = [new BreakStatementStatementNode { BreakStatement = new BreakStatementNode() }],
        };
        var loop = new TypedForeachForeachStatementNode
        {
            Type = type,
            Name = name,
            Collection = collection,
            Body = body,
        };

        Assert.Same(type, loop.GetTypeOrNull());
        Assert.Equal(name, loop.GetName());
        Assert.Same(collection, loop.GetCollection());
        Assert.Same(body, loop.GetBody());
    }

    [Fact]
    public void InferredForeachReturnsNoWrittenTypeAndOriginalParts()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "item", 13);
        var collection = MistAstGenerator.WrapPrimary(new IdentifierExpressionPrimaryNode
        {
            Name = SourceToken(TokenKind.IDENTIFIER, "items", 21),
        });
        var body = new BlockNode
        {
            Statements = [new BreakStatementStatementNode { BreakStatement = new BreakStatementNode() }],
        };
        var loop = new VarForeachForeachStatementNode
        {
            VAR = SourceToken(TokenKind.VAR, "var", 9),
            Name = name,
            Collection = collection,
            Body = body,
        };

        Assert.Null(loop.GetTypeOrNull());
        Assert.Equal(name, loop.GetName());
        Assert.Same(collection, loop.GetCollection());
        Assert.Same(body, loop.GetBody());
    }

    [Fact]
    public void RegularTypeParameterReturnsDeclarationToken()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "TValue", 10);
        var parameter = new RegularTypeParameterTypeParameterNode { Name = name };

        Assert.Equal(name, parameter.GetName());
    }

    [Fact]
    public void PrimitiveDependentParameterReturnsDeclarationToken()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "Size", 10);
        var parameter = new DependentTypeParameterTypeParameterNode
        {
            Name = name,
            ValueType = new IntTypePrimitiveTypeNode { INT = SourceToken(TokenKind.INT, "int", 15) },
        };

        Assert.Equal(name, parameter.GetName());
    }

    [Fact]
    public void NamedDomainParameterReturnsDeclarationToken()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "Size", 10);
        var parameter = new NamedConstraintDependentTypeParameterTypeParameterNode
        {
            Name = name,
            ConstraintName = new QualifiedIdentifierNode
            {
                IDENTIFIER = SourceToken(TokenKind.IDENTIFIER, "Measures", 15),
                IDENTIFIERs = [SourceToken(TokenKind.IDENTIFIER, "PositiveInt", 24)],
            },
        };

        Assert.Equal(name, parameter.GetName());
    }

    [Fact]
    public void ParamsPackReturnsDeclarationToken()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "TArgs", 17);
        var parameter = new ParamsPackTypeParameterTypeParameterNode
        {
            PARAMS = SourceToken(TokenKind.PARAMS, "params", 10),
            Name = name,
        };

        Assert.Equal(name, parameter.GetName());
    }

    [Fact]
    public void ExplicitPackReturnsDeclarationToken()
    {
        var name = SourceToken(TokenKind.IDENTIFIER, "TArgs", 17);
        var parameter = new ExplicitPackTypeParameterTypeParameterNode
        {
            DomainName = SourceToken(TokenKind.IDENTIFIER, "Type", 10),
            ArrayRank = new UnsizedArrayRankArrayRankSuffixNode(),
            Name = name,
        };

        Assert.Equal(name, parameter.GetName());
    }

    private static Token SourceToken(TokenKind kind, string text, int offset)
    {
        return new Token((int)kind, new SourceSpan(offset, text.Length),
            new LinePosition(1, offset + 1), new LinePosition(1, offset + text.Length + 1),
            TokenChannel.Default, text);
    }
}