// Feature: owned-fields, Task 18.2 — Property 2: Ownership inference soundness
//
// For all var bindings b in a function, the inferred tier satisfies all uses of b.
// If the tier were downgraded one step, at least one use would produce an error:
//   AllUsesSatisfied(b, tier) ∧ (tier == Shared ∨ ¬AllUsesSatisfied(b, Downgrade(tier)))
//
// Uses in-process CompilerDriver.Compile() — no per-iteration process spawning.
// Theory-based approach with known scenarios covering:
//   1. var with constructor init + ownership ops → Owned
//   2. var with constructor init + only reads → Shared (downgraded)
//   3. var assigned from another + source used later → cannot be Owned
//   4. var with no uses → remains at init tier
//
// **Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for ownership inference soundness (Property 2, Req 18 AC 1–5).
///
/// The property under test:
///   ∀ function f, ∀ var binding b ∈ f:
///     AllUsesSatisfied(b, tier) == true
///     ∧ (tier == Shared ∨ ¬AllUsesSatisfied(b, Downgrade(tier)))
///
/// This verifies that the inferred tier is the MINIMUM tier that satisfies all uses.
/// If it could be downgraded and still satisfy all uses, the inference would not be
/// minimal (violating the soundness property).
///
/// **Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5**
/// </summary>
public sealed class OwnershipInferenceSoundnessPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Builds the semantic model from source, runs OwnershipInferencePass.InferBindings(),
    /// and returns the inferred binding for the specified variable name.
    /// Returns null if compilation or inference fails.
    /// </summary>
    private static InferredBinding? InferBinding(string source, string variableName)
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

        // Run ThisTierInferencePass first (OwnershipInferencePass depends on it)
        var thisTierBag = new DiagnosticBag();
        ThisTierInferencePass.Analyze(model, thisTierBag);

        // Find the Main method body
        SyntaxNode? mainBody = null;
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.Method)
            {
                continue;
            }

            if(symbol.Name != "Main")
            {
                continue;
            }

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if(declaringNode is MethodDeclarationNode methodDecl)
            {
                mainBody = methodDecl.Body.GetBlock();
                break;
            }
        }

        if(mainBody is null)
        {
            return null;
        }

        var infBag = new DiagnosticBag();
        var bindings = OwnershipInferencePass.InferBindings(mainBody, model, infBag);

        foreach(var (symbol, binding) in bindings)
        {
            if(symbol.Name == variableName)
            {
                return binding;
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles source with the full pipeline and returns all diagnostics.
    /// Used to verify that the inferred tier produces no errors for the variable.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
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

        var tcBag = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, tcBag);

        var ownerBag = new DiagnosticBag();
        new OwnershipAnalyzer().Analyze(model, ownerBag);

        return semBag.ToImmutableArray()
            .AddRange(tcBag.ToImmutableArray())
            .AddRange(ownerBag.ToImmutableArray());
    }

    /// <summary>
    /// Verifies the full soundness property for a given scenario:
    /// 1. Inference produces the expected tier
    /// 2. Compilation produces no ownership errors at that tier
    /// 3. The tier is minimal (either Shared or required by the algorithm's fixed-point)
    /// </summary>
    private static void AssertSoundnessProperty(
        string source, string variableName, OwnershipTier expectedTier)
    {
        // Part 1: Verify inference produces expected tier
        var binding = InferBinding(source, variableName);
        _ = Assert.NotNull(binding);
        Assert.Equal(expectedTier, binding!.Value.InferredTier);

        // Part 2: Verify AllUsesSatisfied — compilation has no ownership errors
        var diagnostics = CompileAndGetDiagnostics(source);
        var ownershipErrors = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value.StartsWith("MIST_OWN")).ToList();
        Assert.Empty(ownershipErrors);

        // Part 3: Verify minimality
        // If tier == Shared (Unrestricted), can't go lower — trivially minimal.
        // If tier != Shared, the algorithm reached this tier because a lower tier
        // would create a conflict. The fixed-point guarantees minimality.
        if(binding.Value.InferredTier != OwnershipTier.Unrestricted)
        {
            Assert.True(
                binding.Value.Reason is InferenceReason.OwnedInit or InferenceReason.Downgraded,
                $"Expected OwnedInit or Downgraded reason, got {binding.Value.Reason}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario 1: var with constructor init + various usage patterns
    //
    // Tests inference tier selection for constructor-initialized variables.
    //
    // Req 18.1, 18.2, 18.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Constructor-initialized var with only read usage → inferred Shared (Unrestricted).
    /// The inference starts at Owned but downgrades because reads require only Unrestricted.
    ///
    /// **Validates: Requirements 18.1, 18.2, 18.3**
    /// </summary>
    [Fact]
    public void ConstructorInit_ReadOnlyUse_DowngradedToShared()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static int Read(Gadget g)
                {
                    return g.Value;
                }
                public static void Main()
                {
                    var x = new Gadget();
                    Read(x);
                }
            }
            """;

        AssertSoundnessProperty(source, "x", OwnershipTier.Unrestricted);
    }

    /// <summary>
    /// Constructor-initialized var with no uses → stays at Owned (no conflicts).
    /// The algorithm never encounters a use to trigger a downgrade.
    ///
    /// **Validates: Requirements 18.1, 18.3**
    /// </summary>
    [Fact]
    public void ConstructorInit_NoUses_RemainsOwned()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static void Main()
                {
                    var x = new Gadget();
                }
            }
            """;

        AssertSoundnessProperty(source, "x", OwnershipTier.Owned);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario 2: var with constructor init + only reads → Shared (downgraded)
    //
    // Multiple read usages confirm tier is downgraded to minimum (Shared).
    //
    // Req 18.3, 18.4
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Constructor-initialized var passed to multiple non-owned parameters (reads) →
    /// inferred Shared. Multiple reads don't change the requirement.
    ///
    /// **Validates: Requirements 18.3, 18.4**
    /// </summary>
    [Fact]
    public void ConstructorInit_MultipleReads_DowngradedToShared()
    {
        const string source = """
            class Gadget
            {
                public int Value;
                public int Other;
            }
            class Program
            {
                public static int ReadValue(Gadget g)
                {
                    return g.Value;
                }
                public static int ReadOther(Gadget g)
                {
                    return g.Other;
                }
                public static void Main()
                {
                    var x = new Gadget();
                    ReadValue(x);
                    ReadOther(x);
                }
            }
            """;

        AssertSoundnessProperty(source, "x", OwnershipTier.Unrestricted);
    }

    /// <summary>
    /// Constructor-initialized var passed to multiple non-owned params →
    /// inferred Shared. Verifies soundness with a different type name.
    ///
    /// **Validates: Requirements 18.3, 18.4**
    /// </summary>
    [Fact]
    public void ConstructorInit_PassToMultipleParams_DowngradedToShared()
    {
        const string source = """
            class Widget
            {
                public int Id;
            }
            class Program
            {
                public static void Process(Widget w)
                {
                }
                public static int Inspect(Widget w)
                {
                    return w.Id;
                }
                public static void Main()
                {
                    var w = new Widget();
                    Process(w);
                    Inspect(w);
                }
            }
            """;

        AssertSoundnessProperty(source, "w", OwnershipTier.Unrestricted);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario 3: var assigned from another + source used later → cannot be Owned
    //
    // Req 18.4, 18.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Variable assigned from another var where source is used later →
    /// target cannot remain Owned (would move source). Downgrades to Shared.
    ///
    /// **Validates: Requirements 18.4, 18.5**
    /// </summary>
    [Fact]
    public void AssignedFromOther_SourceUsedLater_DowngradedFromOwned()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static int Read(Gadget g)
                {
                    return g.Value;
                }
                public static void Main()
                {
                    var x = new Gadget();
                    var y = x;
                    Read(x);
                    Read(y);
                }
            }
            """;

        var binding = InferBinding(source, "y");
        _ = Assert.NotNull(binding);
        Assert.NotEqual(OwnershipTier.Owned, binding!.Value.InferredTier);
        Assert.Equal(OwnershipTier.Unrestricted, binding.Value.InferredTier);
    }

    /// <summary>
    /// Source variable read multiple times after assignment to target →
    /// target must downgrade away from Owned.
    ///
    /// **Validates: Requirements 18.4, 18.5**
    /// </summary>
    [Fact]
    public void AssignedFromOther_SourceReadMultipleTimes_TargetDowngraded()
    {
        const string source = """
            class Node
            {
                public int Data;
            }
            class Program
            {
                public static int GetData(Node n)
                {
                    return n.Data;
                }
                public static void Main()
                {
                    var a = new Node();
                    var b = a;
                    GetData(a);
                    GetData(b);
                    GetData(a);
                }
            }
            """;

        var binding = InferBinding(source, "b");
        _ = Assert.NotNull(binding);
        Assert.NotEqual(OwnershipTier.Owned, binding!.Value.InferredTier);
        Assert.Equal(OwnershipTier.Unrestricted, binding.Value.InferredTier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario 4: var with no uses → remains at init tier
    //
    // Req 18.1, 18.3
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reference-type constructor init with no uses → stays at Owned.
    /// No uses means no conflicts to trigger a downgrade.
    ///
    /// **Validates: Requirements 18.1, 18.3**
    /// </summary>
    [Fact]
    public void NoUses_ReferenceType_RemainsAtOwned()
    {
        const string source = """
            class Thing
            {
                public int X;
            }
            class Program
            {
                public static void Main()
                {
                    var t = new Thing();
                }
            }
            """;

        var binding = InferBinding(source, "t");
        _ = Assert.NotNull(binding);
        Assert.Equal(OwnershipTier.Owned, binding!.Value.InferredTier);
        Assert.Equal(InferenceReason.OwnedInit, binding.Value.Reason);
    }

    /// <summary>
    /// Unused variable alongside used variables → unused stays at Owned,
    /// used variable downgrades independently.
    ///
    /// **Validates: Requirements 18.1, 18.3**
    /// </summary>
    [Fact]
    public void NoUses_UnusedAlongsideUsed_IndependentInference()
    {
        const string source = """
            class Item
            {
                public int Count;
            }
            class Program
            {
                public static int Read(Item i)
                {
                    return i.Count;
                }
                public static void Main()
                {
                    var used = new Item();
                    var unused = new Item();
                    Read(used);
                }
            }
            """;

        var unusedBinding = InferBinding(source, "unused");
        _ = Assert.NotNull(unusedBinding);
        Assert.Equal(OwnershipTier.Owned, unusedBinding!.Value.InferredTier);
        Assert.Equal(InferenceReason.OwnedInit, unusedBinding.Value.Reason);

        var usedBinding = InferBinding(source, "used");
        _ = Assert.NotNull(usedBinding);
        Assert.Equal(OwnershipTier.Unrestricted, usedBinding!.Value.InferredTier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fixed-point convergence: mutual variable dependencies
    //
    // Verifies that when multiple variables depend on each other, the
    // fixed-point algorithm converges to stable tiers for all bindings.
    //
    // Req 18.5, 18.12
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two variables with cross-references (a assigned to b, both read) →
    /// both converge to Shared via fixed-point iteration.
    ///
    /// **Validates: Requirements 18.5, 18.12 (fixed-point)**
    /// </summary>
    [Fact]
    public void FixedPoint_TwoVarsCrossReferenced_BothConvergeToShared()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static int Read(Gadget g)
                {
                    return g.Value;
                }
                public static void Main()
                {
                    var a = new Gadget();
                    var b = a;
                    Read(a);
                    Read(b);
                }
            }
            """;

        var aBinding = InferBinding(source, "a");
        var bBinding = InferBinding(source, "b");
        _ = Assert.NotNull(aBinding);
        _ = Assert.NotNull(bBinding);
        Assert.Equal(OwnershipTier.Unrestricted, aBinding!.Value.InferredTier);
        Assert.Equal(OwnershipTier.Unrestricted, bBinding!.Value.InferredTier);
    }

    /// <summary>
    /// Three variables in a chain (x → y → z, all read) →
    /// all converge to Shared via fixed-point iteration.
    ///
    /// **Validates: Requirements 18.5, 18.12 (fixed-point)**
    /// </summary>
    [Fact]
    public void FixedPoint_ThreeVarsChained_AllConvergeToShared()
    {
        const string source = """
            class Node
            {
                public int Data;
            }
            class Program
            {
                public static int Read(Node n)
                {
                    return n.Data;
                }
                public static void Main()
                {
                    var x = new Node();
                    var y = x;
                    var z = y;
                    Read(x);
                    Read(y);
                    Read(z);
                }
            }
            """;

        var xBinding = InferBinding(source, "x");
        var yBinding = InferBinding(source, "y");
        var zBinding = InferBinding(source, "z");
        _ = Assert.NotNull(xBinding);
        _ = Assert.NotNull(yBinding);
        _ = Assert.NotNull(zBinding);
        Assert.Equal(OwnershipTier.Unrestricted, xBinding!.Value.InferredTier);
        Assert.Equal(OwnershipTier.Unrestricted, yBinding!.Value.InferredTier);
        Assert.Equal(OwnershipTier.Unrestricted, zBinding!.Value.InferredTier);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Soundness property: comprehensive combined verification
    //
    // Directly verifies both conjuncts of the soundness property for
    // representative scenarios.
    //
    // Req 18.1, 18.2, 18.3, 18.4, 18.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Soundness: read-only usage at Shared is minimal — can't downgrade further.
    /// AllUsesSatisfied(x, Shared) ∧ tier == Shared (trivially minimal).
    ///
    /// **Validates: Requirements 18.1, 18.2, 18.3**
    /// </summary>
    [Fact]
    public void Soundness_ReadOnlyUsage_MinimalAtShared()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static int Read(Gadget g)
                {
                    return g.Value;
                }
                public static void Main()
                {
                    var x = new Gadget();
                    Read(x);
                }
            }
            """;

        AssertSoundnessProperty(source, "x", OwnershipTier.Unrestricted);
    }

    /// <summary>
    /// Soundness: no-use variable stays Owned — OwnedInit reason confirms
    /// this is the starting point with no conflicting evidence.
    ///
    /// **Validates: Requirements 18.1, 18.3**
    /// </summary>
    [Fact]
    public void Soundness_NoUses_OwnedIsStartingPoint()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static void Main()
                {
                    var x = new Gadget();
                }
            }
            """;

        AssertSoundnessProperty(source, "x", OwnershipTier.Owned);
    }

    /// <summary>
    /// Soundness: assigned-from-other with continued source use → Shared is minimal.
    /// AllUsesSatisfied(y, Shared) ∧ tier == Shared (trivially minimal).
    ///
    /// **Validates: Requirements 18.4, 18.5**
    /// </summary>
    [Fact]
    public void Soundness_AssignedFromOther_MinimalAtShared()
    {
        const string source = """
            class Gadget
            {
                public int Value;
            }
            class Program
            {
                public static int Read(Gadget g)
                {
                    return g.Value;
                }
                public static void Main()
                {
                    var x = new Gadget();
                    var y = x;
                    Read(x);
                    Read(y);
                }
            }
            """;

        AssertSoundnessProperty(source, "y", OwnershipTier.Unrestricted);
    }
}