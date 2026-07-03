// Feature: drop-semantics, Property 11: ValueTask vs Task Emission
// **Validates: Requirements 8.9, 8.10, 8.11, 8.12**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based and example-based tests verifying that the C# emitter uses the correct
/// task type based on whether the return type was inferred or explicitly written.
///
/// **Validates: Requirements 8.9, 8.10, 8.11, 8.12**
///
/// <list type="bullet">
///   <item>Inferred-return async methods (conservative pass) always emit <c>Task&lt;T&gt;</c>.</item>
///   <item>An explicitly written <c>ValueTask&lt;TResult&gt;</c> return type emits
///         <c>System.Threading.Tasks.ValueTask&lt;T&gt;</c>.</item>
///   <item>The <c>ValueTask</c> void alias emits <c>System.Threading.Tasks.ValueTask</c>.</item>
///   <item>Inbound CLR <c>System.Threading.Tasks.ValueTask&lt;T&gt;</c> projects to the
///         Mist <c>ValueTask&lt;TResult&gt;</c> intrinsic type.</item>
/// </list>
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public sealed class ValueTaskEmissionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public ValueTaskEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistValueTaskPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario record for inferred-return property test ───────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async method with an inferred return type.
    /// </summary>
    public record InferredReturnScenario(
        string ClassName,
        string MethodName,
        string ReturnTypeName,
        bool IsStatic,
        bool IsVoid,
        bool HasCaller)
    {
        public override string ToString()
        {
            var staticStr = IsStatic ? "static " : "";
            var retType = IsVoid ? "void" : ReturnTypeName;
            var callerStr = HasCaller ? " + caller" : "";
            return $"class {ClassName} {{ public {staticStr}async {retType} {MethodName}() {{ }} }}{callerStr}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor", "Manager"];
    private static readonly string[] MethodNames = ["Compute", "Fetch", "Process", "Execute", "Load"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool", "float", "void"];

    public static Arbitrary<InferredReturnScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<InferredReturnScenario> GenScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from isStatic in Gen.Elements(true, false)
               from hasCaller in Gen.Elements(true, false)
               select new InferredReturnScenario(
                   className, methodName, returnType, isStatic,
                   returnType == "void", hasCaller);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    private static CompilationUnitNode BuildAst(InferredReturnScenario scenario)
    {
        // Build the async method
        var returnTypeRef = scenario.IsVoid
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var modifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        modifiers.Add(new PublicModifierModifierNode());
        if(scenario.IsStatic)
        {
            modifiers.Add(new StaticModifierModifierNode());
        }

        modifiers.Add(new AsyncModifierModifierNode());

        var asyncMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.MethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = modifiers.ToImmutable(),
        };

        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        members.Add(new MethodMemberMemberDeclarationNode { Method = asyncMethod });

        // Optionally add a caller method that awaits the async method
        if(scenario.HasCaller)
        {
            var callerMethod = BuildCallerMethod(scenario);
            members.Add(new MethodMemberMemberDeclarationNode { Method = callerMethod });
        }

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(scenario.ClassName),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a caller method that invokes the async method (triggering auto-unwrap in sync context
    /// or explicit await in async context).
    /// </summary>
    private static MethodDeclarationNode BuildCallerMethod(InferredReturnScenario scenario)
    {
        // Build a call expression: MethodName()
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.MethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };

        // Wrap in await expression: await MethodName()
        var awaitExpr = new AwaitExpressionUnaryNode
        {
            Operand = new PostfixExpressionUnaryNode { Primary = callPostfix },
        };

        var mult = new MultiplicativeNode { Left = awaitExpr };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

        return new MethodDeclarationNode
        {
            Name = Tok("CallAsync"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(exprStmt),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new AsyncModifierModifierNode()),
        };
    }

    private static (bool Succeeded, string CsContent, string DiagnosticText) CompileAndEmit(
        InferredReturnScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Check for build errors
        var buildErrors = buildDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(buildErrors.Count > 0)
        {
            return (false, "", FormatDiagnostics(buildErrors));
        }

        // Run type checker (ignore entry point diagnostic — test programs don't need Main)
        var tcBag = new DiagnosticBag();
        TypeCheckerAnalyzer typeChecker = new();
        typeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();
        if(tcErrors.Count > 0)
        {
            return (false, "", FormatDiagnostics(tcErrors));
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);

        if(emitBag.HasErrors)
        {
            return (false, "", FormatEmitDiagnostics(emitBag));
        }

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns the emitted user C# file (the one containing the test namespace), or null.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    private static string FormatEmitDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property: Inferred-return async methods emit Task<T>, not ValueTask
    //
    // **Validates: Requirements 8.11, 8.12**
    //
    // The ValueTaskUsageAnalysisPass is conservative for inferred-return
    // methods: it always emits Task<T>. The emitted C# for inferred-return
    // async methods must contain Task references and must NOT contain
    // ValueTask references in the method's async return type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ValueTaskEmissionPropertyTests) })]
    public Property InferredReturnAsyncMethod_EmitsTask_NotValueTask(InferredReturnScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileAndEmit(scenario);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed: {scenario}\nDiagnostics:\n{diagnosticText}");
        }

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty: {scenario}");
        }

        // For inferred-return methods, the emitted async return type must be Task/Task<T>.
        // The emitted C# must contain evidence of Task usage:
        // - "Task<" (generic Task return type)
        // - "async Task " (non-generic Task for void-returning async methods)
        // - "System.Threading.Tasks.Task" (fully qualified reference)
        var hasTaskReference =
            csContent.Contains("Task<") ||
            csContent.Contains("async Task ") ||
            csContent.Contains("async Task\n") ||
            csContent.Contains("System.Threading.Tasks.Task");

        if(!hasTaskReference)
        {
            return false.ToProperty()
                .Label($"Emitted C# does not contain any Task reference " +
                       $"('Task<', 'async Task ', or 'System.Threading.Tasks.Task').\n" +
                       $"Scenario: {scenario}\nEmitted:\n{csContent}");
        }

        // For inferred-return methods, ValueTask must NOT appear as the async return type.
        // We check specifically for "async System.Threading.Tasks.ValueTask" which is the
        // pattern EmitAsyncReturnType would produce if it incorrectly chose ValueTask.
        // Note: ValueTask may legitimately appear in other contexts (e.g., if the method body
        // uses ValueTask), but the method's own return type must be Task.
        var hasValueTaskAsReturnType =
            csContent.Contains("async System.Threading.Tasks.ValueTask<") ||
            csContent.Contains("async System.Threading.Tasks.ValueTask ");

        if(hasValueTaskAsReturnType)
        {
            return false.ToProperty()
                .Label($"Emitted C# contains ValueTask as the async return type, " +
                       $"but conservative inferred-return implementation " +
                       $"should always emit Task<T>.\nScenario: {scenario}\n" +
                       $"Emitted:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit ValueTask<TResult> Return Type Emission
    //
    // **Validates: Requirements 8.10, 8.12**
    //
    // When an async method's return type is explicitly written as
    // ValueTask<TResult>, the emitted C# must contain
    // System.Threading.Tasks.ValueTask<T>.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("int", "System.Threading.Tasks.ValueTask<int>")]
    [InlineData("string", "System.Threading.Tasks.ValueTask<string>")]
    [InlineData("bool", "System.Threading.Tasks.ValueTask<bool>")]
    public void ExplicitValueTaskReturn_EmitsValueTask(string innerType, string expectedCsType)
    {
        // A method with explicit ValueTask<T> return type. The method must be async.
        // Main is non-async so the entry point check passes.
        var source = $$"""
            using Standard;

            namespace TestProgram;

            public class Worker
            {
                public static async ValueTask<{{innerType}}> Compute()
                {
                }

                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        Assert.Contains(expectedCsType, emitted!,
            StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit ValueTask Void Alias Emission
    //
    // **Validates: Requirement 8.10**
    //
    // When an async method's return type is explicitly written as
    // ValueTask (the void alias), the emitted C# must contain
    // System.Threading.Tasks.ValueTask (non-generic).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitValueTaskVoidAlias_EmitsNonGenericValueTask()
    {
        var source = """
            using Standard;

            namespace TestProgram;

            public class Worker
            {
                public static async ValueTask Compute()
                {
                }

                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        // Must contain non-generic ValueTask in the async method signature
        Assert.Contains("System.Threading.Tasks.ValueTask", emitted!, StringComparison.Ordinal);

        // Must NOT contain the generic form for this void alias
        // (the non-generic ValueTask already matched above; verify it's not ValueTask<void>)
        Assert.DoesNotContain("ValueTask<void>", emitted!, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inbound CLR ValueTask Projection
    //
    // **Validates: Requirement 8.9**
    //
    // The IntrinsicTypeMap recognizes System.Threading.Tasks.ValueTask and
    // System.Threading.Tasks.ValueTask<T> and projects them to the Mist
    // ValueTask/ValueTask<TResult> intrinsic linear type.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InboundClrValueTask_ProjectsToMistValueTaskType()
    {
        // Build a minimal AST (just need stdlib loaded to inspect ValueTask)
        var emptyClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Placeholder"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = emptyClass }).AsTopLevelItems(),
        };

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Find ValueTask<TResult> in the model
        var valueTaskSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Class
                && s.Name == "ValueTask"
                && s.DeclaredType is ClassType ct
                && ct.ContainingNamespace == "Standard"
                && ct.IsBackendBacked);

        Assert.NotNull(valueTaskSymbol);

        var valueTaskClassType = (ClassType)valueTaskSymbol!.DeclaredType!;

        // The Mist ValueTask type must be linear (SubstructuralDiscipline.Linear)
        Assert.Equal(SubstructuralDiscipline.Linear, valueTaskClassType.Discipline);

        // It must have Drop() recognized
        Assert.True(valueTaskClassType.HasDrop,
            "ValueTask<TResult>.HasDrop should be true — Drop() must be recognized.");

        // DropReturnType should be TResult (the type parameter)
        Assert.NotNull(valueTaskClassType.DropReturnType);
        _ = Assert.IsType<TypeParameterType>(valueTaskClassType.DropReturnType);

        var dropReturnParam = (TypeParameterType)valueTaskClassType.DropReturnType!;
        Assert.Equal("TResult", dropReturnParam.DisplayName);
    }

    [Fact]
    public void InboundClrValueTask_IntrinsicMapEntry_Exists()
    {
        // Verify the CSharpTypeMap contains the Standard.ValueTask → CLR mapping.
        // This confirms that the outbound emission path knows how to map the Mist
        // ValueTask type to its CLR counterpart (System.Threading.Tasks.ValueTask).
        Assert.True(CSharpTypeMap.BackingTypes.ContainsKey("Standard.ValueTask"),
            "CSharpTypeMap.BackingTypes should contain an entry for Standard.ValueTask");
        Assert.Equal("System.Threading.Tasks.ValueTask",
            CSharpTypeMap.BackingTypes["Standard.ValueTask"]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Reconciliation: Explicit ValueTask takes precedence over UseValueTask
    //
    // **Validates: Requirements 8.11, 8.12**
    //
    // The ValueTaskUsageAnalysisPass only applies to inferred-return methods.
    // An explicitly written ValueTask<TResult> return type takes precedence
    // and is emitted directly via the intrinsic type path.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExplicitValueTask_TakesPrecedence_OverUseValueTaskFlag()
    {
        // This test verifies that HasExplicitValueTaskReturnType correctly identifies
        // methods with explicit ValueTask return types.
        var source = """
            using Standard;

            namespace TestProgram;

            public class Worker
            {
                public async ValueTask<int> DoWork()
                {
                }

                public async int DoInferred()
                {
                }

                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        // The explicit ValueTask<int> method must emit ValueTask<int>
        Assert.Contains("System.Threading.Tasks.ValueTask<int>", emitted!, StringComparison.Ordinal);

        // The inferred-return method must emit Task<int> (not ValueTask)
        Assert.Contains("System.Threading.Tasks.Task<int>", emitted!, StringComparison.Ordinal);
    }
}