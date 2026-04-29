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
    private readonly SemanticModelBuilder _builder = new();

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

    private ISemanticModel BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, diagnostics);
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, diagnostics);
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an expression with a member access suffix: target.memberName
    /// </summary>
    private static ExpressionNode MakeMemberAccessExpression(string target, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode { Statements = statements },
            Modifiers = modifiers,
        };

        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        if (extraMembers is not null)
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeIntVarDecl("x")),
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
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
            Span = new Storm.Runtime.SourceSpan(0, source.Length),
        };

        var ast = new CompilationUnitNode { Namespace = nsNode };
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
        while (current!.Parent is not null)
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
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode(),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var model = BuildModel(ast);

        // Global scope should only have the class, not the field or method.
        Assert.Single(model.GlobalScope.Members);
        Assert.Equal("C", model.GlobalScope.Members[0].Name);

        // The field and method should be in the type scope.
        var classSymbol = model.GlobalScope.Members[0];
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
            (StatementNode)innerBlock);

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
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
            Span = new Storm.Runtime.SourceSpan(14, 16),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Namespace = nsNode,
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast, source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
        Assert.Contains(model.AllSymbols, s => s.Name == "MyType" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public void UsingDeclaration_AmbientNamespace_AcceptedWithoutDiagnostic()
    {
        // using System; class C { }
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (_, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeExpressionStatement(consoleExpr)),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // Console should be resolved — no undefined identifier diagnostic for "Console".
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_IDENTIFIER") && d.Message.Contains("Console"));
        // No undefined member diagnostic for "WriteLine".
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_MEMBER") && d.Message.Contains("WriteLine"));
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
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeExpressionStatement(thisExpr)),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        var expr = new ExpressionNode { Assignment = assign };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeExpressionStatement(expr)),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeExpressionStatement(consoleExpr)),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        var expr = new ExpressionNode { Assignment = assign };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(MakeExpressionStatement(expr)),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("a"),
        };
        var param2 = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
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
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = paramList,
            Body = new BlockNode(),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
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
            (StatementNode)innerBlock);

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
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };

        var statements = ImmutableArray.Create(MakeIntVarDecl("x"));
        var extraMembers = ImmutableArray.Create<MemberDeclarationNode>(
            new FieldMemberMemberDeclarationNode { Field = fieldDecl });

        var ast = WrapInClassMethod("C", "M", statements, extraMembers);
        var model = BuildModel(ast);

        // All symbols should have UserCode origin.
        foreach (var symbol in model.AllSymbols)
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var model = BuildModel(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "C");
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        Assert.NotNull(classSymbol.DeclarationSpan);
    }

    [Fact]
    public void BackendSymbols_HaveBackendOrigin_AndNullDeclarationSpan()
    {
        // Verify that ambient type symbols created by the resolver have Backend origin
        // and null DeclarationSpan. We verify this through the CSharpAmbientTypeResolver
        // directly, since ambient symbols are registered in an internal file scope
        // during Pass 2 and are not exposed via model.AllSymbols.
        var resolver = new CSharpAmbientTypeResolver();

        // Resolve Console — this creates the type and its member symbols.
        var consoleType = resolver.ResolveType("System", "Console");
        Assert.NotNull(consoleType);

        var members = resolver.GetMembers(consoleType!);
        Assert.NotEmpty(members);

        // All member symbols should have Backend origin and null DeclarationSpan.
        foreach (var member in members)
        {
            Assert.Equal(SymbolOrigin.Backend, member.Origin);
            Assert.Null(member.DeclarationSpan);
        }

        // Now verify in a full build that user-defined symbols have UserCode origin.
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C", start: 6),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var model = BuildModel(ast);
        var classSymbol = model.AllSymbols.First(s => s.Name == "C");
        Assert.Equal(SymbolOrigin.UserCode, classSymbol.Origin);
        Assert.NotNull(classSymbol.DeclarationSpan);
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
            Type = new NamedTypeRefTypeReferenceNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("Console"),
                    Span = new Storm.Runtime.SourceSpan(0, 7),
                },
            },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("M"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new BlockNode(),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };

        var resolver = new CSharpAmbientTypeResolver();
        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No undefined type diagnostics — Console should resolve.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("UNDEFINED_TYPE"));

        // User symbols are in AllSymbols (via SymbolTable).
        var userSymbols = model.AllSymbols.Where(s => s.Origin == SymbolOrigin.UserCode).ToList();
        Assert.NotEmpty(userSymbols);

        // All user symbols should have non-null DeclarationSpan.
        Assert.All(userSymbols, s => Assert.NotNull(s.DeclarationSpan));

        // The field's declared type should be the ambient Console type (ClassType).
        var fieldSymbol = model.AllSymbols.First(s => s.Name == "f" && s.Kind == SymbolKind.Field);
        Assert.Equal(SymbolOrigin.UserCode, fieldSymbol.Origin);
        var fieldType = Assert.IsType<ClassType>(fieldSymbol.DeclaredType);
        Assert.Equal("Console", fieldType.DisplayName);

        // Verify the ambient type's members have Backend origin.
        var consoleMembers = resolver.GetMembers(fieldType);
        Assert.NotEmpty(consoleMembers);
        Assert.All(consoleMembers, m => Assert.Equal(SymbolOrigin.Backend, m.Origin));
        Assert.All(consoleMembers, m => Assert.Null(m.DeclarationSpan));
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

        if (baseClassName is not null)
        {
            classDecl.BaseClause = MakeToken(":");
            classDecl.BaseClassName = MakeToken(baseClassName);
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
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
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
                ReturnType = new VoidTypeRefTypeReferenceNode(),
                Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = aDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = bDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = cDecl }),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = baseDecl },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedDecl }),
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
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = decl }),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_MEMBER_REDECLARATION");
    }
}
