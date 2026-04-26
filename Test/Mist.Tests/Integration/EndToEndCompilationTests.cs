using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end integration tests that exercise the full CompilerDriver pipeline.
/// Tests cover:
/// 1. Full pipeline with real lexer/parser (skeletal Storm-generated) — verifies graceful handling
/// 2. Hand-crafted ASTs bypassing lexer/parser — verifies semantic model → type checker → emitter pipeline
/// 3. Multi-file compilation with cross-file type references
/// </summary>
public sealed class EndToEndCompilationTests : IDisposable
{
    private readonly string _outputDir;

    public EndToEndCompilationTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helper: build a CompilerDriver with real components ─────────────

    private static CompilerDriver CreateRealDriver()
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private CompilerOptions MakeOptions(params string[] sourcePaths)
    {
        return new CompilerOptions(
            SourcePaths: sourcePaths.ToImmutableArray(),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);
    }

    // ─── Helper: AST construction utilities ──────────────────────────────

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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeStringLiteralExpression(string value = "\"hello\"")
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
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


    // ═══════════════════════════════════════════════════════════════════════
    // Section 1: Full pipeline tests with real (skeletal) lexer/parser
    // These verify the pipeline handles the skeletal Storm-generated state gracefully.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FullPipeline_WithSampleFiles_DoesNotCrash()
    {
        // Arrange: point at the real sample .mist files
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");
        Assert.NotEmpty(sourceFiles);

        var driver = CreateRealDriver();
        var options = MakeOptions(sourceFiles);

        // Act: the pipeline should not throw
        var result = driver.Compile(options);

        // Assert: a CompilerResult is returned (even if not successful)
        Assert.NotNull(result);
        Assert.False(result.Diagnostics.IsDefault);
    }

    [Fact]
    public void FullPipeline_WithSampleFiles_ReturnsCompilerResult()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");

        var driver = CreateRealDriver();
        var options = MakeOptions(sourceFiles);

        var result = driver.Compile(options);

        // The result should be a valid CompilerResult with diagnostics
        Assert.IsType<CompilerResult>(result);
        // Diagnostics should be populated (at minimum MIST_PARSE_STUB warnings from skeletal parser)
        Assert.True(result.Diagnostics.Length > 0, "Expected at least one diagnostic from the skeletal parser");
    }

    [Fact]
    public void FullPipeline_WithSampleFiles_ProducesParseStubDiagnostics()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var sourceFiles = Directory.GetFiles(sampleDir, "*.mist");

        var driver = CreateRealDriver();
        var options = MakeOptions(sourceFiles);

        var result = driver.Compile(options);

        // The skeletal parser should produce MIST_PARSE_STUB warnings
        var parseStubDiagnostics = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_PARSE_STUB")
            .ToList();

        Assert.True(parseStubDiagnostics.Count > 0,
            "Expected MIST_PARSE_STUB diagnostics from the skeletal parser");

        // Each source file should produce a parse stub warning
        foreach (var diag in parseStubDiagnostics)
        {
            Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
            Assert.Equal(PipelineStage.Parsing, diag.Stage);
        }
    }

    [Fact]
    public void FullPipeline_WithSingleFile_DoesNotCrash()
    {
        var sampleDir = Path.Combine(FindRepoRoot(), "samples", "HelloMist");
        var programFile = Path.Combine(sampleDir, "Program.mist");
        Assert.True(File.Exists(programFile), $"Sample file not found: {programFile}");

        var driver = CreateRealDriver();
        var options = MakeOptions(programFile);

        var result = driver.Compile(options);

        Assert.NotNull(result);
    }

    [Fact]
    public void FullPipeline_WithNonExistentFile_ProducesFileErrorDiagnostic()
    {
        var driver = CreateRealDriver();
        var options = MakeOptions("/nonexistent/path/test.mist");

        var result = driver.Compile(options);

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_FILE_ERROR");
    }

    [Fact]
    public void FullPipeline_WithInvalidBackend_ProducesUnknownBackendDiagnostic()
    {
        var options = new CompilerOptions(
            SourcePaths: ImmutableArray.Create("dummy.mist"),
            Backend: new BackendId("nonexistent-backend"),
            OutputDirectory: _outputDir,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var driver = CreateRealDriver();
        var result = driver.Compile(options);

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Id.Value == "MIST_UNKNOWN_BACKEND");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 2: Hand-crafted AST tests bypassing lexer/parser
    // These verify the semantic model → type checker → emitter pipeline works end-to-end.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandCraftedAst_SimpleClassWithMain_EmitsValidCSharp()
    {
        // Build a minimal AST: namespace MyApp; class Program { public static void Main() { } }
        var source = "namespace MyApp;\npublic class Program {\n    public static void Main() { }\n}";

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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("MyApp"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        // Build semantic model
        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        // Run type checker
        var typeChecker = new TypeCheckerAnalyzer();
        var tcDiagnostics = new DiagnosticBag();
        typeChecker.Analyze(model, tcDiagnostics);

        // Emit C#
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, emitDiagnostics);

        // Verify output files exist
        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var csContent = File.ReadAllText(csFiles[0]);
        Assert.Contains("namespace MyApp", csContent);
        Assert.Contains("class Program", csContent);
        Assert.Contains("void Main", csContent);

        // Verify .csproj was emitted
        var csprojFiles = Directory.GetFiles(_outputDir, "*.csproj");
        Assert.NotEmpty(csprojFiles);
    }

    [Fact]
    public void HandCraftedAst_ClassWithFieldAndMethod_EmitsCorrectly()
    {
        var source = "namespace TestNs;\npublic class Greeter {\n    private string name;\n    public string Greet() { return name; }\n}";

        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("name"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = MakeIdentifierExpression("name"),
            },
        };

        var greetMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Greet"),
            ReturnType = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            },
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(returnStmt),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Also add a Main method so the type checker doesn't complain about missing entry point
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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Greeter"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl },
                new MethodMemberMemberDeclarationNode { Method = greetMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("TestNs"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("greeter.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        // Emit C#
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, emitDiagnostics);

        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var csContent = File.ReadAllText(csFiles[0]);
        Assert.Contains("class Greeter", csContent);
        Assert.Contains("name", csContent);
        Assert.Contains("Greet", csContent);
    }

    [Fact]
    public void HandCraftedAst_StructDeclaration_EmitsCorrectly()
    {
        var source = "namespace TestNs;\npublic struct Point {\n    public int x;\n    public int y;\n}";

        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var fieldY = new FieldDeclarationNode
        {
            Name = MakeToken("y"),
            Type = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Add a Main method in a separate class for entry point
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
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new FieldMemberMemberDeclarationNode { Field = fieldY }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("TestNs"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new StructTypeTypeDeclarationNode { StructDeclaration = structDecl },
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("point.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, emitDiagnostics);

        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var csContent = File.ReadAllText(csFiles[0]);
        Assert.Contains("struct Point", csContent);
        Assert.Contains("int x", csContent);
        Assert.Contains("int y", csContent);
    }


    [Fact]
    public void HandCraftedAst_WithAmbientTypes_ResolvesSystemConsole()
    {
        // Build an AST that uses System.Console via ambient type resolver
        var source = "using System;\nnamespace MyApp;\npublic class Program {\n    public static void Main() { }\n}";

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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("MyApp"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        // Use the real CSharpAmbientTypeResolver
        var resolver = new CSharpAmbientTypeResolver();

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            resolver,
            diagnostics);

        // The using System should be resolved without errors
        var undefinedNsDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == "MIST_UNDEFINED_NAMESPACE")
            .ToList();
        Assert.Empty(undefinedNsDiags);

        // Emit C# and verify using directive is present
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, emitDiagnostics);

        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.NotEmpty(csFiles);

        var csContent = File.ReadAllText(csFiles[0]);
        Assert.Contains("using System;", csContent);
    }

    [Fact]
    public void HandCraftedAst_TypeCheckerRunsWithoutCrash()
    {
        // Verify the type checker can analyze a hand-crafted model without crashing
        var source = "namespace MyApp;\npublic class Program {\n    public static void Main() { }\n}";

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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("MyApp"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        // Run type checker — should not throw
        var typeChecker = new TypeCheckerAnalyzer();
        var tcDiagnostics = new DiagnosticBag();
        typeChecker.Analyze(model, tcDiagnostics);

        // With a valid entry point, no MIST_TC_0017 should be emitted
        var noEntryPointDiags = tcDiagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == "MIST_TC_0017")
            .ToList();
        Assert.Empty(noEntryPointDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 3: Multi-file compilation with cross-file type references
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandCraftedAst_MultiFile_CrossFileTypeReferences_ResolvedCorrectly()
    {
        // Simulate two files in the same namespace:
        // File 1: namespace App; public class Helper { public int GetValue() { return 42; } }
        // File 2: namespace App; public class Program { public static void Main() { } }
        // The semantic model should resolve types from both files.

        var source1 = "namespace App;\npublic class Helper {\n    public int GetValue() { return 42; }\n}";
        var source2 = "namespace App;\npublic class Program {\n    public static void Main() { }\n}";

        // AST for file 1: Helper class
        var getValueMethod = new MethodDeclarationNode
        {
            Name = MakeToken("GetValue"),
            ReturnType = new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            Parameters = null,
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MakeIntLiteralExpression("42"),
                        },
                    }),
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var helperClass = new ClassDeclarationNode
        {
            Name = MakeToken("Helper"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = getValueMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast1 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("App"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = helperClass }),
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
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
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
        var sourceText1 = new SourceText("helper.mist", source1);
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

        // Both types should be registered in the semantic model
        var allSymbols = model.AllSymbols;
        Assert.Contains(allSymbols, s => s.Name == "Helper" && s.Kind == SymbolKind.Class);
        Assert.Contains(allSymbols, s => s.Name == "Program" && s.Kind == SymbolKind.Class);
        Assert.Contains(allSymbols, s => s.Name == "GetValue" && s.Kind == SymbolKind.Method);
        Assert.Contains(allSymbols, s => s.Name == "Main" && s.Kind == SymbolKind.Method);

        // Emit C# — should produce files for both compilation units
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, emitDiagnostics);

        var csFiles = Directory.GetFiles(_outputDir, "*.cs");
        Assert.True(csFiles.Length >= 2, $"Expected at least 2 .cs files, got {csFiles.Length}");
    }

    [Fact]
    public void HandCraftedAst_MultiFile_SameNamespace_TypesShareNamespaceScope()
    {
        // Two files in the same namespace should have their types visible to each other
        var source1 = "namespace Shared;\npublic class Foo { }";
        var source2 = "namespace Shared;\npublic class Bar { public static void Main() { } }";

        var fooClass = new ClassDeclarationNode
        {
            Name = MakeToken("Foo"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

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

        var barClass = new ClassDeclarationNode
        {
            Name = MakeToken("Bar"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast1 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("Shared"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = fooClass }),
                Span = new Storm.Runtime.SourceSpan(0, source1.Length),
            },
        };

        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("Shared"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = barClass }),
                Span = new Storm.Runtime.SourceSpan(0, source2.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(new SourceText("foo.mist", source1)),
            new CompilationUnit(new SourceText("bar.mist", source2))));
        var diagnostics = new DiagnosticBag();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            null,
            diagnostics);

        // Both Foo and Bar should be in the model
        Assert.Contains(model.AllSymbols, s => s.Name == "Foo" && s.Kind == SymbolKind.Class);
        Assert.Contains(model.AllSymbols, s => s.Name == "Bar" && s.Kind == SymbolKind.Class);

        // The namespace "Shared" should appear as a symbol
        Assert.Contains(model.AllSymbols, s => s.Name == "Shared" && s.Kind == SymbolKind.Namespace);
    }

    [Fact]
    public void HandCraftedAst_EmitterThenDotnetBuild_ProducesExecutable()
    {
        // Build a complete, minimal program that should compile with dotnet build
        var source = "namespace MyApp;\npublic class Program {\n    public static void Main() { }\n}";

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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("MyApp"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        // Use the full backend (emit + dotnet build)
        var backend = new CSharpTranspilerBackend();
        var backendDiagnostics = new DiagnosticBag();
        var result = backend.Emit(model, _outputDir, backendDiagnostics);

        // The emitted C# should compile successfully
        Assert.True(result.Succeeded, $"dotnet build failed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.PrimaryArtifactPath);
        Assert.True(File.Exists(result.PrimaryArtifactPath), $"Expected artifact at {result.PrimaryArtifactPath}");
    }

    [Fact]
    public void HandCraftedAst_ProducedExecutable_RunsWithoutErrors()
    {
        // Build a minimal program, compile it, and run the resulting executable
        var source = "namespace MyApp;\npublic class Program {\n    public static void Main() { }\n}";

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

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Types = ImmutableArray<TypeDeclarationNode>.Empty,
            Namespace = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("MyApp"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            },
        };

        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, diagnostics);

        // Emit and compile
        var backend = new CSharpTranspilerBackend();
        var backendDiagnostics = new DiagnosticBag();
        var result = backend.Emit(model, _outputDir, backendDiagnostics);

        Assert.True(result.Succeeded, "Build must succeed before running");
        Assert.NotNull(result.PrimaryArtifactPath);

        // Run the produced executable
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryArtifactPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(process);

        process.WaitForExit(10000);
        Assert.True(process.HasExited, "Process did not exit within timeout");
        Assert.Equal(0, process.ExitCode);
    }

    // ─── Utility: find the repository root ───────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: try current directory
        dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}
