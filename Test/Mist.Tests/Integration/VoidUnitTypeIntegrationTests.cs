// Feature: void-unit-type, Task 9.1: End-to-end compilation integration tests
// Requirements: 5.3, 10.1, 10.2, 10.3, 10.4, 10.5

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the void unit type feature.
/// These tests exercise the full compiler pipeline: AST → semantic model → type checker → C# emitter,
/// and verify the generated C# code is correct.
///
/// Validates: Requirements 5.3, 10.1, 10.2, 10.3, 10.4, 10.5
/// </summary>
public sealed class VoidUnitTypeIntegrationTests : IDisposable
{
    private readonly string outputDir;

    public VoidUnitTypeIntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVoidE2E_" + Guid.NewGuid().ToString("N"));
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

    private static Storm.Runtime.Token Tok(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static ExpressionNode MakeVoidDotVoidExpression()
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok("Void") };
        var suffix = new MemberAccessSuffixPostfixSuffixNode { Name = Tok("Void") };
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

    private static ExpressionNode MakeMethodCallExpression(string methodName, ExpressionNode[]? args = null)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(methodName) };
        ArgumentListNode? argList = null;
        if(args is { Length: > 0 })
        {
            argList = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = args[0] } };
        }

        var suffix = new InvocationSuffixPostfixSuffixNode { Arguments = argList };
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

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeInferredVarDeclStatement(string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                TypeArgs = typeArgs,
            }
        };
    }

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode>? body = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] types)
    {
        return new CompilationUnitNode
        {
            Items = types.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Runs the full pipeline: build semantic model → run type checker → emit C#.
    /// Returns the generated C# content and any diagnostics.
    /// </summary>
    private (string CsContent, ImmutableArray<Diagnostic> BuildDiagnostics, ImmutableArray<Diagnostic> TypeCheckDiagnostics) RunFullPipeline(
        CompilationUnitNode ast, string source)
    {
        // Step 1: Build semantic model
        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var buildDiagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiagnostics);

        // Step 2: Run type checker
        var typeChecker = new TypeCheckerAnalyzer();
        var tcDiagnostics = new DiagnosticBag();
        typeChecker.Analyze(model, tcDiagnostics);

        // Step 3: Emit C#
        var emitDiagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitDiagnostics);

        // Return the main source file content (not MistVoid.g.cs or .csproj)
        var csFile = Directory.GetFiles(outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs", StringComparison.Ordinal));
        var csContent = csFile is not null ? File.ReadAllText(csFile) : "";

        return (csContent, buildDiagnostics.ToImmutableArray(), tcDiagnostics.ToImmutableArray());
    }

    private bool MistVoidFileExists()
    {
        return File.Exists(Path.Combine(outputDir, "MistVoid.g.cs"));
    }

    private string GetMistVoidFileContent()
    {
        var mistVoidPath = Path.Combine(outputDir, "MistVoid.g.cs");
        return File.Exists(mistVoidPath) ? File.ReadAllText(mistVoidPath) : "";
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: End-to-end compilation of program using Result<void>
    // Validates: Requirement 5.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A generic class Result&lt;T&gt; with a T field, instantiated with void as the type argument,
    /// compiles through the full pipeline without errors and the generated C# uses MistVoid
    /// for the type argument.
    /// </summary>
    [Fact]
    public void EndToEnd_ResultOfVoid_CompilesAndEmitsMistVoid()
    {
        CleanOutputDir();

        // class Result<T> { T value; }
        var genericField = new FieldDeclarationNode
        {
            Name = Tok("value"),
            Type = MakeNamedTypeRef("T"),
        };

        var resultClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Result"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = genericField }),
        };

        // class Program {
        //   Result<void> result;
        //   public static void Main() { }
        // }
        var typeArgList = MakeTypeArgList([new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }]);
        var fieldTypeRef = MakeNamedTypeRef("Result", typeArgList);
        var resultFieldMember = new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = Tok("result"),
                Type = fieldTypeRef,
            },
        };

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                resultFieldMember,
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() }),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = resultClass },
            new BodyTypeTypeDeclarationNode { TypeBody = programClass });

        var source = "class Result<T> { T value; } class Program { Result<void> result; public static void Main() { } }";

        var (csContent, buildDiags, tcDiags) = RunFullPipeline(ast, source);

        // No errors in semantic model building
        var buildErrors = buildDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(buildErrors);

        // No errors in type checking
        var tcErrors = tcDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(tcErrors);

        // The generated C# should use MistVoid for the void type argument
        Assert.Contains("Result<MistVoid>", csContent);

        // The T field in Result<T> should be emitted (the generic definition uses T)
        Assert.Contains("T value", csContent);

        // The MistVoid struct should be emitted in a separate shared file
        Assert.True(MistVoidFileExists());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Generated C# compiles and runs with MistVoid struct
    // Validates: Requirements 10.1, 10.2, 10.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A program using Void.Void in a variable declaration goes through the full pipeline
    /// and produces valid C# with the MistVoid struct that compiles with dotnet build.
    /// </summary>
    [Fact]
    public void EndToEnd_VoidDotVoidVariable_ProducesCompilableCSharp()
    {
        CleanOutputDir();

        // class Program {
        //   public static void Main() { var x = Void.Void; }
        // }
        var varDecl = MakeInferredVarDeclStatement("x", MakeVoidDotVoidExpression());
        var mainMethod = MakeMainMethod(ImmutableArray.Create(varDecl));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = programClass });

        var source = "class Program { public static void Main() { var x = Void.Void; } }";

        var (csContent, buildDiags, tcDiags) = RunFullPipeline(ast, source);

        // No errors in the pipeline
        var buildErrors = buildDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(buildErrors);
        var tcErrors = tcDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(tcErrors);

        // Verify the generated C# contains MistVoid usage
        Assert.Contains("MistVoid.Instance", csContent);
        Assert.Contains("var x = MistVoid.Instance;", csContent);

        // Verify the MistVoid struct is emitted in a separate shared file with all required members
        var mistVoidContent = GetMistVoidFileContent();
        Assert.NotEmpty(mistVoidContent);
        Assert.Contains("public readonly struct MistVoid : System.IEquatable<MistVoid>", mistVoidContent);
        Assert.Contains("public static readonly MistVoid Instance = default;", mistVoidContent);
        Assert.Contains("public bool Equals(MistVoid other) => true;", mistVoidContent);
        Assert.Contains("public override bool Equals(object? obj) => obj is MistVoid;", mistVoidContent);
        Assert.Contains("public override int GetHashCode() => 0;", mistVoidContent);
        Assert.Contains("public static bool operator ==(MistVoid left, MistVoid right) => true;", mistVoidContent);
        Assert.Contains("public static bool operator !=(MistVoid left, MistVoid right) => false;", mistVoidContent);
        Assert.Contains("public override string ToString() => \"Void\";", mistVoidContent);

        // Now verify the generated C# actually compiles with dotnet build
        var backend = new CSharpTranspilerBackend();
        var backendDiagnostics = new DiagnosticBag();
        var binDir = Path.Combine(outputDir, "bin");

        // Re-run the full pipeline through the backend to get dotnet build
        var builder = new SemanticModelBuilder();
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diags = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diags);

        // Clean output dir for fresh emit
        CleanOutputDir();
        var result = backend.Emit(model, outputDir, binDir, "TestProject", backendDiagnostics);

        Assert.True(result.Succeeded,
            $"dotnet build failed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Fire-and-forget void method emits C# void
    // Validates: Requirement 10.4
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A void method called as a standalone expression statement (fire-and-forget)
    /// goes through the full pipeline and emits standard C# void, not MistVoid.
    /// </summary>
    [Fact]
    public void EndToEnd_FireAndForgetVoidMethod_EmitsCSharpVoid()
    {
        CleanOutputDir();

        // class Program {
        //   public void DoWork() { return; }
        //   public static void Main() { DoWork(); }
        // }
        var doWorkMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var callExpr = MakeMethodCallExpression("DoWork");
        var callStmt = MakeExpressionStatement(callExpr);
        var mainMethod = MakeMainMethod(ImmutableArray.Create(callStmt));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = doWorkMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = programClass });

        var source = "class Program { public void DoWork() { return; } public static void Main() { DoWork(); } }";

        var (csContent, buildDiags, tcDiags) = RunFullPipeline(ast, source);

        // No errors in the pipeline
        var buildErrors = buildDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(buildErrors);
        var tcErrors = tcDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(tcErrors);

        // Fire-and-forget: should emit standard C# void, not MistVoid
        Assert.Contains("public void DoWork()", csContent);
        Assert.DoesNotContain("MistVoid DoWork", csContent);

        // No MistVoid file should be emitted since void is not used as a value
        Assert.False(MistVoidFileExists());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Value-returning void method emits MistVoid
    // Validates: Requirement 10.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A void method whose return value is consumed (assigned to a variable)
    /// goes through the full pipeline and emits MistVoid return type with
    /// return MistVoid.Instance in the method body.
    /// </summary>
    [Fact]
    public void EndToEnd_ValueReturningVoidMethod_EmitsMistVoid()
    {
        CleanOutputDir();

        // class Program {
        //   public void DoWork() { return; }
        //   public static void Main() { var result = DoWork(); }
        // }
        var doWorkMethod = new MethodDeclarationNode
        {
            Name = Tok("DoWork"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var callExpr = MakeMethodCallExpression("DoWork");
        var varDecl = MakeInferredVarDeclStatement("result", callExpr);
        var mainMethod = MakeMainMethod(ImmutableArray.Create(varDecl));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = doWorkMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = programClass });

        var source = "class Program { public void DoWork() { return; } public static void Main() { var result = DoWork(); } }";

        var (csContent, buildDiags, tcDiags) = RunFullPipeline(ast, source);

        // No errors in the pipeline
        var buildErrors = buildDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(buildErrors);
        var tcErrors = tcDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(tcErrors);

        // Value-returning: should emit MistVoid return type
        Assert.Contains("public MistVoid DoWork()", csContent);

        // The method body should have return MistVoid.Instance
        Assert.Contains("return MistVoid.Instance;", csContent);

        // The variable declaration should use var
        Assert.Contains("var result = DoWork();", csContent);

        // The MistVoid struct should be emitted in a separate shared file
        Assert.True(MistVoidFileExists());
    }
}