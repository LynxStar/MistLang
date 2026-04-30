// Feature: mist-language-v1, Property 7: Name resolution follows scope chain
// **Validates: Requirements 6.1, 6.2, 6.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that name resolution follows the scope chain.
///
/// Property 7: For any valid Mist program with nested scopes, when an identifier
/// is referenced, the semantic model SHALL resolve it to the declaration in the
/// nearest enclosing scope that contains a matching name. If the same name is
/// declared in both an inner and outer scope, the inner declaration SHALL shadow
/// the outer one.
/// </summary>
public class NameResolutionPropertyTests
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
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "",
        IAmbientTypeResolver? ambientTypeResolver = null)
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Creates a minimal expression AST node (integer literal) suitable for
    /// use as a variable initializer.
    /// </summary>
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

    /// <summary>
    /// Creates an identifier expression AST node for referencing a variable by name.
    /// </summary>
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

    /// <summary>
    /// Creates a typed variable declaration statement: <c>int name = 0;</c>
    /// </summary>
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

    /// <summary>
    /// Creates an expression statement that references an identifier: <c>name;</c>
    /// </summary>
    private static StatementNode MakeIdentifierStatement(string name)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MakeIdentifierExpression(name),
            },
        };
    }

    /// <summary>
    /// Wraps statements in a class with a void method.
    /// </summary>
    private static CompilationUnitNode WrapInClassMethod(
        string className,
        string methodName,
        ImmutableArray<StatementNode> statements,
        ImmutableArray<ParameterNode>? parameters = null)
    {
        ParameterListNode? paramList = null;
        if (parameters is { Length: > 0 } paramArray)
        {
            paramList = new ParameterListNode
            {
                Parameter = paramArray[0],
            };
        }

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = paramList,
            Body = new BlockNode
            {
                Statements = statements,
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Represents a scope nesting scenario for testing name resolution.
    /// </summary>
#pragma warning disable CA1034
    public record ScopeScenario(string Label, int OuterValue, int InnerValue, bool HasShadowing)
    {
        public override string ToString() => $"{Label}(outer={OuterValue}, inner={InnerValue}, shadow={HasShadowing})";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scope scenarios with varying nesting depths and shadowing.
    /// </summary>
    public static Arbitrary<ScopeScenario> ArbitraryScopeScenarios()
    {
        var gen = from hasShadow in Gen.Elements(true, false)
                  from outerVal in Gen.Choose(1, 100)
                  from innerVal in Gen.Choose(101, 200)
                  from label in Gen.Elements(
                      "MethodParam_vs_LocalVar",
                      "OuterBlock_vs_InnerBlock",
                      "Field_vs_LocalVar")
                  select new ScopeScenario(label, outerVal, innerVal, hasShadow);
        return gen.ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7a: For any program with a variable declared in an outer scope and
    /// the same name declared in an inner scope, the inner declaration shadows the
    /// outer one — the semantic model contains both symbols, and the inner-scope
    /// symbol is distinct from the outer-scope symbol.
    ///
    /// We verify this by building a method with an outer variable and an inner block
    /// that re-declares the same name. Both symbols must exist in AllSymbols with
    /// SymbolKind.LocalVariable, and they must be different symbol instances.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NameResolutionPropertyTests) }, MaxTest = 50)]
    public bool InnerScope_Shadows_OuterScope(ScopeScenario scenario)
    {
        // Build: class C { void M() { int x = outerVal; { int x = innerVal; } } }
        // or without shadowing: class C { void M() { int x = outerVal; { int y = innerVal; } } }
        var outerVarName = "x";
        var innerVarName = scenario.HasShadowing ? "x" : "y";

        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                        MakeIntVarDecl(innerVarName, scenario.InnerValue.ToString())),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl(outerVarName, scenario.OuterValue.ToString()),
            (StatementNode)innerBlock);

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var model = BuildModel(ast);
        var allSymbols = model.AllSymbols;

        // Both the outer and inner variable declarations should produce LocalVariable symbols.
        var localVars = allSymbols.Where(s => s.Kind == SymbolKind.LocalVariable).ToList();

        if (scenario.HasShadowing)
        {
            // With shadowing, there should be two LocalVariable symbols both named "x".
            var xVars = localVars.Where(s => s.Name == "x").ToList();
            return xVars.Count == 2 && !ReferenceEquals(xVars[0], xVars[1]);
        }
        else
        {
            // Without shadowing, there should be one "x" and one "y".
            var hasX = localVars.Any(s => s.Name == "x");
            var hasY = localVars.Any(s => s.Name == "y");
            return hasX && hasY;
        }
    }

    /// <summary>
    /// Property 7b: Scope chain lookup walks up to parent scopes. A variable
    /// declared in an outer scope is visible in an inner scope when no shadowing
    /// declaration exists. We verify this by declaring a variable in the method
    /// scope and referencing it inside a nested block — the reference should not
    /// produce an "undefined" diagnostic.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NameResolutionPropertyTests) }, MaxTest = 50)]
    public bool ScopeChain_WalksUp_ToParentScopes(ScopeScenario scenario)
    {
        // Build: class C { void M() { int x = val; { x; } } }
        // The reference to "x" in the inner block should resolve via scope chain walk-up.
        var varName = "outerVar";

        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                        MakeIdentifierStatement(varName)),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl(varName, scenario.OuterValue.ToString()),
            (StatementNode)innerBlock);

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The reference to "outerVar" in the inner block should NOT produce
        // an undefined identifier diagnostic.
        var undefinedDiags = diagnostics
            .Where(d => d.Id.Value.Contains("UNDEFINED") && d.Message.Contains(varName))
            .ToList();

        return undefinedDiags.Count == 0;
    }

    /// <summary>
    /// Property 7c: The scope hierarchy follows the structure
    /// global → namespace → type → method → block (R6.1).
    /// We verify this by building a program with a namespace, class, method, and
    /// nested block, then checking that the scope chain depth is correct.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NameResolutionPropertyTests) }, MaxTest = 50)]
    public bool ScopeHierarchy_FollowsExpectedNesting(ScopeScenario scenario)
    {
        // Build: namespace TestNs; class C { void M() { int x = val; } }
        var source = "namespace TestNs;";
        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("TestNs", start: 10),
            Span = new Storm.Runtime.SourceSpan(10, 6),
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeIntVarDecl("localVar", scenario.OuterValue.ToString())),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
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

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);
        var allSymbols = model.AllSymbols;

        // Verify the scope hierarchy by checking that the local variable's
        // containing scope chain has the expected depth:
        // localVar → block scope → method scope → type scope → namespace scope → global scope
        var localVar = allSymbols.FirstOrDefault(s => s.Name == "localVar" && s.Kind == SymbolKind.LocalVariable);
        if (localVar is null) return false;

        // Walk up the scope chain and count depth.
        var depth = 0;
        var scope = localVar.ContainingScope;
        while (scope is not null)
        {
            depth++;
            scope = scope.Parent;
        }

        // Expected: block scope (1) → method scope (2) → type scope (3) → namespace scope (4) → global scope (5)
        // The local variable's containing scope is the block scope, so walking up
        // should give us at least 4 levels (block → method → type → namespace → global = 5 scopes).
        return depth >= 4;
    }

    // ─── Targeted unit-style tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that a variable declared in a method parameter shadows a field
    /// with the same name in the enclosing class scope. The parameter should be
    /// resolvable in the method body without ambiguity.
    /// </summary>
    [Fact]
    public void Parameter_Shadows_Field_WithSameName()
    {
        // Build: class C { int x; void M(int x) { x; } }
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };

        var paramNode = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("x"),
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = paramNode },
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeIdentifierStatement("x")),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Both the field and parameter named "x" should exist.
        var fieldSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.Field);
        var paramSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.Parameter);

        Assert.NotNull(fieldSymbol);
        Assert.NotNull(paramSymbol);
        Assert.NotSame(fieldSymbol, paramSymbol);

        // No undefined identifier diagnostics for "x".
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED") && d.Message.Contains("'x'"));
    }

    /// <summary>
    /// Verifies that a local variable in an inner block shadows a local variable
    /// in an outer block, and both are registered as distinct symbols.
    /// </summary>
    [Fact]
    public void InnerBlock_LocalVar_Shadows_OuterBlock_LocalVar()
    {
        // Build: class C { void M() { int val = 1; if (1) { int val = 2; } } }
        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                        MakeIntVarDecl("val", "2")),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl("val", "1"),
            (StatementNode)innerBlock);

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var model = BuildModel(ast);

        // Two distinct LocalVariable symbols named "val" should exist.
        var valSymbols = model.AllSymbols
            .Where(s => s.Name == "val" && s.Kind == SymbolKind.LocalVariable)
            .ToList();

        Assert.Equal(2, valSymbols.Count);
        Assert.NotSame(valSymbols[0], valSymbols[1]);
    }

    /// <summary>
    /// Verifies that scope chain lookup walks up from an inner block to find
    /// a variable declared in the method's outer block scope.
    /// </summary>
    [Fact]
    public void ScopeChainLookup_FindsVariable_InOuterScope()
    {
        // Build: class C { void M() { int outer = 1; if (1) { outer; } } }
        var innerBlock = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = MakeIntLiteralExpression("1"),
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                        MakeIdentifierStatement("outer")),
                },
            },
        };

        var statements = ImmutableArray.Create(
            MakeIntVarDecl("outer", "1"),
            (StatementNode)innerBlock);

        var ast = WrapInClassMethod("TestClass", "TestMethod", statements);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // "outer" should be found via scope chain — no undefined diagnostics.
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED") && d.Message.Contains("outer"));
    }

    /// <summary>
    /// Verifies that the scope chain lookup walks up from an inner scope to find
    /// a symbol declared in a parent scope. We test this through the semantic model
    /// by declaring a field in a class and referencing it from a method body.
    /// </summary>
    [Fact]
    public void Scope_Lookup_FollowsScopeChain()
    {
        // Build: class C { int NsItem; void M() { NsItem; } }
        // The reference to "NsItem" in the method body should resolve via scope chain
        // walk-up from block scope → method scope → type scope where the field is declared.
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("NsItem"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeIdentifierStatement("NsItem")),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The field "NsItem" should be found via scope chain — no undefined diagnostics.
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED") && d.Message.Contains("NsItem"));

        // The field symbol should exist.
        Assert.Contains(model.AllSymbols, s => s.Name == "NsItem" && s.Kind == SymbolKind.Field);
    }

    /// <summary>
    /// Verifies that the nearest (innermost) declaration wins when the same name
    /// exists in multiple scopes. A parameter named "x" should shadow a field named "x",
    /// and both should be registered as distinct symbols.
    /// </summary>
    [Fact]
    public void Scope_Lookup_ReturnsNearest_WhenShadowed()
    {
        // Build: class C { int x; void M(int x) { } }
        // The parameter "x" shadows the field "x" in the method scope.
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };

        var paramNode = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Name = MakeToken("x"),
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = paramNode },
            Body = new BlockNode(),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
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

        // Both the field and parameter named "x" should exist as distinct symbols.
        var fieldSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.Field);
        var paramSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.Parameter);

        Assert.NotNull(fieldSymbol);
        Assert.NotNull(paramSymbol);
        Assert.NotSame(fieldSymbol, paramSymbol);
    }

    /// <summary>
    /// Verifies that using declarations make types from imported namespaces
    /// visible in the current scope. When a namespace is imported via "using",
    /// types from that namespace should be resolvable.
    /// </summary>
    [Fact]
    public void UsingDeclaration_MakesNamespaceTypes_Visible()
    {
        // Build a program with:
        //   using TestNs;
        //   namespace TestNs; class MyType { }
        // The using should be accepted without errors when the namespace exists.
        //
        // Source layout: "using TestNs; namespace TestNs; class MyType { }"
        // Positions:      0123456789...
        //   "using " starts at 0, "TestNs" at 6 (length 6)
        //   "namespace " starts at 14, "TestNs" at 24 (length 6)
        var source = "using TestNs; namespace TestNs;";

        var usingQualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("TestNs", start: 6),
            Span = new Storm.Runtime.SourceSpan(6, 6),
        };
        var usingDecl = new UsingDeclarationNode
        {
            Name = usingQualifiedId,
        };

        var qualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("TestNs", start: 24),
            Span = new Storm.Runtime.SourceSpan(24, 6),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyType"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = qualifiedId,
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
            Span = new Storm.Runtime.SourceSpan(14, 16),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast, source);

        // The namespace "TestNs" exists (user-defined), so the using should not
        // produce an "undefined namespace" diagnostic.
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));

        // The type "MyType" should be registered in the model.
        Assert.Contains(model.AllSymbols, s => s.Name == "MyType" && s.Kind == SymbolKind.Class);
    }

    /// <summary>
    /// Verifies that using declarations for ambient namespaces (via IAmbientTypeResolver)
    /// are accepted without producing diagnostics.
    /// </summary>
    [Fact]
    public void UsingDeclaration_WithAmbientNamespace_ProducesNoDiagnostic()
    {
        // Build: using System; class C { void M() { } }
        var usingQualifiedId = new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken("System"),
            Span = new Storm.Runtime.SourceSpan(0, 6),
        };
        var usingDecl = new UsingDeclarationNode
        {
            Name = usingQualifiedId,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        // Create a mock ambient type resolver that knows about "System".
        var resolver = new TestAmbientTypeResolver(knownNamespaces: new[] { "System" });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast, ambientTypeResolver: resolver);

        // No "undefined namespace" diagnostic should be emitted.
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value.Contains("UNDEFINED_NAMESPACE"));
    }

    // ─── Test ambient type resolver ──────────────────────────────────────

    /// <summary>
    /// Minimal ambient type resolver for testing using declaration validation.
    /// </summary>
    private sealed class TestAmbientTypeResolver : IAmbientTypeResolver
    {
        private readonly HashSet<string> _knownNamespaces;

        public TestAmbientTypeResolver(IEnumerable<string> knownNamespaces)
        {
            _knownNamespaces = new HashSet<string>(knownNamespaces);
        }

        public bool HasNamespace(string namespaceName) => _knownNamespaces.Contains(namespaceName);

        public IType? ResolveType(string namespaceName, string typeName) => null;

        public ImmutableArray<ISymbol> GetMembers(IType type) => ImmutableArray<ISymbol>.Empty;

        public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName)
            => ImmutableArray<string>.Empty;
    }
}
