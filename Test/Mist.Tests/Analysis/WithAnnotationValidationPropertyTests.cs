// Feature: owned-fields, Task 18.8 — Property 8: With-annotation validation
//
// For all methods with explicit `with declared`:
//   Compiles(m) ⟺ declared >= InferredMinimum(m.Body)
//
// Uses in-process lex → parse → semantic model build → ThisTierInferencePass.Analyze().
// Theory-based approach exercising all meaningful combinations of declared tier vs body requirement.
// No per-iteration process spawning.
//
// **Validates: Requirements 17.10, 17.11, 17.12**

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
/// Property-based tests for with-annotation validation (Property 8, Req 17 AC 10–12).
///
/// The property under test:
///   ∀ method m with 'with declared':
///     Compiles(m) ⟺ declared >= InferredMinimum(m.Body)
///
/// When declared >= required: the method compiles without MIST_OWN_THIS_TIER_INSUFFICIENT.
/// When declared &lt; required: the compiler emits MIST_OWN_THIS_TIER_INSUFFICIENT.
///
/// The test enumerates all combinations of (declared tier) × (body requirement) and verifies
/// the biconditional holds for each.
///
/// **Validates: Requirements 17.10, 17.11, 17.12**
/// </summary>
public sealed class WithAnnotationValidationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// The body patterns that produce different inferred minimum tiers.
    /// </summary>
    public enum BodyRequirement
    {
        /// <summary>No owned-field access → inferred minimum is None.</summary>
        None,

        /// <summary>Reads an owned field → inferred minimum is ReadonlyRef.</summary>
        ReadonlyRef,

        /// <summary>Writes to an owned field → inferred minimum is Borrowed.</summary>
        Borrowed,
    }

    /// <summary>
    /// Maps a body requirement to the ThisTier that the body demands.
    /// </summary>
    private static ThisTier RequiredTier(BodyRequirement req) => req switch
    {
        BodyRequirement.None => ThisTier.None,
        BodyRequirement.ReadonlyRef => ThisTier.ReadonlyRef,
        BodyRequirement.Borrowed => ThisTier.Borrowed,
        _ => ThisTier.None,
    };

    /// <summary>
    /// Generates Mist source code with a class containing an owned field
    /// and a method whose body has the specified requirement level.
    /// </summary>
    private static string BuildSource(BodyRequirement bodyReq)
    {
        var body = bodyReq switch
        {
            BodyRequirement.None => "int _unused = 0;",
            BodyRequirement.ReadonlyRef => "int _r = this.data;",
            BodyRequirement.Borrowed => "this.data = 42;",
            _ => "int _unused = 0;",
        };

        return $$"""
            class Container
            {
                owned int data;
                void Target()
                {
                    {{body}}
                }
            }
            """;
    }

    /// <summary>
    /// Builds the semantic model from source, sets DeclaredThisTier on the "Target" method,
    /// runs ThisTierInferencePass.Analyze(), and returns whether MIST_OWN_THIS_TIER_INSUFFICIENT
    /// was emitted.
    /// </summary>
    private static (bool HasInsufficient, ThisTier InferredTier) AnalyzeWithDeclaredTier(
        string source, ThisTier declaredTier)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors,
            $"Parse should not produce errors. Errors: {string.Join(", ", parseBag.ToImmutableArray().Select(d => d.Message))}");
        Assert.NotNull(parseResult.Root);

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

        // Set DeclaredThisTier on the target method to simulate parsed 'with {tier}'
        var targetMethod = (Symbol)model.AllSymbols
            .First(s => s.Name == "Target" && s.Kind == SymbolKind.Method);
        targetMethod.DeclaredThisTier = declaredTier;

        var diagnostics = new DiagnosticBag();
        ThisTierInferencePass.Analyze(model, diagnostics);

        var hasInsufficient = diagnostics.ToImmutableArray()
            .Any(d => d.Id.Value == "MIST_OWN_THIS_TIER_INSUFFICIENT");

        var inferredTier = ThisTierInferencePass.GetThisTier(targetMethod);

        return (hasInsufficient, inferredTier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Exhaustive Theory: all (declared tier) × (body requirement) combinations
    //
    // The property:
    //   Compiles(m) ⟺ declared >= InferredMinimum(m.Body)
    //
    // "Compiles" here means no MIST_OWN_THIS_TIER_INSUFFICIENT error is emitted.
    // ═══════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> AllDeclaredVsRequiredCombinations()
    {
        foreach(var declared in Enum.GetValues<ThisTier>())
        {
            foreach(var bodyReq in Enum.GetValues<BodyRequirement>())
            {
                yield return new object[] { declared, bodyReq };
            }
        }
    }

    /// <summary>
    /// For all combinations of declared tier and body requirement, the method compiles
    /// (no MIST_OWN_THIS_TIER_INSUFFICIENT) if and only if declared >= body's required tier.
    /// Over-constraining is valid (Req 17.12). Under-declaring emits an error (Req 17.11).
    /// The declared tier overrides inference (Req 17.10).
    ///
    /// **Validates: Requirements 17.10, 17.11, 17.12**
    /// </summary>
    [Theory]
    [MemberData(nameof(AllDeclaredVsRequiredCombinations))]
    public void WithAnnotation_CompilesIffDeclaredSatisfiesRequired(
        ThisTier declared, BodyRequirement bodyReq)
    {
        var source = BuildSource(bodyReq);
        var required = RequiredTier(bodyReq);
        var shouldCompile = declared >= required;

        var (hasInsufficient, inferredTier) = AnalyzeWithDeclaredTier(source, declared);

        // The biconditional: compiles ⟺ declared >= required
        Assert.Equal(!shouldCompile, hasInsufficient);

        // When it compiles, the declared tier is used (Req 17.10 — annotation overrides inference)
        if(shouldCompile)
        {
            Assert.Equal(declared, inferredTier);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Specific named cases from the task description
    // These are a subset of the exhaustive theory above, called out explicitly
    // for clarity and traceability.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// declared=Borrowed, body requires Borrowed → accepted (no error).
    /// Exact match — declared satisfies body requirement.
    ///
    /// **Validates: Requirements 17.10, 17.11**
    /// </summary>
    [Fact]
    public void DeclaredBorrowed_BodyRequiresBorrowed_Accepted()
    {
        var source = BuildSource(BodyRequirement.Borrowed);
        var (hasInsufficient, inferredTier) = AnalyzeWithDeclaredTier(source, ThisTier.Borrowed);

        Assert.False(hasInsufficient, "Borrowed >= Borrowed, should compile without error");
        Assert.Equal(ThisTier.Borrowed, inferredTier);
    }

    /// <summary>
    /// declared=Owned, body requires Borrowed → accepted (over-constraining is valid).
    ///
    /// **Validates: Requirements 17.10, 17.12**
    /// </summary>
    [Fact]
    public void DeclaredOwned_BodyRequiresBorrowed_Accepted_OverConstraining()
    {
        var source = BuildSource(BodyRequirement.Borrowed);
        var (hasInsufficient, inferredTier) = AnalyzeWithDeclaredTier(source, ThisTier.Owned);

        Assert.False(hasInsufficient, "Owned >= Borrowed, over-constraining is valid");
        Assert.Equal(ThisTier.Owned, inferredTier);
    }

    /// <summary>
    /// declared=Frozen, body requires Borrowed → MIST_OWN_THIS_TIER_INSUFFICIENT.
    /// Frozen &lt; Borrowed, so the annotation under-declares.
    ///
    /// **Validates: Requirements 17.11**
    /// </summary>
    [Fact]
    public void DeclaredFrozen_BodyRequiresBorrowed_EmitsInsufficient()
    {
        var source = BuildSource(BodyRequirement.Borrowed);
        var (hasInsufficient, _) = AnalyzeWithDeclaredTier(source, ThisTier.Frozen);

        Assert.True(hasInsufficient, "Frozen < Borrowed, should emit MIST_OWN_THIS_TIER_INSUFFICIENT");
    }

    /// <summary>
    /// declared=ReadonlyRef, body requires ReadonlyRef → accepted (exact match).
    ///
    /// **Validates: Requirements 17.10, 17.11**
    /// </summary>
    [Fact]
    public void DeclaredReadonlyRef_BodyRequiresReadonlyRef_Accepted()
    {
        var source = BuildSource(BodyRequirement.ReadonlyRef);
        var (hasInsufficient, inferredTier) = AnalyzeWithDeclaredTier(source, ThisTier.ReadonlyRef);

        Assert.False(hasInsufficient, "ReadonlyRef >= ReadonlyRef, should compile without error");
        Assert.Equal(ThisTier.ReadonlyRef, inferredTier);
    }

    /// <summary>
    /// declared=None, body requires ReadonlyRef → MIST_OWN_THIS_TIER_INSUFFICIENT.
    /// None &lt; ReadonlyRef, so the annotation under-declares.
    ///
    /// **Validates: Requirements 17.11**
    /// </summary>
    [Fact]
    public void DeclaredNone_BodyRequiresReadonlyRef_EmitsInsufficient()
    {
        var source = BuildSource(BodyRequirement.ReadonlyRef);
        var (hasInsufficient, _) = AnalyzeWithDeclaredTier(source, ThisTier.None);

        Assert.True(hasInsufficient, "None < ReadonlyRef, should emit MIST_OWN_THIS_TIER_INSUFFICIENT");
    }

    /// <summary>
    /// declared=Owned, body requires None → accepted (over-constraining).
    /// Owned >= None trivially.
    ///
    /// **Validates: Requirements 17.10, 17.12**
    /// </summary>
    [Fact]
    public void DeclaredOwned_BodyRequiresNone_Accepted_OverConstraining()
    {
        var source = BuildSource(BodyRequirement.None);
        var (hasInsufficient, inferredTier) = AnalyzeWithDeclaredTier(source, ThisTier.Owned);

        Assert.False(hasInsufficient, "Owned >= None, over-constraining is valid");
        Assert.Equal(ThisTier.Owned, inferredTier);
    }
}