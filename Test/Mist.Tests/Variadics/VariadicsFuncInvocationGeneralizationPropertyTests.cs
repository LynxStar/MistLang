// Feature: variadics, task 17.7: property test for Func invocation generalization (classic
// pipeline).
//
// Property 15: Func invocation accepted iff arg list matches TArgs.
// **Validates: Requirements 15.3, 15.4**
//
// Asserts:
//   - Invocation of Func<P1,...,PN,TResult> is well-typed iff arg count == N and each arg
//     is positionally assignable (Req 15.3).
//   - Mismatch in arg count emits MIST_TC_0119 (Req 15.3).
//   - Mismatch in arg type emits MIST_TC_0120 (Req 15.3).
//   - Empty pack (zero-arg Func) is accepted with zero arguments (Req 15.4).
//
// Drives end-to-end via StdlibTestHelper + TypeCheckerAnalyzer. All tests run entirely
// in-process with ZERO external process spawns.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// The expected outcome of a Func invocation scenario.
/// </summary>
public enum FuncInvocationOutcome
{
    /// <summary>Arg count and types match TArgs; well-typed, no diagnostic.</summary>
    Accepted,

    /// <summary>Arg count does not match TArgs length; MIST_TC_0119 emitted.</summary>
    ArgCountMismatch,

    /// <summary>Arg count matches but at least one type is wrong; MIST_TC_0120 emitted.</summary>
    TypeMismatch,
}

/// <summary>
/// A generated scenario for testing Func invocation generalization. Specifies the
/// Func type argument list (which determines TArgs and TResult per the binding rule)
/// and the argument expressions supplied at the invocation site.
/// </summary>
public sealed record FuncInvocationScenario(
    IReadOnlyList<string> FuncTypeArgs,
    IReadOnlyList<string> InvocationArgTypes,
    FuncInvocationOutcome ExpectedOutcome)
{
    /// <summary>TArgs length = FuncTypeArgs.Count - 1 (last is TResult).</summary>
    public int ExpectedParamCount => FuncTypeArgs.Count - 1;

    public override string ToString()
    {
        var typeArgStr = string.Join(", ", FuncTypeArgs);
        var argStr = InvocationArgTypes.Count == 0 ? "none" : string.Join(", ", InvocationArgTypes);
        return $"func<{typeArgStr}> invoked with ({argStr}), expect={ExpectedOutcome}";
    }
}

/// <summary>
/// Property test for <b>Property 15 (Func invocation generalization)</b> over the classic
/// pipeline. Generates constructed <c>Func&lt;...&gt;</c> types and invocations, asserting
/// the type checker accepts or rejects per the single generalized rule. MaxTest = 150.
/// <b>Validates: Requirements 15.3, 15.4.</b>
/// </summary>
public sealed class VariadicsFuncInvocationGeneralizationPropertyTests
{
    private const string ArgCountMismatchId = "MIST_TC_0119";
    private const string TypeMismatchDiagId = "MIST_TC_0120";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 15: Func invocation well-typed IFF arg list matches TArgs.
    // **Validates: Requirements 15.3, 15.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsFuncInvocationGeneralizationPropertyTests) }, MaxTest = 150)]
    public bool FuncInvocation_AcceptedIffArgListMatchesTArgs(FuncInvocationScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = GetDiagnostics(source);

        switch(scenario.ExpectedOutcome)
        {
            case FuncInvocationOutcome.Accepted:
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == ArgCountMismatchId);
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == TypeMismatchDiagId);
                break;

            case FuncInvocationOutcome.ArgCountMismatch:
                Assert.Contains(diagnostics, d => d.Id.Value == ArgCountMismatchId);
                break;

            case FuncInvocationOutcome.TypeMismatch:
                Assert.Contains(diagnostics, d => d.Id.Value == TypeMismatchDiagId);
                break;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source builder
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildSource(FuncInvocationScenario scenario)
    {
        var typeArgStr = string.Join(", ", scenario.FuncTypeArgs);

        // Build the lambda parameter list matching TArgs (for the variable declaration).
        var paramCount = scenario.ExpectedParamCount;
        var lambdaParams = paramCount == 0
            ? "()"
            : $"({string.Join(", ", Enumerable.Range(0, paramCount).Select(i => $"p{i}"))})";

        // Build the lambda body (returns a default value of TResult).
        var resultType = scenario.FuncTypeArgs[^1];
        var lambdaBody = DefaultExprForType(resultType);

        // Build the invocation argument list.
        var args = string.Join(", ", scenario.InvocationArgTypes.Select(LiteralForType));

        return $$"""
            class Program
            {
                public static void Main()
                {
                    func<{{typeArgStr}}> f = {{lambdaParams}} => {{lambdaBody}};
                    var result = f({{args}});
                }
            }
            """;
    }

    private static string LiteralForType(string typeName)
    {
        return typeName switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0",
        };
    }

    private static string DefaultExprForType(string typeName)
    {
        return typeName switch
        {
            "int" => "0",
            "string" => "\"\"",
            "bool" => "false",
            _ => "0",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    // Primitive types used for Func type arguments. We use int, string, bool as the
    // three axes: they are mutually non-assignable.
    private static readonly string[] ValueTypes = ["int", "string", "bool"];

    public static Arbitrary<FuncInvocationScenario> FuncInvocationScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<FuncInvocationScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAcceptedScenario(),
            GenEmptyPackAcceptedScenario(),
            GenArgCountMismatchScenario(),
            GenTypeMismatchScenario());
    }

    /// <summary>
    /// Matching invocation: func with 1-3 TArgs params, invocation supplies correct types.
    /// </summary>
    private static Gen<FuncInvocationScenario> GenAcceptedScenario()
    {
        return
            from paramCount in Gen.Choose(1, 3)
            from paramTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), paramCount)
            from resultType in Gen.Elements(ValueTypes)
            let funcTypeArgs = paramTypes.Append(resultType).ToArray()
            select new FuncInvocationScenario(
                funcTypeArgs, paramTypes, FuncInvocationOutcome.Accepted);
    }

    /// <summary>Empty pack: func&lt;TResult&gt; invoked with zero arguments -> accepted.</summary>
    private static Gen<FuncInvocationScenario> GenEmptyPackAcceptedScenario()
    {
        return
            from resultType in Gen.Elements(ValueTypes)
            select new FuncInvocationScenario(
                new[] { resultType }, Array.Empty<string>(), FuncInvocationOutcome.Accepted);
    }

    /// <summary>
    /// Arg count mismatch: func with N TArgs params, invocation supplies M != N args.
    /// </summary>
    private static Gen<FuncInvocationScenario> GenArgCountMismatchScenario()
    {
        return
            from paramCount in Gen.Choose(1, 3)
            from paramTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), paramCount)
            from resultType in Gen.Elements(ValueTypes)
            from wrongArgCount in Gen.Elements(
                Enumerable.Range(0, paramCount).Concat(
                    Enumerable.Range(paramCount + 1, 3)).ToArray())
            from wrongArgs in Gen.ArrayOf(Gen.Elements(ValueTypes), wrongArgCount)
            let funcTypeArgs = paramTypes.Append(resultType).ToArray()
            select new FuncInvocationScenario(
                funcTypeArgs, wrongArgs, FuncInvocationOutcome.ArgCountMismatch);
    }

    /// <summary>
    /// Type mismatch: func with N TArgs params, invocation supplies N args but at least one
    /// has the wrong type.
    /// </summary>
    private static Gen<FuncInvocationScenario> GenTypeMismatchScenario()
    {
        return
            from paramCount in Gen.Choose(1, 3)
            from paramTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), paramCount)
            from resultType in Gen.Elements(ValueTypes)
            from badIndex in Gen.Choose(0, paramCount - 1)
            let funcTypeArgs = paramTypes.Append(resultType).ToArray()
            let invocationArgs = BuildArgsWithTypeMismatch(paramTypes, badIndex)
            select new FuncInvocationScenario(
                funcTypeArgs, invocationArgs, FuncInvocationOutcome.TypeMismatch);
    }

    private static string[] BuildArgsWithTypeMismatch(string[] paramTypes, int badIndex)
    {
        var result = (string[])paramTypes.Clone();
        // Pick a different type for the bad position.
        var current = result[badIndex];
        result[badIndex] = ValueTypes.First(t => t != current);
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pipeline helper
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var sourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(sourceText);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(parseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }
}