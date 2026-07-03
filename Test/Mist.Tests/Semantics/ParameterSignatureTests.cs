using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="ParameterSignature"/>.
/// Tests cover:
/// - Extract: parameterless methods, single-parameter methods, multi-parameter methods,
///   constructors, and abstract type methods.
/// - AreEqual: identical signatures, different signatures, and different-length signatures.
///
/// _Requirements: 1.1, 1.2_
/// </summary>
public class ParameterSignatureTests
{
    private readonly SemanticModelBuilder builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AreEqual Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AreEqual_BothEmpty_ReturnsTrue()
    {
        var a = Array.Empty<IType>();
        var b = Array.Empty<IType>();

        Assert.True(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_IdenticalSingleType_ReturnsTrue()
    {
        var a = new IType[] { LegacyPrimitiveTypes.Int };
        var b = new IType[] { LegacyPrimitiveTypes.Int };

        Assert.True(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_IdenticalMultipleTypes_ReturnsTrue()
    {
        var a = new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Bool };
        var b = new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Bool };

        Assert.True(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_DifferentTypes_ReturnsFalse()
    {
        var a = new IType[] { LegacyPrimitiveTypes.Int };
        var b = new IType[] { LegacyPrimitiveTypes.Bool };

        Assert.False(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_DifferentLengths_ReturnsFalse()
    {
        var a = new IType[] { LegacyPrimitiveTypes.Int };
        var b = new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool };

        Assert.False(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_EmptyVsNonEmpty_ReturnsFalse()
    {
        var a = Array.Empty<IType>();
        var b = new IType[] { LegacyPrimitiveTypes.Int };

        Assert.False(ParameterSignature.AreEqual(a, b));
    }

    [Fact]
    public void AreEqual_SameTypesDifferentOrder_ReturnsFalse()
    {
        var a = new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String };
        var b = new IType[] { LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Int };

        Assert.False(ParameterSignature.AreEqual(a, b));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Extract Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Extract_ParameterlessMethod_ReturnsEmptyList()
    {
        // class C { void M() { } }
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbol = model.AllSymbols.First(s => s.Name == "M" && s.Kind == SymbolKind.Method);
        var signature = ParameterSignature.Extract(methodSymbol, model);

        Assert.Empty(signature);
    }

    [Fact]
    public void Extract_MethodWithOneParameter_ReturnsSingleType()
    {
        // class C { void M(int x) { } }
        var param = MakeParameter("int", "x");
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbol = model.AllSymbols.First(s => s.Name == "M" && s.Kind == SymbolKind.Method);
        var signature = ParameterSignature.Extract(methodSymbol, model);

        _ = Assert.Single(signature);
        Assert.Equal("int", signature[0].DisplayName);
    }

    [Fact]
    public void Extract_ParameterlessConstructor_ReturnsEmptyList()
    {
        // class C { C() { } }
        var ctor = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var ctorSymbol = model.AllSymbols.First(s => s.Name == "C" && s.Kind == SymbolKind.Constructor);
        var signature = ParameterSignature.Extract(ctorSymbol, model);

        Assert.Empty(signature);
    }

    [Fact]
    public void Extract_ConstructorWithOneParameter_ReturnsSingleType()
    {
        // class C { C(int val) { } }
        var param = MakeParameter("int", "val");
        var ctor = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var ctorSymbol = model.AllSymbols.First(s => s.Name == "C" && s.Kind == SymbolKind.Constructor);
        var signature = ParameterSignature.Extract(ctorSymbol, model);

        _ = Assert.Single(signature);
        Assert.Equal("int", signature[0].DisplayName);
    }

    [Fact]
    public void Extract_AbstractMethodWithParameter_ReturnsParameterType()
    {
        // abstract I { void M(int x); }
        // The SemanticModelBuilder registers parameters for abstract type methods
        // so that ParameterSignature.Extract can resolve parameter types for
        // overload duplicate-signature detection.
        var param = MakeParameter("int", "x");
        var abstractMethod = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodNoBodyMethodBodyNode(),
        };

        var abstractDecl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken("I"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = abstractMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = abstractDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbol = model.AllSymbols.First(s => s.Name == "M" && s.Kind == SymbolKind.Method);
        var signature = ParameterSignature.Extract(methodSymbol, model);

        // Abstract type method parameters are now registered, so Extract returns
        // the resolved parameter types.
        _ = Assert.Single(signature);
        Assert.Equal("int", signature[0].DisplayName);
    }

    [Fact]
    public void Extract_TwoMethodsDifferentSignatures_ReturnDistinctSignatures()
    {
        // class C {
        //   void M(int x) { }
        //   void M(bool y) { }
        // }
        var method1 = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "x") },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var method2 = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = MakeParameter("bool", "y") },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method1 },
                new MethodMemberMemberDeclarationNode { Method = method2 }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbols = model.AllSymbols
            .Where(s => s.Name == "M" && s.Kind == SymbolKind.Method)
            .ToList();

        Assert.Equal(2, methodSymbols.Count);

        var sig1 = ParameterSignature.Extract(methodSymbols[0], model);
        var sig2 = ParameterSignature.Extract(methodSymbols[1], model);

        // The two signatures should be different
        Assert.False(ParameterSignature.AreEqual(sig1, sig2));

        // Each should have exactly one parameter type
        _ = Assert.Single(sig1);
        _ = Assert.Single(sig2);

        // One should be "int" and the other "bool"
        var typeNames = new[] { sig1[0].DisplayName, sig2[0].DisplayName };
        Assert.Contains("int", typeNames);
        Assert.Contains("bool", typeNames);
    }

    [Fact]
    public void Extract_TwoMethodsSameSignature_AreEqualReturnsTrue()
    {
        // class C {
        //   void M(int x) { }
        //   void M(int y) { }
        // }
        // Same parameter type, different parameter names — signatures are equal.
        var method1 = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "x") },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var method2 = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = MakeParameter("int", "y") },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method1 },
                new MethodMemberMemberDeclarationNode { Method = method2 }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbols = model.AllSymbols
            .Where(s => s.Name == "M" && s.Kind == SymbolKind.Method)
            .ToList();

        Assert.Equal(2, methodSymbols.Count);

        var sig1 = ParameterSignature.Extract(methodSymbols[0], model);
        var sig2 = ParameterSignature.Extract(methodSymbols[1], model);

        Assert.True(ParameterSignature.AreEqual(sig1, sig2));
    }
}