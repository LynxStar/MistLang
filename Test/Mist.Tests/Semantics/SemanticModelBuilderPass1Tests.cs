using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

public class SemanticModelBuilderPass1Tests
{
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Helper to create a Token with a given lexeme and span.
    /// </summary>
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

    /// <summary>
    /// Helper to build a minimal MistCompilation with a single source text.
    /// </summary>
    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    /// <summary>
    /// Helper to build the semantic model from a single AST.
    /// </summary>
    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    // ─── Empty program ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyCompilationUnit_ProducesEmptyGlobalScope()
    {
        var ast = new CompilationUnitNode();
        var model = BuildModel(ast);

        // With inferred namespaces, an empty compilation unit still creates
        // the inferred namespace scope ("TestProject") in the global scope.
        // But there should be no user-defined type symbols.
        var nonNamespaceSymbols = model.AllSymbols.Where(s => s.Kind != SymbolKind.Namespace).ToArray();
        Assert.Empty(nonNamespaceSymbols);
    }

    // ─── Class declaration (no namespace) ────────────────────────────────────

    [Fact]
    public void ClassDeclaration_RegistersClassSymbolInGlobalScope()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, types without explicit namespace are registered
        // under the inferred namespace "TestProject", not the global scope directly.
        var nsSymbol = model.GlobalScope.Lookup("TestProject");
        Assert.NotNull(nsSymbol);
        Assert.Equal(SymbolKind.Namespace, nsSymbol!.Kind);

        var classSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "MyClass");
        Assert.NotNull(classSymbol);
        Assert.Equal("MyClass", classSymbol!.Name);
        Assert.Equal(SymbolKind.Class, classSymbol.Kind);
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        _ = Assert.IsType<ClassType>(classSymbol.DeclaredType);
        Assert.Equal("TestProject", ((ClassType)classSymbol.DeclaredType!).ContainingNamespace);
    }

    // ─── Struct declaration (no namespace) ───────────────────────────────────

    [Fact]
    public void StructDeclaration_RegistersStructSymbolInGlobalScope()
    {
        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, types are under the "TestProject" namespace
        var classSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Point");
        Assert.NotNull(classSymbol);
        Assert.Equal("Point", classSymbol!.Name);
        Assert.Equal(SymbolKind.Struct, classSymbol.Kind);
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        _ = Assert.IsType<StructType>(classSymbol.DeclaredType);
        Assert.Equal("TestProject", ((StructType)classSymbol.DeclaredType!).ContainingNamespace);
    }

    // ─── Namespace declaration ───────────────────────────────────────────────

    [Fact]
    public void FileScopedNamespace_RegistersNamespaceSymbolInGlobalScope()
    {
        var source = "namespace MyApp;";
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 5),
        };
        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray<NamespaceBodyItemNode>.Empty,
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        var nsSymbol = model.GlobalScope.Lookup("MyApp");
        Assert.NotNull(nsSymbol);
        Assert.Equal("MyApp", nsSymbol!.Name);
        Assert.Equal(SymbolKind.Namespace, nsSymbol.Kind);
        Assert.Equal(SymbolOrigin.UserCode, nsSymbol.Origin);
    }

    [Fact]
    public void QualifiedNamespace_ExtractsFullName()
    {
        var source = "namespace Foo.Bar.Baz;";
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("Foo", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 11), // "Foo.Bar.Baz"
        };
        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray<NamespaceBodyItemNode>.Empty,
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        var nsSymbol = model.GlobalScope.Lookup("Foo.Bar.Baz");
        Assert.NotNull(nsSymbol);
        Assert.Equal("Foo.Bar.Baz", nsSymbol!.Name);
    }

    // ─── Class inside namespace ──────────────────────────────────────────────

    [Fact]
    public void ClassInsideNamespace_RegistersInNamespaceScope()
    {
        var source = "namespace MyApp; class Greeter { }";
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Greeter", start: 23),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 5),
        };
        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        // The class should NOT be in the global scope directly
        // Global scope contains namespace symbols (MyApp and possibly the inferred TestProject)
        var globalMembers = model.GlobalScope.Members;
        Assert.All(globalMembers, m => Assert.Equal(SymbolKind.Namespace, m.Kind));

        // The class should be findable via scope chain lookup from namespace scope
        var allSymbols = model.AllSymbols;
        Assert.Contains(allSymbols, s => s.Name == "Greeter" && s.Kind == SymbolKind.Class);

        // Verify the class type has the correct namespace
        var classSymbol = allSymbols.First(s => s.Name == "Greeter");
        var classType = classSymbol.DeclaredType as ClassType;
        Assert.NotNull(classType);
        Assert.Equal("MyApp", classType!.ContainingNamespace);
    }

    // ─── Field declarations ──────────────────────────────────────────────────

    [Fact]
    public void FieldDeclaration_RegistersFieldSymbolInTypeScope()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("_value"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var allSymbols = model.AllSymbols;
        var fieldSymbol = allSymbols.FirstOrDefault(s => s.Name == "_value");
        Assert.NotNull(fieldSymbol);
        Assert.Equal(SymbolKind.Field, fieldSymbol!.Kind);
        Assert.Equal(SymbolOrigin.UserCode, fieldSymbol.Origin);
        // Pass 2 resolves the field type from the TypeReference.
        // Post-Task 49: PrimitiveType has been deleted; the fallback
        // produces a StructType with DisplayName "int".
        _ = Assert.IsType<StructType>(fieldSymbol.DeclaredType);
        Assert.Equal("int", fieldSymbol.DeclaredType!.DisplayName);
    }

    // ─── Constructor declarations ────────────────────────────────────────────

    [Fact]
    public void ConstructorDeclaration_RegistersConstructorSymbolInTypeScope()
    {
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var allSymbols = model.AllSymbols;
        var ctorSymbol = allSymbols.FirstOrDefault(s => s.Kind == SymbolKind.Constructor);
        Assert.NotNull(ctorSymbol);
        Assert.Equal("MyClass", ctorSymbol!.Name);
        Assert.Equal(SymbolOrigin.UserCode, ctorSymbol.Origin);
    }

    // ─── Method declarations ─────────────────────────────────────────────────

    [Fact]
    public void MethodDeclaration_RegistersMethodSymbolInTypeScope()
    {
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("DoWork"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var allSymbols = model.AllSymbols;
        var methodSymbol = allSymbols.FirstOrDefault(s => s.Name == "DoWork");
        Assert.NotNull(methodSymbol);
        Assert.Equal(SymbolKind.Method, methodSymbol!.Kind);
        Assert.Equal(SymbolOrigin.UserCode, methodSymbol.Origin);
        // Pass 2 resolves the return type from the TypeReference.
        Assert.True(methodSymbol.DeclaredType!.IsVoid());
    }

    // ─── Multiple members ────────────────────────────────────────────────────

    [Fact]
    public void ClassWithMultipleMembers_RegistersAllMembers()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("_x"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("GetX"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var allSymbols = model.AllSymbols;
        // 1 namespace (TestProject) + 1 class + 3 members = 5 symbols
        Assert.Equal(5, allSymbols.Length);
        Assert.Contains(allSymbols, s => s.Name == "TestProject" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(allSymbols, s => s.Name == "Point" && s.Kind == SymbolKind.Class);
        Assert.Contains(allSymbols, s => s.Name == "_x" && s.Kind == SymbolKind.Field);
        Assert.Contains(allSymbols, s => s.Name == "Point" && s.Kind == SymbolKind.Constructor);
        Assert.Contains(allSymbols, s => s.Name == "GetX" && s.Kind == SymbolKind.Method);
    }

    // ─── Multiple types ──────────────────────────────────────────────────────

    [Fact]
    public void MultipleTypeDeclarations_RegistersAllTypes()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Foo"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Bar"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, types are under the "TestProject" namespace scope.
        // Global scope contains the namespace symbol; types are in the namespace scope.
        var typeSymbols = model.AllSymbols.Where(s => s.Kind != SymbolKind.Namespace).ToArray();
        Assert.Equal(2, typeSymbols.Length);
        Assert.Contains(typeSymbols, s => s.Name == "Foo" && s.Kind == SymbolKind.Class);
        Assert.Contains(typeSymbols, s => s.Name == "Bar" && s.Kind == SymbolKind.Struct);
    }

    // ─── SymbolTable node-to-symbol mapping ──────────────────────────────────

    [Fact]
    public void SymbolTable_MapsAstNodeToSymbol()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var symbol = model.GetDeclaredSymbol(classDecl);
        Assert.NotNull(symbol);
        Assert.Equal("MyClass", symbol!.Name);
        Assert.Equal(SymbolKind.Class, symbol.Kind);
    }

    [Fact]
    public void SymbolTable_MapsSymbolToAstNode()
    {
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("Run"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("App"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var methodSymbol = model.AllSymbols.First(s => s.Name == "Run");
        var declaringNode = model.GetDeclaringNode(methodSymbol);
        Assert.Same(methodDecl, declaringNode);
    }

    // ─── Scope hierarchy ─────────────────────────────────────────────────────

    [Fact]
    public void ScopeHierarchy_MembersAreInTypeScopeNotGlobalScope()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("_value"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, global scope contains the namespace symbol "TestProject"
        _ = Assert.Single(model.GlobalScope.Members);
        Assert.Equal("TestProject", model.GlobalScope.Members[0].Name);
        Assert.Equal(SymbolKind.Namespace, model.GlobalScope.Members[0].Kind);

        // The class should be in the namespace scope, not global scope
        var classSymbol = model.AllSymbols.First(s => s.Name == "MyClass");
        var classType = classSymbol.DeclaredType as ClassType;
        Assert.NotNull(classType);
        var fieldSymbol = classType!.MemberScope.Lookup("_value");
        Assert.NotNull(fieldSymbol);
        Assert.Equal(SymbolKind.Field, fieldSymbol!.Kind);
    }

    [Fact]
    public void ScopeHierarchy_TypeScopeParentIsNamespaceScope()
    {
        var source = "namespace MyApp; class Greeter { }";
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Greeter", start: 23),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 5),
        };
        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        // Find the class symbol
        var classSymbol = model.AllSymbols.First(s => s.Name == "Greeter");
        var classType = classSymbol.DeclaredType as ClassType;
        Assert.NotNull(classType);

        // The type scope's parent should be the namespace scope
        var typeScope = classType!.MemberScope;
        Assert.NotNull(typeScope.Parent);

        // The namespace scope's parent should be the global scope
        var namespaceScope = typeScope.Parent;
        Assert.NotNull(namespaceScope!.Parent);
        Assert.Same(model.GlobalScope, namespaceScope.Parent);
    }

    // ─── Block namespace ─────────────────────────────────────────────────────

    [Fact]
    public void BlockNamespace_RegistersTypesInNamespaceScope()
    {
        var source = "namespace MyApp { class Greeter { } }";
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Greeter", start: 24),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("MyApp", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 5),
        };
        var nsNode = new BlockNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        // Namespace symbol should be in global scope
        var nsSymbol = model.GlobalScope.Lookup("MyApp");
        Assert.NotNull(nsSymbol);
        Assert.Equal(SymbolKind.Namespace, nsSymbol!.Kind);

        // Class should be registered
        var classSymbol = model.AllSymbols.First(s => s.Name == "Greeter");
        Assert.Equal(SymbolKind.Class, classSymbol.Kind);
        var classType = classSymbol.DeclaredType as ClassType;
        Assert.NotNull(classType);
        Assert.Equal("MyApp", classType!.ContainingNamespace);
    }

    // ─── Struct with members ─────────────────────────────────────────────────

    [Fact]
    public void StructWithMembers_RegistersMembersInStructScope()
    {
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("X"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, the struct is under the "TestProject" namespace
        var structSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(structSymbol);
        var structType = structSymbol!.DeclaredType as StructType;
        Assert.NotNull(structType);

        // Members should be in the struct's scope
        var fieldSymbol = structType!.MemberScope.Lookup("X");
        Assert.NotNull(fieldSymbol);
        Assert.Equal(SymbolKind.Field, fieldSymbol!.Kind);

        var ctorSymbol = structType.MemberScope.Members.FirstOrDefault(s => s.Kind == SymbolKind.Constructor);
        Assert.NotNull(ctorSymbol);
    }

    // ─── DeclarationSpan ─────────────────────────────────────────────────────

    [Fact]
    public void UserCodeSymbols_HaveNonNullDeclarationSpan()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass", start: 6),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "MyClass");
        _ = Assert.NotNull(classSymbol.DeclarationSpan);
        Assert.Equal(6, classSymbol.DeclarationSpan!.Value.Start);
        Assert.Equal(7, classSymbol.DeclarationSpan.Value.Length);
    }
}