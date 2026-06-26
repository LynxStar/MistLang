using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration tests that exercise all three bugfix scenarios through the full
/// compiler pipeline (semantic model → type checker → emitter → dotnet build).
///
/// Bug 1: Multi-parameter constructors/methods — all parameters must be in scope
/// Bug 2: Field initializers — must not silently drop the file's AST
/// Bug 3: string.Length() calls — must resolve to int without crashing
///
/// These tests build hand-crafted ASTs that combine all three scenarios in a
/// multi-file program and verify the full pipeline produces no error diagnostics.
/// </summary>
public sealed class BugfixIntegrationTests : IDisposable
{
    private readonly string outputDir;

    public BugfixIntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistBugfix_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

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
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    private static ExpressionNode MakeMethodCallExpression(string targetName, string methodName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };
        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(methodCallSuffix),
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

    private static ExpressionNode MakeMemberAccessExpression(string targetName, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };
        var memberAccessSuffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken(memberName),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(memberAccessSuffix),
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

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeIdentifierStatement(string name)
    {
        return MakeExpressionStatement(MakeIdentifierExpression(name));
    }

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
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

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        return new UsingDeclarationNode { Name = MakeQualifiedId(namespaceName) };
    }

    private static MistCompilation MakeCompilation(params (string FileName, string Source)[] files)
    {
        // After Task 42, String and Array are stdlib types (not ambient types),
        // so tests that reference string.Length() or array.Length need the stdlib loaded.
        var (stdlibUnits, _) = Helpers.StdlibTestHelper.LoadStdlib();
        var userUnits = files.Select(f => new CompilationUnit(new SourceText(f.FileName, f.Source)))
            .ToImmutableArray();
        var allUnits = stdlibUnits.AddRange(userUnits);
        return new MistCompilation(allUnits);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Integration test: Multi-file program exercising all three bug scenarios
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compiles a multi-file program through the full pipeline (semantic model →
    /// type checker → emitter) that exercises all three bug scenarios:
    ///
    /// File 1 (Geometry.mist): Class with a 2-parameter constructor (Bug 1)
    ///   and a field with initializer token (Bug 2)
    /// File 2 (StringHelper.mist): Class with a method calling string.Length() (Bug 3)
    /// File 3 (Program.mist): Entry point class with Main method
    ///
    /// Asserts no error diagnostics are produced across the full pipeline.
    /// </summary>
    [Fact]
    public void MultiFile_AllThreeBugScenarios_NoErrorDiagnostics()
    {
        // ── File 1: Geometry class — Bug 1 (multi-param ctor) + Bug 2 (field initializer) ──
        var source1 = "using Standard;\nnamespace TestApp;\npublic class Geometry {\n    private int dimension = 2;\n    public Geometry(int x, int y) { this.x = x; this.y = y; }\n}";

        // Constructor with 2 parameters (Bug 1)
        var ctorParam = MakeParameter("int", "x");
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("Geometry"),
            Parameters = new ParameterListNode { Parameter = ctorParam },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIdentifierStatement("x")),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Field with initializer expression (Bug 2)
        var fieldWithInit = new FieldDeclarationNode
        {
            Name = MakeToken("dimension"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Initializer = MakeIntLiteralExpression("0"),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        // Plain field without initializer
        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        var fieldY = new FieldDeclarationNode
        {
            Name = MakeToken("y"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        var geometryClass = new ClassDeclarationNode
        {
            Name = MakeToken("Geometry"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldWithInit },
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new FieldMemberMemberDeclarationNode { Field = fieldY },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast1 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = geometryClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source1.Length),
                }
            }),
        };

        // ── File 2: StringHelper class — Bug 3 (string.Length property access) ──
        var source2 = "using Standard;\nnamespace TestApp;\npublic class StringHelper {\n    public int GetLength(string input) { int len = input.Length; return len; }\n}";

        var stringParam = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("input"),
        };

        // Variable declaration: int len = input.Length;
        var lengthCallExpr = MakeMemberAccessExpression("input", "Length");
        var varDecl = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("len"),
            Initializer = lengthCallExpr,
        };

        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = MakeIdentifierExpression("len"),
            },
        };

        var getLengthMethod = new MethodDeclarationNode
        {
            Name = MakeToken("GetLength"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Parameters = new ParameterListNode { Parameter = stringParam },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new VariableDeclarationStatementStatementNode { VariableDeclaration = varDecl },
                    returnStmt),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var stringHelperClass = new ClassDeclarationNode
        {
            Name = MakeToken("StringHelper"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = getLengthMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = stringHelperClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source2.Length),
                }
            }),
        };

        // ── File 3: Program class with Main entry point ──
        var source3 = "using Standard;\nnamespace TestApp;\npublic class Program {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast3 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source3.Length),
                }
            }),
        };

        // ── Build semantic model with all three files ──
        var compilation = MakeCompilation(
            ("Geometry.mist", source1),
            ("StringHelper.mist", source2),
            ("Program.mist", source3));

        var resolver = new CSharpAmbientTypeResolver();
        var builder = new SemanticModelBuilder();
        var diagnostics = new DiagnosticBag();

        // Include stdlib ASTs so String/Array member lookup works (Task 42).
        var (_, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();
        var allAsts = stdlibAsts.AddRange(ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2, ast3));

        var model = builder.Build(
            compilation,
            allAsts,
            resolver,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        // ── Assert: no error diagnostics from semantic model building ──
        var errors = diagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);

        // ── Assert: all types from all three files are registered ──
        Assert.Contains(model.AllSymbols, s => s.Name == "Geometry" && s.Kind == SymbolKind.Class);
        Assert.Contains(model.AllSymbols, s => s.Name == "StringHelper" && s.Kind == SymbolKind.Class);
        Assert.Contains(model.AllSymbols, s => s.Name == "Program" && s.Kind == SymbolKind.Class);

        // ── Assert: constructor parameter is registered (Bug 1) ──
        Assert.Contains(model.AllSymbols, s => s.Name == "x" && s.Kind == SymbolKind.Parameter);

        // ── Assert: field with initializer didn't drop the AST (Bug 2) ──
        Assert.Contains(model.AllSymbols, s => s.Name == "dimension" && s.Kind == SymbolKind.Field);

        // ── Assert: string.Length resolved without crash (Bug 3) ──
        // The StringHelper class and its method should be present
        Assert.Contains(model.AllSymbols, s => s.Name == "GetLength" && s.Kind == SymbolKind.Method);

        // ── Run type checker — should not produce errors ──
        var typeChecker = new TypeCheckerAnalyzer();
        var tcDiagnostics = new DiagnosticBag();
        typeChecker.Analyze(model, tcDiagnostics);

        var tcErrors = tcDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // No entry point error (MIST_TC_0017) — Program.Main exists
        Assert.DoesNotContain(tcErrors, d => d.Id.Value == "MIST_TC_0017");

        // ── Emit C# — should produce output files ──
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitDiagnostics);

        var csFiles = Directory.GetFiles(outputDir, "*.cs");
        Assert.True(csFiles.Length >= 3, $"Expected at least 3 .cs files, got {csFiles.Length}");
    }

    /// <summary>
    /// Verifies that the emitter produces C# output files without errors
    /// for a multi-file program that exercises all three bug scenarios.
    /// Checks that the emitter itself does not crash and produces .cs files.
    /// </summary>
    [Fact]
    public void MultiFile_AllThreeBugScenarios_EmitterProducesOutputFiles()
    {
        // ── File 1: Class with field initializer (Bug 2) + constructor param (Bug 1) ──
        var source1 = "namespace TestApp;\npublic class Widget {\n    private int count;\n    public Widget(int w) { this.count = w; }\n}";

        var fieldCount = new FieldDeclarationNode
        {
            Name = MakeToken("count"),
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Initializer = MakeIntLiteralExpression("0"),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        var ctorParam = MakeParameter("int", "w");
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken("Widget"),
            Parameters = new ParameterListNode { Parameter = ctorParam },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(MakeIdentifierStatement("w")),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var widgetClass = new ClassDeclarationNode
        {
            Name = MakeToken("Widget"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldCount },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast1 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = widgetClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source1.Length),
                }
            }),
        };

        // ── File 2: Class with string.Length property access (Bug 3) ──
        var source2 = "using Standard;\nnamespace TestApp;\npublic class TextUtil {\n    public int Measure(string text) { int len = text.Length; return len; }\n}";

        var textParam = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                }
            },
            Name = MakeToken("text"),
        };

        var lengthCall = MakeMemberAccessExpression("text", "Length");
        var lenVarDecl = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("len"),
            Initializer = lengthCall,
        };

        var returnLen = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = MakeIdentifierExpression("len"),
            },
        };

        var measureMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Measure"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Parameters = new ParameterListNode { Parameter = textParam },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new VariableDeclarationStatementStatementNode { VariableDeclaration = lenVarDecl },
                    returnLen),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var textUtilClass = new ClassDeclarationNode
        {
            Name = MakeToken("TextUtil"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = measureMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = textUtilClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source2.Length),
                }
            }),
        };

        // ── File 3: Program with Main ──
        var source3 = "namespace TestApp;\npublic class Program {\n    public static void Main() { }\n}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast3 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                {
                    Name = MakeQualifiedId("TestApp"),
                    Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsNamespaceBodyItems(),
                    Span = new Storm.Runtime.SourceSpan(0, source3.Length),
                }
            }),
        };

        // ── Build semantic model ──
        var compilation = MakeCompilation(
            ("Widget.mist", source1),
            ("TextUtil.mist", source2),
            ("Program.mist", source3));

        var resolver = new CSharpAmbientTypeResolver();
        var builder = new SemanticModelBuilder();
        var diagnostics = new DiagnosticBag();

        // Include stdlib ASTs so String/Array member lookup works (Task 42).
        var (_, stdlibAsts2) = Helpers.StdlibTestHelper.LoadStdlib();
        var allAsts2 = stdlibAsts2.AddRange(ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2, ast3));

        var model = builder.Build(
            compilation,
            allAsts2,
            resolver,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        // ── Assert: no error diagnostics from semantic model ──
        var errors = diagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        // ── Emit C# — should produce output files without errors ──
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitDiagnostics);

        var emitErrors = emitDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(emitErrors);

        // Verify output files were produced for all three compilation units
        var csFiles = Directory.GetFiles(outputDir, "*.cs");
        Assert.True(csFiles.Length >= 3, $"Expected at least 3 .cs files, got {csFiles.Length}");

        // Verify .csproj was emitted
        var csprojFiles = Directory.GetFiles(outputDir, "*.csproj");
        Assert.NotEmpty(csprojFiles);
    }
}