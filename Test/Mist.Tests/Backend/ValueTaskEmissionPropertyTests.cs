// Feature: structured-concurrency, Property 11: ValueTask vs Task Emission
// **Validates: Requirements 10.1, 10.2, 10.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter uses the correct task type
/// (<c>Task&lt;T&gt;</c> vs <c>ValueTask&lt;T&gt;</c>) based on usage analysis.
///
/// **Validates: Requirements 10.1, 10.2, 10.3**
///
/// Since the current implementation is conservative (always emits <c>Task&lt;T&gt;</c>),
/// this test verifies that the emitted C# contains <c>Task&lt;</c> or
/// <c>System.Threading.Tasks.Task</c> for all async methods, and never contains
/// <c>ValueTask&lt;</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public sealed class ValueTaskEmissionPropertyTests
{
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async method with a specific return type and usage pattern.
    /// </summary>
    public record ValueTaskEmissionScenario(
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

    public static Arbitrary<ValueTaskEmissionScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<ValueTaskEmissionScenario> GenScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from isStatic in Gen.Elements(true, false)
               from hasCaller in Gen.Elements(true, false)
               select new ValueTaskEmissionScenario(
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
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveNode };
    }

    private static CompilationUnitNode BuildAst(ValueTaskEmissionScenario scenario)
    {
        // Build the async method
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new VoidTypeRefTypeReferenceNode()
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var modifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        modifiers.Add(new PublicModifierModifierNode());
        if (scenario.IsStatic)
            modifiers.Add(new StaticModifierModifierNode());
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
        if (scenario.HasCaller)
        {
            var callerMethod = BuildCallerMethod(scenario);
            members.Add(new MethodMemberMemberDeclarationNode { Method = callerMethod });
        }

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ClassName),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a caller method that invokes the async method (triggering auto-unwrap in sync context
    /// or explicit await in async context).
    /// </summary>
    private static MethodDeclarationNode BuildCallerMethod(ValueTaskEmissionScenario scenario)
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
        var expr = new ExpressionNode { Assignment = assign };

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

        return new MethodDeclarationNode
        {
            Name = Tok("CallAsync"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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
        ValueTaskEmissionScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Check for build errors
        var buildErrors = buildDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (buildErrors.Count > 0)
            return (false, "", FormatDiagnostics(buildErrors));

        // Run type checker (ignore entry point diagnostic — test programs don't need Main)
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017")
            .ToList();
        if (tcErrors.Count > 0)
            return (false, "", FormatDiagnostics(tcErrors));

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if (emitBag.HasErrors)
            return (false, "", FormatEmitDiagnostics(emitBag));

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach (var (_, content) in files)
            sb.AppendLine(content);

        return (true, sb.ToString(), "");
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
    // Property 11: ValueTask vs Task Emission
    //
    // **Validates: Requirements 10.1, 10.2, 10.3**
    //
    // Since the current implementation is conservative (always emits Task<T>),
    // this test verifies that:
    // 1. The emitted C# contains Task< or System.Threading.Tasks.Task for
    //    async methods (confirming Task<T> is used).
    // 2. The emitted C# does NOT contain ValueTask< (confirming the
    //    conservative behavior).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ValueTaskEmissionPropertyTests) })]
    public Property AsyncMethod_EmitsTaskNotValueTask(ValueTaskEmissionScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileAndEmit(scenario);

        if (!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed: {scenario}\nDiagnostics:\n{diagnosticText}");
        }

        if (string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty: {scenario}");
        }

        // The emitted C# must NOT contain ValueTask< — the conservative implementation
        // always uses Task<T>.
        if (csContent.Contains("ValueTask<"))
        {
            return false.ToProperty()
                .Label($"Emitted C# contains 'ValueTask<' but conservative implementation " +
                       $"should always emit 'Task<T>'.\nScenario: {scenario}\n" +
                       $"Emitted:\n{csContent}");
        }

        // The emitted C# must contain evidence of Task usage — either:
        // - "Task<" (generic Task return type)
        // - "async Task " (non-generic Task for void-returning async methods)
        // - "System.Threading.Tasks.Task" (fully qualified reference)
        var hasTaskReference =
            csContent.Contains("Task<") ||
            csContent.Contains("async Task ") ||
            csContent.Contains("async Task\n") ||
            csContent.Contains("System.Threading.Tasks.Task");

        if (!hasTaskReference)
        {
            return false.ToProperty()
                .Label($"Emitted C# does not contain any Task reference " +
                       $"('Task<', 'async Task ', or 'System.Threading.Tasks.Task').\n" +
                       $"Scenario: {scenario}\nEmitted:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }
}
