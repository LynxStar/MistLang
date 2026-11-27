using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class CompilationUnitSyntaxAdapterTests
{
    [Fact]
    public void GetNamespaceReturnsTheFirstNamespaceInAMixedUnit()
    {
        var first = new BlockNamespaceNamespaceDeclarationNode { Name = QualifiedName("First") };
        var second = new BlockNamespaceNamespaceDeclarationNode { Name = QualifiedName("Second") };
        var unit = new CompilationUnitNode
        {
            Items =
            [
                new TypeItemTopLevelItemNode { TypeDeclaration = ClassDeclaration("Outside") },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = first },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = second },
            ],
        };

        Assert.Same(first, unit.GetNamespace());
    }

    [Fact]
    public void GetNamespaceReturnsNullWhenNoItemIsANamespace()
    {
        var unit = new CompilationUnitNode
        {
            Items = [new TypeItemTopLevelItemNode { TypeDeclaration = ClassDeclaration("Outside") }],
        };

        Assert.Null(unit.GetNamespace());
    }

    [Fact]
    public void GetTypesReturnsTheOriginalTopLevelTypesInItemOrder()
    {
        var first = new EnumTypeTypeDeclarationNode { EnumDeclaration = new EnumDeclarationNode { Name = Identifier("Zebra") } };
        var second = ClassDeclaration("Apple");
        var scope = new BlockNamespaceNamespaceDeclarationNode
        {
            Name = QualifiedName("Outer"),
            Body = [new NamespaceTypeItemNamespaceBodyItemNode { TypeDeclaration = ClassDeclaration("Nested") }],
        };
        var unit = new CompilationUnitNode
        {
            Items =
            [
                new TypeItemTopLevelItemNode { TypeDeclaration = first },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = scope },
                new TypeItemTopLevelItemNode { TypeDeclaration = second },
            ],
        };

        Assert.Collection(unit.GetTypes(), type => Assert.Same(first, type), type => Assert.Same(second, type));
    }

    [Fact]
    public void GetBlockReturnsTheOriginalBlockEvenWhenEmpty()
    {
        var block = new BlockNode { Statements = [] };
        var body = new CallableBlockBodyCallableBodyNode { Block = block };

        Assert.Same(block, body.GetBlock());
    }

    [Fact]
    public void GetBlockReturnsNullForABareSemicolon()
    {
        var body = new CallableNoBodyCallableBodyNode();

        Assert.Null(body.GetBlock());
    }

    private static BodyTypeTypeDeclarationNode ClassDeclaration(string name)
    {
        return new BodyTypeTypeDeclarationNode
        {
            TypeBody = new TypeBodyDeclarationNode { Keyword = new ClassKeywordTypeKeywordNode(), Name = Identifier(name) },
        };
    }

    private static QualifiedIdentifierNode QualifiedName(string name)
    {
        return new QualifiedIdentifierNode { IDENTIFIER = Identifier(name) };
    }

    private static Token Identifier(string name)
    {
        return new Token((int)TokenKind.IDENTIFIER, new SourceSpan(0, name.Length),
            new LinePosition(1, 1), new LinePosition(1, name.Length + 1), TokenChannel.Default, name);
    }
}