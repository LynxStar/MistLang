// Feature: owned-fields, Task 18.1 — Property 1: This-tier inference soundness
//
// For all methods m with owned-field accesses, the inferred this-tier is the minimum
// tier that satisfies all field operations in the body:
//   inferred == MaxTierFromFieldOps(m.Body)
//
// Uses in-process CompilerDriver.Compile() — no per-iteration process spawning.
// Exhaustive Theory-based approach covering all meaningful combinations of
// owned-field operations (read, write, none) across 1–2 fields.
//
// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for this-tier inference soundness (Property 1, Req 17 AC 1–6).
///
/// The property under test:
///   ∀ method m: InferThisTier(m) == MaxTierFromFieldOps(m.Body)
///
/// where MaxTierFromFieldOps computes the minimum tier that satisfies all owned-field
/// operations using the mapping:
///   - Write/Move → Borrowed (Req 17.2)
///   - Read → ReadonlyRef (Req 17.4)
///   - No access → None (Req 17.5)
///   - Composition: most restrictive wins (Req 17.6)
///
/// The test enumerates all combinations of field access patterns (None, Read, Write)
/// for methods with 1 or 2 owned fields, verifying the inferred tier against the
/// expected computation for each combination.
///
/// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6**
/// </summary>
public sealed class ThisTierInferenceSoundnessPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// The kinds of owned-field operations a method body may perform.
    /// </summary>
    public enum FieldOp
    {
        /// <summary>No access to this owned field in the method body.</summary>
        None,

        /// <summary>The method reads this owned field (e.g., var x = this.field).</summary>
        Read,

        /// <summary>The method writes to this owned field (e.g., this.field = value).</summary>
        Write,
    }

    /// <summary>
    /// Computes the expected ThisTier for a single field operation.
    /// </summary>
    private static ThisTier TierForOp(FieldOp op) => op switch
    {
        FieldOp.None => ThisTier.None,
        FieldOp.Read => ThisTier.ReadonlyRef,
        FieldOp.Write => ThisTier.Borrowed,
        _ => ThisTier.None,
    };

    /// <summary>
    /// Computes the expected this-tier as the maximum across all field operations
    /// (most restrictive wins — Req 17.6).
    /// </summary>
    private static ThisTier ExpectedTier(params FieldOp[] ops)
    {
        var max = ThisTier.None;
        foreach(var op in ops)
        {
            var tier = TierForOp(op);
            if(tier > max)
            {
                max = tier;
            }
        }
        return max;
    }

    /// <summary>
    /// Generates the body line(s) for a given field operation on a named owned field.
    /// </summary>
    private static string GenerateBodyLine(FieldOp op, string fieldName) => op switch
    {
        FieldOp.None => "",
        FieldOp.Read => $"int _r_{fieldName} = this.{fieldName};",
        FieldOp.Write => $"this.{fieldName} = 42;",
        _ => "",
    };

    /// <summary>
    /// Builds a Mist source program with a class containing the specified owned fields
    /// and a method performing the given operations on them.
    /// </summary>
    private static string BuildSource(FieldOp[] ops, string[] fieldNames)
    {
        var fieldDecls = string.Join("\n    ", fieldNames.Select(f => $"owned int {f};"));
        var bodyLines = new List<string>();
        for(var i = 0; i < ops.Length; i++)
        {
            var line = GenerateBodyLine(ops[i], fieldNames[i]);
            if(!string.IsNullOrEmpty(line))
            {
                bodyLines.Add(line);
            }
        }

        var body = bodyLines.Count > 0
            ? string.Join("\n            ", bodyLines)
            : "int _unused = 0;";

        return $$"""
            class Container
            {
                {{fieldDecls}}
                void Target()
                {
                    {{body}}
                }
            }
            """;
    }

    /// <summary>
    /// Builds the semantic model from source, runs ThisTierInferencePass.Analyze(),
    /// and returns the inferred tier for the "Target" method.
    /// </summary>
    private static ThisTier? AnalyzeAndGetTier(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return null;
        }

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return null;
        }

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var analysisBag = new DiagnosticBag();
        ThisTierInferencePass.Analyze(model, analysisBag);

        var targetMethod = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Target" && s.Kind == SymbolKind.Method);

        if(targetMethod is null)
        {
            return null;
        }

        return ThisTierInferencePass.GetThisTier(targetMethod);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Single field: exhaustive over {None, Read, Write}
    //
    // 3 test cases covering Req 17.2, 17.4, 17.5
    // ═══════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> SingleFieldCombinations()
    {
        foreach(var op in Enum.GetValues<FieldOp>())
        {
            yield return new object[] { op };
        }
    }

    /// <summary>
    /// For a method with a single owned field, the inferred this-tier must equal
    /// the tier required by the single operation on that field.
    ///
    /// **Validates: Requirements 17.2, 17.4, 17.5**
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleFieldCombinations))]
    public void SingleField_InferredTierMatchesExpected(FieldOp op)
    {
        var source = BuildSource(new[] { op }, new[] { "fieldA" });
        var expected = ExpectedTier(op);

        var actual = AnalyzeAndGetTier(source);

        _ = Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Two fields: exhaustive over {None, Read, Write} × {None, Read, Write}
    //
    // 9 test cases covering Req 17.1, 17.2, 17.4, 17.5, 17.6
    // The composition property: inferred == Max(TierForOp(opA), TierForOp(opB))
    // ═══════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> TwoFieldCombinations()
    {
        foreach(var opA in Enum.GetValues<FieldOp>())
        {
            foreach(var opB in Enum.GetValues<FieldOp>())
            {
                yield return new object[] { opA, opB };
            }
        }
    }

    /// <summary>
    /// For a method with two owned fields, the inferred this-tier must equal the
    /// maximum of the tiers required by the operations on each field (most restrictive wins).
    ///
    /// **Validates: Requirements 17.1, 17.2, 17.4, 17.5, 17.6**
    /// </summary>
    [Theory]
    [MemberData(nameof(TwoFieldCombinations))]
    public void TwoFields_InferredTierIsMaxOfOperations(FieldOp opA, FieldOp opB)
    {
        var source = BuildSource(new[] { opA, opB }, new[] { "fieldA", "fieldB" });
        var expected = ExpectedTier(opA, opB);

        var actual = AnalyzeAndGetTier(source);

        _ = Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Three fields: exhaustive over all 27 combinations
    //
    // Validates that composition generalizes beyond 2 fields (Req 17.6).
    // ═══════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> ThreeFieldCombinations()
    {
        foreach(var opA in Enum.GetValues<FieldOp>())
        {
            foreach(var opB in Enum.GetValues<FieldOp>())
            {
                foreach(var opC in Enum.GetValues<FieldOp>())
                {
                    yield return new object[] { opA, opB, opC };
                }
            }
        }
    }

    /// <summary>
    /// For a method with three owned fields, the inferred this-tier must equal the
    /// maximum of all three field operations. Validates generalization of composition.
    ///
    /// **Validates: Requirements 17.1, 17.2, 17.4, 17.5, 17.6**
    /// </summary>
    [Theory]
    [MemberData(nameof(ThreeFieldCombinations))]
    public void ThreeFields_InferredTierIsMaxOfAllOperations(FieldOp opA, FieldOp opB, FieldOp opC)
    {
        var source = BuildSource(new[] { opA, opB, opC }, new[] { "fieldA", "fieldB", "fieldC" });
        var expected = ExpectedTier(opA, opB, opC);

        var actual = AnalyzeAndGetTier(source);

        _ = Assert.NotNull(actual);
        Assert.Equal(expected, actual!.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Transitive composition: method calls a helper on this
    //
    // Validates that when a method calls another method on `this`, the caller's
    // inferred tier is at least as high as the callee's (Req 17.7 — included
    // here because it's part of the "minimum tier" soundness guarantee).
    //
    // **Validates: Requirements 17.1, 17.6**
    // ═══════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> TransitiveCompositionCases()
    {
        // (helperOp, callerOp) → caller tier should be Max(tierForHelperOp, tierForCallerOp)
        foreach(var helperOp in Enum.GetValues<FieldOp>())
        {
            foreach(var callerOp in Enum.GetValues<FieldOp>())
            {
                yield return new object[] { helperOp, callerOp };
            }
        }
    }

    /// <summary>
    /// When a method calls a helper on <c>this</c>, the caller's inferred tier
    /// is at least the helper's tier (transitive composition, Req 17 AC 7).
    ///
    /// **Validates: Requirements 17.1, 17.6**
    /// </summary>
    [Theory]
    [MemberData(nameof(TransitiveCompositionCases))]
    public void TransitiveComposition_CallerTierIsAtLeastCalleesTier(FieldOp helperOp, FieldOp callerOp)
    {
        var helperBody = GenerateBodyLine(helperOp, "data");
        if(string.IsNullOrEmpty(helperBody))
        {
            helperBody = "int _h = 0;";
        }

        var callerDirectBody = GenerateBodyLine(callerOp, "data");
        var callerBody = string.IsNullOrEmpty(callerDirectBody)
            ? "this.Helper();"
            : $"{callerDirectBody}\n            this.Helper();";

        var source = $$"""
            class Container
            {
                owned int data;
                void Helper()
                {
                    {{helperBody}}
                }
                void Target()
                {
                    {{callerBody}}
                }
            }
            """;

        var expectedHelperTier = TierForOp(helperOp);
        var expectedCallerTier = ExpectedTier(helperOp, callerOp);

        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors);

        var builder = new SemanticModelBuilder();
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var analysisBag = new DiagnosticBag();
        ThisTierInferencePass.Analyze(model, analysisBag);

        var targetMethod = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Target" && s.Kind == SymbolKind.Method);
        var helperMethod = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Helper" && s.Kind == SymbolKind.Method);

        Assert.NotNull(targetMethod);
        Assert.NotNull(helperMethod);

        var actualHelperTier = ThisTierInferencePass.GetThisTier(helperMethod!);
        var actualCallerTier = ThisTierInferencePass.GetThisTier(targetMethod!);

        Assert.Equal(expectedHelperTier, actualHelperTier);
        Assert.Equal(expectedCallerTier, actualCallerTier);
        // Soundness: caller tier >= callee tier (transitive requirement)
        Assert.True(actualCallerTier >= actualHelperTier,
            $"Caller tier ({actualCallerTier}) must be >= helper tier ({actualHelperTier})");
    }
}