using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that field declarations with initializer expressions
/// parse successfully and their types are registered in the semantic model
/// after the Bug 2 fix (field initializer resolution).
///
/// The grammar rule for fields is:
///   rule FieldDeclaration = modifiers:Modifier* type:TypeReference name:IDENTIFIER
///                           initializer:(EQUAL Expression)? SEMICOLON;
///
/// The Storm source generator stores the EQUAL token in the <c>Initializer</c>
/// property (<c>Token?</c>). The actual <c>ExpressionNode</c> is found by
/// walking <c>Children()</c>, matching the pattern used by <c>CSharpEmitter</c>
/// and <c>MistPrettyPrinter</c>.
///
/// Before the fix, a field with an initializer (e.g., <c>private int value = 0;</c>)
/// would cause the entire file's AST to be silently dropped during Pass 2.
/// These tests verify that the fixed <c>ResolveFieldDeclaration</c> correctly
/// handles the optional initializer without crashing.
/// </summary>
public class FieldInitializerTests
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

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    /// <summary>
    /// Creates a field declaration with the Initializer token set (simulating
    /// the EQUAL token from the grammar's optional group). In hand-crafted ASTs,
    /// the ExpressionNode is not directly accessible via Children() — it is only
    /// present when the real parser builds the AST. The key verification is that
    /// the semantic model builder does not crash when Initializer.HasValue is true.
    /// </summary>
    private static FieldDeclarationNode MakeFieldWithInitializer(
        string fieldName,
        PrimitiveTypeNode primitiveType,
        params ModifierNode[] modifiers)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType },
            Initializer = MakeToken("="),  // The EQUAL token — signals initializer is present
            Modifiers = modifiers.Length > 0
                ? ImmutableArray.Create(modifiers)
                : ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static FieldDeclarationNode MakeFieldWithoutInitializer(
        string fieldName,
        PrimitiveTypeNode primitiveType,
        params ModifierNode[] modifiers)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType },
            Modifiers = modifiers.Length > 0
                ? ImmutableArray.Create(modifiers)
                : ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeClassWithMembers(
        string className,
        params MemberDeclarationNode[] members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create(members),
        };

        return new CompilationUnitNode
        {
            Types = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
        };
    }

    private static QualifiedIdentifierNode MakeQualifiedId(string name)
    {
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(name),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Class with `private int value = 0;` — type registered, no crash
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldWithIntInitializer_TypeIsRegistered_NoCrash()
    {
        // class Counter { private int value = 0; }
        var field = MakeFieldWithInitializer(
            "value",
            new IntTypePrimitiveTypeNode(),
            new PrivateModifierModifierNode());

        var ast = MakeClassWithMembers("Counter",
            new FieldMemberMemberDeclarationNode { Field = field });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered in the semantic model
        Assert.Contains(model.AllSymbols, s => s.Name == "Counter" && s.Kind == SymbolKind.Class);

        // The field symbol should exist
        Assert.Contains(model.AllSymbols, s => s.Name == "value" && s.Kind == SymbolKind.Field);

        // No semantic error diagnostics from the field initializer
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_SEMANTIC_ERROR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Class with `private static int count = 0;` — static field works
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StaticFieldWithInitializer_TypeIsRegistered()
    {
        // class Tracker { private static int count = 0; }
        var field = MakeFieldWithInitializer(
            "count",
            new IntTypePrimitiveTypeNode(),
            new PrivateModifierModifierNode(),
            new StaticModifierModifierNode());

        var ast = MakeClassWithMembers("Tracker",
            new FieldMemberMemberDeclarationNode { Field = field });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "Tracker" && s.Kind == SymbolKind.Class);

        // The static field symbol should exist
        Assert.Contains(model.AllSymbols, s => s.Name == "count" && s.Kind == SymbolKind.Field);

        // No semantic errors
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_SEMANTIC_ERROR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Class with `private string name = "hello";` — string initializer
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StringFieldWithInitializer_TypeIsRegistered()
    {
        // class Greeter { private string name = "hello"; }
        var field = MakeFieldWithInitializer(
            "name",
            new StringTypePrimitiveTypeNode(),
            new PrivateModifierModifierNode());

        var ast = MakeClassWithMembers("Greeter",
            new FieldMemberMemberDeclarationNode { Field = field });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "Greeter" && s.Kind == SymbolKind.Class);

        // The string field symbol should exist
        Assert.Contains(model.AllSymbols, s => s.Name == "name" && s.Kind == SymbolKind.Field);

        // No semantic errors
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_SEMANTIC_ERROR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Class with field initializer AND a method — both registered
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldWithInitializerAndMethod_BothRegistered()
    {
        // class Counter {
        //   private int value = 0;
        //   public int GetValue() { return value; }
        // }
        var field = MakeFieldWithInitializer(
            "value",
            new IntTypePrimitiveTypeNode(),
            new PrivateModifierModifierNode());

        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = MakeIdentifierExpression("value"),
            },
        };

        var method = new MethodDeclarationNode
        {
            Name = MakeToken("GetValue"),
            ReturnType = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(returnStmt),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = MakeClassWithMembers("Counter",
            new FieldMemberMemberDeclarationNode { Field = field },
            new MethodMemberMemberDeclarationNode { Method = method });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // The class type should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "Counter" && s.Kind == SymbolKind.Class);

        // Both the field and the method should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "value" && s.Kind == SymbolKind.Field);
        Assert.Contains(model.AllSymbols, s => s.Name == "GetValue" && s.Kind == SymbolKind.Method);

        // No semantic errors from the field initializer
        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_SEMANTIC_ERROR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: Multi-file — class with field initializer is resolvable from
    //         another file
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiFile_ClassWithFieldInitializer_TypeResolvableFromOtherFile()
    {
        // File 1: namespace App; class Counter { private int value = 0; }
        // File 2: namespace App; class Program { public static void Main() { } }
        //
        // Before the fix, the field initializer in Counter would cause the
        // entire file 1 AST to be dropped, making Counter unresolvable.

        var source1 = "namespace App;\nclass Counter { private int value = 0; }";
        var source2 = "namespace App;\nclass Program { public static void Main() { } }";

        // AST for file 1: Counter class with field initializer
        var field = MakeFieldWithInitializer(
            "value",
            new IntTypePrimitiveTypeNode(),
            new PrivateModifierModifierNode());

        var counterClass = new ClassDeclarationNode
        {
            Name = MakeToken("Counter"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = field }),
        };

        var ast1 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("App"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = counterClass }),
                Span = new Storm.Runtime.SourceSpan(0, source1.Length),
            },
        };

        // AST for file 2: Program class with Main
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("App"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }),
                Span = new Storm.Runtime.SourceSpan(0, source2.Length),
            },
        };

        // Build semantic model with both ASTs
        var builder = new SemanticModelBuilder();
        var sourceText1 = new SourceText("counter.mist", source1);
        var sourceText2 = new SourceText("program.mist", source2);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2)));
        var diagnostics = new DiagnosticBag();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            null,
            diagnostics);

        var allDiagnostics = diagnostics.ToImmutableArray();

        // Both types should be registered in the semantic model
        Assert.Contains(model.AllSymbols, s => s.Name == "Counter" && s.Kind == SymbolKind.Class);
        Assert.Contains(model.AllSymbols, s => s.Name == "Program" && s.Kind == SymbolKind.Class);

        // The field should be registered
        Assert.Contains(model.AllSymbols, s => s.Name == "value" && s.Kind == SymbolKind.Field);

        // No MIST_UNDEFINED_TYPE errors — Counter should be resolvable
        Assert.DoesNotContain(allDiagnostics,
            d => d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Counter"));

        // Both ASTs should be preserved (not dropped)
        Assert.Equal(2, model.Asts.Length);
    }
}
