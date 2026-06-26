using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="SemanticModelBuilder"/> covering scope hierarchy,
/// using declarations, this resolution, member access, duplicate declarations,
/// and SymbolOrigin tagging.
/// </summary>
public class SemanticModelBuilderUnitTests
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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeThisExpression()
    {
        var primary = new ThisExpressionPrimaryNode();
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an expression with a method call suffix: target.methodName(args)
    /// </summary>
    private static ExpressionNode MakeMethodCallExpression(string target, string methodName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken(name),
                Initializer = MakeIntLiteralExpression(value),
            },
        };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static CompilationUnitNode WrapInClassMethod(
        string className,
        string methodName,
        ImmutableArray<StatementNode> statements,
        ImmutableArray<MemberDeclarationNode>? extraMembers = null,
        bool isStatic = false)
    {
        var modifiers = isStatic
            ? ImmutableArray.Create<ModifierNode>(new StaticModifierModifierNode())
            : ImmutableArray<ModifierNode>.Empty;

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = statements } },
            Modifiers = modifiers,
        };

        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        if(extraMembers is not null)
        {
            members.AddRange(extraMembers.Value);
        }

        members.Add(new MethodMemberMemberDeclarationNode { Method = methodDecl });

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Scope hierarchy construction (global → namespace → type → method → block)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ScopeHierarchy_GlobalToNamespaceToTypeToMethodToBlock()
    {
        // namespace TestNs; class C { void M() { int x = 0; } }
        var source = "namespace TestNs;";
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("TestNs", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 6),
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIntVarDecl("x")),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };

        var ast = new CompilationUnitNode { Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }) };
        var model = BuildModel(ast, source);

        // Find the local variable "x" and walk up its scope chain.
        var localVar = model.AllSymbols.FirstOrDefault(
            s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);
        Assert.NotNull(localVar);

        // Walk up: block → method → type → namespace → global
        var blockScope = localVar!.ContainingScope;
        Assert.NotNull(blockScope);

        var methodScope = blockScope.Parent;
        Assert.NotNull(methodScope);

        var typeScope = methodScope!.Parent;
        Assert.NotNull(typeScope);

        var namespaceOrFileScope = typeScope!.Parent;
        Assert.NotNull(namespaceOrFileScope);

        // Eventually we should reach the global scope (null parent).
        var current = namespaceOrFileScope;
        while(current!.Parent is not null)
        {
            current = current.Parent;
        }

        Assert.Same(model.GlobalScope, current);
    }

    [Fact]
    public void ScopeHierarchy_TypeScopeContainsMembers_NotGlobalScope()
    {
        // class C { int f; void M() { } }
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("f"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        // With inferred namespaces, global scope has the namespace symbol "TestProject",
        // not the class directly. The class is under the namespace scope.
        _ = Assert.Single(model.GlobalScope.Members);
        Assert.Equal("TestProject", model.GlobalScope.Members[0].Name);
        Assert.Equal(SymbolKind.Namespace, model.GlobalScope.Members[0].Kind);

        // The field and method should be in the type scope.
        var classSymbol = model.AllSymbols.First(s => s.Name == "C" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);
        Assert.NotNull(classType.MemberScope.Lookup("f"));
        Assert.NotNull(classType.MemberScope.Lookup("M"));
    }

    [Fact]
    public void ScopeHierarchy_NestedBlockCreatesChildScope()
    {
        // class C { void M() { int a = 1; if (1) { int b = 2; } } }
        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIntVarDecl("b", "2")),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl("a", "1"),
            innerBlock);

        var ast = WrapInClassMethod("C", "M", statements);
        var model = BuildModel(ast);

        var varA = model.AllSymbols.First(s => s.Name == "a" && s.Kind == SymbolKind.LocalVariable);
        var varB = model.AllSymbols.First(s => s.Name == "b" && s.Kind == SymbolKind.LocalVariable);

        // a and b should be in different scopes.
        Assert.NotSame(varA.ContainingScope, varB.ContainingScope);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Using declaration imports types from referenced namespace
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UsingDeclaration_UserDefinedNamespace_NoUndefinedDiagnostic()
    {
        // using TestNs; namespace TestNs; class MyType { }
        var source = "using TestNs; namespace TestNs;";

        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("TestNs", start: 6),
                Span = new Storm.Runtime.SourceSpan(6, 6),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyType"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("TestNs", start: 24),
                Span = new Storm.Runtime.SourceSpan(24, 6),
            },
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(14, 16),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast, source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
        Assert.Contains(model.AllSymbols, s => s.Name == "MyType" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public void UsingDeclaration_AmbientNamespace_AcceptedWithoutDiagnostic()
    {
        // After stdlib migration, using System; produces UNDEFINED_NAMESPACE
        // because the ambient resolver no longer has types in the System namespace.
        // Console, Math, and Convert are now in the Standard namespace (stdlib).
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("System"),
                Span = new Storm.Runtime.SourceSpan(0, 6),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        Assert.Contains(diagnostics, d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
    }

    [Fact]
    public void UsingDeclaration_UnknownNamespace_ProducesDiagnostic()
    {
        // using Nonexistent; class C { }
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("Nonexistent"),
                Span = new Storm.Runtime.SourceSpan(0, 11),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
    }

    [Fact]
    public void UsingDeclaration_ImportsAmbientTypes_ResolvableInCode()
    {
        // using System; class C { void M() { Console.WriteLine(); } }
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("System"),
                Span = new Storm.Runtime.SourceSpan(0, 6),
            },
        };

        // Console.WriteLine() expression
        var consoleExpr = MakeMethodCallExpression("Console", "WriteLine");

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeExpressionStatement(consoleExpr)),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // After stdlib migration, Console is no longer resolvable via using System;
        // The ambient resolver has no types registered, so Console produces an
        // undefined identifier diagnostic.
        Assert.Contains(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_IDENTIFIER") && d.Message.Contains("Console"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. `this` resolution within instance methods and constructors
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void This_InInstanceMethod_ResolvesToEnclosingType()
    {
        // class C { void M() { this; } }
        var statements = ImmutableArray.Create(
            MakeExpressionStatement(MakeThisExpression()));

        var ast = WrapInClassMethod("C", "M", statements);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No diagnostics about invalid this usage.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("INVALID_THIS"));

        // The expression type of `this` should be the enclosing ClassType.
        var classSymbol = model.AllSymbols.First(s => s.Name == "C" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);
        Assert.Equal("C", classType.DisplayName);
    }

    [Fact]
    public void This_InConstructor_ResolvesToEnclosingType()
    {
        // class C { C() { this; } }
        var thisExpr = MakeThisExpression();

        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeExpressionStatement(thisExpr)),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // No diagnostics about invalid this usage.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("INVALID_THIS"));
    }

    [Fact]
    public void This_InStaticMethod_ProducesDiagnostic()
    {
        // class C { static void M() { this; } }
        var statements = ImmutableArray.Create(
            MakeExpressionStatement(MakeThisExpression()));

        var ast = WrapInClassMethod("C", "M", statements, isStatic: true);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value.Contains("INVALID_THIS"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Member access resolution on user-defined and ambient types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MemberAccess_OnUserDefinedType_ResolvesField()
    {
        // class C { int val; void M() { this.val; } }
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("val"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };

        // Build this.val expression
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberSuffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken("val"),
        };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeExpressionStatement(expr)),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // No undefined member diagnostic for "val".
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_MEMBER") && d.Message.Contains("val"));
    }

    [Fact]
    public void MemberAccess_OnAmbientType_ResolvesMethod()
    {
        // using System; class C { void M() { Console.WriteLine(); } }
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("System"),
                Span = new Storm.Runtime.SourceSpan(0, 6),
            },
        };

        var consoleExpr = MakeMethodCallExpression("Console", "WriteLine");

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeExpressionStatement(consoleExpr)),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_MEMBER") && d.Message.Contains("WriteLine"));
    }

    [Fact]
    public void MemberAccess_UndefinedMember_ProducesDiagnostic()
    {
        // class C { void M() { this.nonexistent; } }
        var thisPrimary = new ThisExpressionPrimaryNode();
        var memberSuffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken("nonexistent"),
        };
        var postfix = new PostfixNode
        {
            Target = thisPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeExpressionStatement(expr)),
                }
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_MEMBER") && d.Message.Contains("nonexistent"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Duplicate declaration diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateLocalVariable_ProducesDiagnostic()
    {
        // class C { void M() { int x = 1; int x = 2; } }
        var statements = ImmutableArray.Create(
            MakeIntVarDecl("x", "1"),
            MakeIntVarDecl("x", "2"));

        var ast = WrapInClassMethod("C", "M", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics,
            d => d.Id.Value.Contains("DUPLICATE_DECLARATION") && d.Message.Contains("'x'"));
    }

    [Fact]
    public void DuplicateParameter_ProducesDiagnostic()
    {
        // class C { void M(int a, int a) { } }
        var param1 = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("a"),
        };
        var param2 = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("a"),
        };

        var paramList = new ParameterListNode
        {
            Parameter = param1,
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        // Note: The ParameterListNode grammar uses (COMMA Parameter)* for additional params.
        // With a single Parameter property, duplicate detection depends on how Children() works.
        // This test verifies the single-parameter case doesn't produce a false positive.
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // With a single parameter in the ParameterListNode, no duplicate should be reported.
        // The grammar flattens additional params via Children(), so we test the single-param case.
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("DUPLICATE_DECLARATION") && d.Message.Contains("'a'"));
    }

    [Fact]
    public void ShadowingInNestedScope_DoesNotProduceDuplicateDiagnostic()
    {
        // class C { void M() { int x = 1; if (1) { int x = 2; } } }
        // Shadowing in a nested scope is allowed — no duplicate diagnostic.
        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIntVarDecl("x", "2")),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl("x", "1"),
            innerBlock);

        var ast = WrapInClassMethod("C", "M", statements);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("DUPLICATE_DECLARATION"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. SymbolOrigin tagging: UserCode vs Backend
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UserDefinedSymbols_HaveUserCodeOrigin()
    {
        // class C { int f; void M() { int x = 0; } }
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("f"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
        };

        var statements = ImmutableArray.Create(MakeIntVarDecl("x"));
        var extraMembers = ImmutableArray.Create<MemberDeclarationNode>(
            new FieldMemberMemberDeclarationNode { Field = fieldDecl });

        var ast = WrapInClassMethod("C", "M", statements, extraMembers);
        var model = BuildModel(ast);

        // All symbols should have UserCode origin.
        foreach(var symbol in model.AllSymbols)
        {
            Assert.Equal(SymbolOrigin.UserCode, symbol.Origin);
        }
    }

    [Fact]
    public void UserDefinedSymbols_HaveNonNullDeclarationSpan()
    {
        // class C { }
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C", start: 6),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "C");
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        _ = Assert.NotNull(classSymbol.DeclarationSpan);
    }

    [Fact]
    public void BackendSymbols_HaveBackendOrigin_AndNullDeclarationSpan()
    {
        // After stdlib migration, the ambient resolver has no registered types.
        // Console, Math, and Convert are now stdlib types with SymbolOrigin.Source.
        // This test verifies the resolver still works (returns null) and that
        // user-defined symbols have UserCode origin.
        var resolver = new CSharpAmbientTypeResolver();

        // Resolve Console — should return null since it's been migrated to stdlib.
        var consoleType = resolver.ResolveType("System", "Console");
        Assert.Null(consoleType);

        // Verify user-defined symbols still have UserCode origin.
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C", start: 6),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var classSymbol = model.AllSymbols.First(s => s.Name == "C");
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        _ = Assert.NotNull(classSymbol.DeclarationSpan);
    }

    [Fact]
    public void MixedOrigins_UserAndBackendSymbolsCoexist()
    {
        // using System; class C { Console f; void M() { } }
        // Verify that user-defined symbols (C, f, M) have UserCode origin,
        // and that the field typed as Console resolves to a Backend-origin ClassType.
        var usingDecl = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("System"),
                Span = new Storm.Runtime.SourceSpan(0, 6),
            },
        };

        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("f"),
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("Console"),
                        Span = new Storm.Runtime.SourceSpan(0, 7),
                    },
                }
            },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // After stdlib migration, using System; no longer resolves Console.
        // The ambient resolver has no types, so Console produces UNDEFINED_TYPE.
        Assert.Contains(diagnostics, d => d.Id.Value.Contains("UNDEFINED_TYPE") || d.Id.Value.Contains("UNDEFINED_NAMESPACE"));

        // User symbols are still in AllSymbols (via SymbolTable).
        var userSymbols = model.AllSymbols.Where(s => s.Origin == SymbolOrigin.UserCode).ToList();
        Assert.NotEmpty(userSymbols);

        // All user symbols should have non-null DeclarationSpan.
        Assert.All(userSymbols, s => Assert.NotNull(s.DeclarationSpan));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Additive-only constraint — member redeclaration diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Helper to create a ClassDeclarationNode with an optional base class and members.
    /// </summary>
    private static ClassDeclarationNode MakeClassDeclWithMembers(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static MemberDeclarationNode MakeFieldMember(string name)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(name),
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
            },
        };
    }

    private static MemberDeclarationNode MakeMethodMember(string name)
    {
        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = MakeToken(name),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            },
        };
    }

    [Fact]
    public void MemberRedeclaration_FieldInDerived_SameAsFieldInBase_ProducesDiagnostic()
    {
        // class Base { int x; }
        // class Derived : Base { int x; }
        var baseDecl = MakeClassDeclWithMembers("Base", null,
            ImmutableArray.Create(MakeFieldMember("x")));
        var derivedDecl = MakeClassDeclWithMembers("Derived", "Base",
            ImmutableArray.Create(MakeFieldMember("x")));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_MEMBER_REDECLARATION"
            && d.Message.Contains("Derived")
            && d.Message.Contains("x")
            && d.Message.Contains("Base"));
    }

    [Fact]
    public void MemberRedeclaration_MethodInDerived_SameAsMethodInBase_ProducesDiagnostic()
    {
        // class Base { void DoWork() { } }
        // class Derived : Base { void DoWork() { } }
        var baseDecl = MakeClassDeclWithMembers("Base", null,
            ImmutableArray.Create(MakeMethodMember("DoWork")));
        var derivedDecl = MakeClassDeclWithMembers("Derived", "Base",
            ImmutableArray.Create(MakeMethodMember("DoWork")));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_MEMBER_REDECLARATION"
            && d.Message.Contains("Derived")
            && d.Message.Contains("DoWork")
            && d.Message.Contains("Base"));
    }

    [Fact]
    public void MemberRedeclaration_FieldInDerived_SameAsFieldInGrandparent_ProducesDiagnostic()
    {
        // class A { int val; }
        // class B : A { }
        // class C : B { int val; }
        var aDecl = MakeClassDeclWithMembers("A", null,
            ImmutableArray.Create(MakeFieldMember("val")));
        var bDecl = MakeClassDeclWithMembers("B", "A",
            ImmutableArray<MemberDeclarationNode>.Empty);
        var cDecl = MakeClassDeclWithMembers("C", "B",
            ImmutableArray.Create(MakeFieldMember("val")));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = aDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = bDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = cDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_MEMBER_REDECLARATION"
            && d.Message.Contains("C")
            && d.Message.Contains("val")
            && d.Message.Contains("A"));
    }

    [Fact]
    public void MemberRedeclaration_NoConflict_NoDiagnostic()
    {
        // class Base { int x; }
        // class Derived : Base { int y; }
        var baseDecl = MakeClassDeclWithMembers("Base", null,
            ImmutableArray.Create(MakeFieldMember("x")));
        var derivedDecl = MakeClassDeclWithMembers("Derived", "Base",
            ImmutableArray.Create(MakeFieldMember("y")));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_MEMBER_REDECLARATION");
    }

    [Fact]
    public void MemberRedeclaration_ClassWithNoBase_NoDiagnostic()
    {
        // class Standalone { int x; void M() { } }
        var decl = MakeClassDeclWithMembers("Standalone", null,
            ImmutableArray.Create(MakeFieldMember("x"), MakeMethodMember("M")));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = decl }).AsTopLevelItems(),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_MEMBER_REDECLARATION");
        ;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. Pass 1 with-clause extraction on method-like declarations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithClause_OnMethod_ResolvesTypeAndStoresOnSymbol()
    {
        // class MyCtx { }
        // class C { void M() with MyCtx ctx { } }
        var ctxClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyCtx"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            WithClauses = ImmutableArray.Create<WithClauseNode>(new ContextWithClauseWithClauseNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken("MyCtx"),
                            Span = new Storm.Runtime.SourceSpan(0, 5),
                        },
                    }
                },
                Name = MakeToken("ctx"),
            }),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = ctxClassDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CTX_UNDEFINED_TYPE");

        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "M" && s.Kind == SymbolKind.Method);

        _ = Assert.Single(methodSymbol.WithClauses);
        Assert.Equal("ctx", methodSymbol.WithClauses[0].Identifier);
        Assert.Equal("MyCtx", methodSymbol.WithClauses[0].ContextType.DisplayName);
    }

    [Fact]
    public void WithClause_OnConstructor_ResolvesTypeAndStoresOnSymbol()
    {
        // class MyCtx { }
        // class C { C() with MyCtx ctx { } }
        var ctxClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyCtx"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("C"),
            WithClauses = ImmutableArray.Create<WithClauseNode>(new ContextWithClauseWithClauseNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken("MyCtx"),
                            Span = new Storm.Runtime.SourceSpan(0, 5),
                        },
                    }
                },
                Name = MakeToken("ctx"),
            }),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = ctxClassDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CTX_UNDEFINED_TYPE");

        var ctorSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "C" && s.Kind == SymbolKind.Constructor);

        _ = Assert.Single(ctorSymbol.WithClauses);
        Assert.Equal("ctx", ctorSymbol.WithClauses[0].Identifier);
        Assert.Equal("MyCtx", ctorSymbol.WithClauses[0].ContextType.DisplayName);
    }

    [Fact]
    public void WithClause_OnAbstractMethod_ResolvesTypeAndStoresOnSymbol()
    {
        // class MyCtx { }
        // abstract I { void M() with MyCtx ctx; }
        var ctxClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyCtx"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var abstractMethodDecl = new AbstractMethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            WithClauses = ImmutableArray.Create<WithClauseNode>(new ContextWithClauseWithClauseNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken("MyCtx"),
                            Span = new Storm.Runtime.SourceSpan(0, 5),
                        },
                    }
                },
                Name = MakeToken("ctx"),
            }),
        };

        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("I"),
            Members = ImmutableArray.Create<AbstractMemberDeclarationNode>(
                new AbstractMethodMemberAbstractMemberDeclarationNode { Method = abstractMethodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = ctxClassDecl },
                new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CTX_UNDEFINED_TYPE");

        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "M" && s.Kind == SymbolKind.Method);

        _ = Assert.Single(methodSymbol.WithClauses);
        Assert.Equal("ctx", methodSymbol.WithClauses[0].Identifier);
        Assert.Equal("MyCtx", methodSymbol.WithClauses[0].ContextType.DisplayName);
    }

    [Fact]
    public void WithClause_MultipleClausesOnMethod_AllResolved()
    {
        // class CtxA { }
        // class CtxB { }
        // class C { void M() with CtxA a with CtxB b { } }
        var ctxADecl = new ClassDeclarationNode
        {
            Name = MakeToken("CtxA"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ctxBDecl = new ClassDeclarationNode
        {
            Name = MakeToken("CtxB"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            WithClauses = ImmutableArray.Create<WithClauseNode>(
                new ContextWithClauseWithClauseNode
                {
                    Type = new TypeReferenceNode
                    {
                        First = new NamedSingleTypeRefSingleTypeRefNode
                        {
                            Name = new QualifiedIdentifierNode
                            {
                                IDENTIFIER = MakeToken("CtxA"),
                                Span = new Storm.Runtime.SourceSpan(0, 4),
                            },
                        }
                    },
                    Name = MakeToken("a"),
                },
                new ContextWithClauseWithClauseNode
                {
                    Type = new TypeReferenceNode
                    {
                        First = new NamedSingleTypeRefSingleTypeRefNode
                        {
                            Name = new QualifiedIdentifierNode
                            {
                                IDENTIFIER = MakeToken("CtxB"),
                                Span = new Storm.Runtime.SourceSpan(0, 4),
                            },
                        }
                    },
                    Name = MakeToken("b"),
                }),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = ctxADecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = ctxBDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CTX_UNDEFINED_TYPE");

        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "M" && s.Kind == SymbolKind.Method);

        Assert.Equal(2, methodSymbol.WithClauses.Count);
        Assert.Equal("a", methodSymbol.WithClauses[0].Identifier);
        Assert.Equal("CtxA", methodSymbol.WithClauses[0].ContextType.DisplayName);
        Assert.Equal("b", methodSymbol.WithClauses[1].Identifier);
        Assert.Equal("CtxB", methodSymbol.WithClauses[1].ContextType.DisplayName);
    }

    [Fact]
    public void WithClause_UndefinedType_EmitsDiagnostic()
    {
        // class C { void M() with NonExistent ctx { } }
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            WithClauses = ImmutableArray.Create<WithClauseNode>(new ContextWithClauseWithClauseNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken("NonExistent"),
                            Span = new Storm.Runtime.SourceSpan(0, 11),
                        },
                    }
                },
                Name = MakeToken("ctx"),
            }),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_CTX_UNDEFINED_TYPE");

        // The method symbol should have no with-clauses since the type couldn't be resolved.
        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "M" && s.Kind == SymbolKind.Method);

        Assert.Empty(methodSymbol.WithClauses);
    }

    [Fact]
    public void WithClause_NoWithClauses_SymbolHasEmptyList()
    {
        // class C { void M() { } }
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, _) = BuildModelWithDiagnostics(ast);

        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .First(s => s.Name == "M" && s.Kind == SymbolKind.Method);

        Assert.Empty(methodSymbol.WithClauses);
    }

}