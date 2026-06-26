// Feature: owned-fields, Task 18.6 — Property 6: Field-type restriction correctness (Req 4)
//
// For all owned types T:
//   Compiles(T) ⟺ ∀ field f: IsOwnedType(typeof(f)) ∨ ImplementsIClonable(typeof(f)) ∨ IsValueType(typeof(f))
//
// Uses in-process CompilerDriver.Compile() — no per-iteration process spawning.
// Theory-based approach covering:
//   1. Owned type with only value-type fields → compiles cleanly (no MIST_OWN_FIELD_TYPE_VIOLATION)
//   2. Owned type with IClonable fields (string) → compiles cleanly
//   3. Owned type with another owned type field → compiles cleanly
//   4. Owned type with non-owned, non-IClonable reference type field → MIST_OWN_FIELD_TYPE_VIOLATION
//   5. Mixed valid fields → compiles cleanly
//   6. Multiple violating fields → multiple MIST_OWN_FIELD_TYPE_VIOLATION diagnostics
//
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).
//
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property 6: Field-type restriction correctness (Req 4).
///
/// For all owned types T:
///   Compiles(T) ⟺ ∀ field f ∈ T:
///     IsOwnedType(typeof(f)) ∨ ImplementsIClonable(typeof(f)) ∨ IsValueType(typeof(f))
///
/// The test exercises both directions of the biconditional:
///   → (forward): types with all valid fields compile without MIST_OWN_FIELD_TYPE_VIOLATION
///   ← (backward): types with at least one invalid field produce MIST_OWN_FIELD_TYPE_VIOLATION
///
/// Since the input space is bounded by field-type categories (value type, IClonable,
/// owned type, non-owned non-IClonable reference), this uses a deterministic [Theory]
/// approach that enumerates meaningful combinations exhaustively.
///
/// <b>Validates: Requirements 4.1, 4.2, 4.3, 4.4</b>
/// </summary>
public sealed class FieldTypeRestrictionCorrectnessPropertyTests
{
    private const string FieldTypeViolationId = "MIST_OWN_FIELD_TYPE_VIOLATION";

    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_field_restrict_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IEnumerable<Diagnostic> FindFieldTypeViolations(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Where(d => d.Id.Value == FieldTypeViolationId);
    }

    // ─── Helper type declarations ────────────────────────────────────────

    /// <summary>
    /// A non-owned, non-IClonable reference type (has an array field which blocks
    /// IClonable auto-derivation).
    /// </summary>
    private const string NonClonableReferenceType = """
        class Gadget
        {
            public int[] data;
        }
        """;

    /// <summary>
    /// Another non-owned, non-IClonable reference type for multi-violation tests.
    /// </summary>
    private const string AnotherNonClonableType = """
        class Widget
        {
            public int[] parts;
        }
        """;

    /// <summary>
    /// An owned type used as a valid field type in other owned types.
    /// </summary>
    private const string OwnedChildType = """
        owned class OwnedChild
        {
            public int Value;
        }
        """;

    /// <summary>
    /// A type that auto-derives IClonable (all fields are value types).
    /// </summary>
    private const string ClonableType = """
        class ClonableItem
        {
            public int Id;
            public int Count;
        }
        """;

    /// <summary>
    /// Standard program entry point for compilation.
    /// </summary>
    private const string MainProgram = """

        class Program
        {
            public static void Main()
            {
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Forward direction (→): Valid field types produce no violation
    //
    // Types satisfying the restriction compile without MIST_OWN_FIELD_TYPE_VIOLATION.
    // ═══════════════════════════════════════════════════════════════════════

    // ─── 1. Only value-type fields → compiles cleanly (Req 4 AC 3) ───────

    /// <summary>
    /// An owned type where all fields are value types (int, bool) satisfies the
    /// restriction. Value types use copy semantics and cannot introduce aliasing.
    ///
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("bool")]
    [InlineData("int, bool, int")]
    public void OwnedType_AllValueTypeFields_NoViolation(string fieldTypes)
    {
        var fields = fieldTypes.Split(',')
            .Select((t, i) => $"    public {t.Trim()} field{i};")
            .Aggregate((a, b) => a + "\n" + b);

        var source = $$"""
            owned class Target
            {
            {{fields}}
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    // ─── 2. IClonable fields (string) → compiles cleanly (Req 4 AC 2) ───

    /// <summary>
    /// An owned type where fields are IClonable types (string is intrinsically
    /// IClonable, ClonableItem auto-derives IClonable) satisfies the restriction.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Fact]
    public void OwnedType_IClonableStringField_NoViolation()
    {
        var source = """
            owned class Target
            {
                public string Name;
                public string Description;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    /// <summary>
    /// An owned type with a field whose type auto-derives IClonable (all fields
    /// are value types) satisfies the restriction.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Fact]
    public void OwnedType_AutoDerivedIClonableField_NoViolation()
    {
        var source = ClonableType + """

            owned class Target
            {
                public ClonableItem item;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    // ─── 3. Owned type field → compiles cleanly (Req 4 AC 1) ─────────────

    /// <summary>
    /// An owned type with a field whose type is another owned type satisfies the
    /// restriction. Owned types guarantee full graph ownership.
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Fact]
    public void OwnedType_OwnedTypeField_NoViolation()
    {
        var source = OwnedChildType + """

            owned class Target
            {
                public OwnedChild child;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    /// <summary>
    /// An owned type with multiple owned-type fields satisfies the restriction.
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Fact]
    public void OwnedType_MultipleOwnedTypeFields_NoViolation()
    {
        var source = OwnedChildType + """

            owned class AnotherOwned
            {
                public int X;
            }

            owned class Target
            {
                public OwnedChild first;
                public AnotherOwned second;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    // ─── 5. Mixed valid fields → compiles cleanly (Req 4 AC 1+2+3) ──────

    /// <summary>
    /// An owned type with a mix of all three valid field categories (value type,
    /// IClonable, owned type) satisfies the restriction.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Fact]
    public void OwnedType_MixedValidFields_NoViolation()
    {
        var source = OwnedChildType + ClonableType + """

            owned class Target
            {
                public int count;
                public string name;
                public OwnedChild child;
                public ClonableItem data;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Empty(violations);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Backward direction (←): Invalid field types produce violation
    //
    // Types violating the restriction produce MIST_OWN_FIELD_TYPE_VIOLATION.
    // ═══════════════════════════════════════════════════════════════════════

    // ─── 4. Non-owned, non-IClonable reference field → violation (Req 4 AC 4) ──

    /// <summary>
    /// An owned type with a field whose type is a non-owned reference type that
    /// does NOT implement IClonable produces MIST_OWN_FIELD_TYPE_VIOLATION.
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Fact]
    public void OwnedType_NonOwnedNonClonableField_EmitsViolation()
    {
        var source = NonClonableReferenceType + """

            owned class Target
            {
                public Gadget item;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        _ = Assert.Single(violations);
        Assert.Equal(DiagnosticSeverity.Error, violations[0].Severity);
        Assert.Contains("item", violations[0].Message, StringComparison.Ordinal);
        Assert.Contains("Gadget", violations[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An owned type with a single violating field alongside valid value-type
    /// fields produces exactly one MIST_OWN_FIELD_TYPE_VIOLATION — only the
    /// violating field is reported.
    ///
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Fact]
    public void OwnedType_OneViolatingFieldAmongValidOnes_SingleViolation()
    {
        var source = NonClonableReferenceType + """

            owned class Target
            {
                public int count;
                public Gadget bad;
                public string name;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        _ = Assert.Single(violations);
        Assert.Contains("bad", violations[0].Message, StringComparison.Ordinal);
        Assert.Contains("Gadget", violations[0].Message, StringComparison.Ordinal);
    }

    // ─── 6. Multiple violating fields → multiple diagnostics (Req 4 AC 4) ──

    /// <summary>
    /// An owned type with multiple violating fields produces one
    /// MIST_OWN_FIELD_TYPE_VIOLATION per violating field. Each violation is
    /// reported independently.
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Fact]
    public void OwnedType_MultipleViolatingFields_MultipleViolations()
    {
        var source = NonClonableReferenceType + AnotherNonClonableType + """

            owned class Target
            {
                public Gadget first;
                public Widget second;
                public int validCount;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Equal(2, violations.Count);

        var messages = violations.Select(d => d.Message).ToList();
        Assert.Contains(messages, m => m.Contains("first", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("second", StringComparison.Ordinal));

        // Value-type field should not appear in any violation.
        Assert.DoesNotContain(messages, m => m.Contains("validCount", StringComparison.Ordinal));
    }

    /// <summary>
    /// An owned type with three violating fields (all non-owned, non-IClonable
    /// reference types) produces three independent violations.
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Fact]
    public void OwnedType_ThreeViolatingFields_ThreeViolations()
    {
        var source = NonClonableReferenceType + AnotherNonClonableType + """

            class ThirdBad
            {
                public int[] items;
            }

            owned class Target
            {
                public Gadget a;
                public Widget b;
                public ThirdBad c;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        Assert.Equal(3, violations.Count);

        var messages = violations.Select(d => d.Message).ToList();
        Assert.Contains(messages, m => m.Contains("a", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("b", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("c", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Biconditional completeness: field-type category exhaustive coverage
    //
    // Theory-based enumeration of all field-type categories to verify the
    // biconditional holds exhaustively.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The categories of field types relevant to the restriction.
    /// </summary>
    public enum FieldCategory
    {
        /// <summary>int, bool — value types always accepted.</summary>
        ValueType,

        /// <summary>string — intrinsically IClonable.</summary>
        IClonable,

        /// <summary>Another owned type — always accepted.</summary>
        OwnedType,

        /// <summary>Non-owned reference type without IClonable — violates restriction.</summary>
        NonOwnedNonClonable,
    }

    /// <summary>
    /// Returns whether a field category satisfies the restriction.
    /// </summary>
    private static bool SatisfiesRestriction(FieldCategory category)
    {
        return category switch
        {
            FieldCategory.ValueType => true,
            FieldCategory.IClonable => true,
            FieldCategory.OwnedType => true,
            FieldCategory.NonOwnedNonClonable => false,
            _ => false,
        };
    }

    /// <summary>
    /// Generates the supporting type declarations needed for a given category.
    /// </summary>
    private static string TypeDeclarationsFor(FieldCategory category)
    {
        return category switch
        {
            FieldCategory.ValueType => "",
            FieldCategory.IClonable => "",
            FieldCategory.OwnedType => OwnedChildType,
            FieldCategory.NonOwnedNonClonable => NonClonableReferenceType,
            _ => "",
        };
    }

    /// <summary>
    /// Returns the type name for a field of the given category.
    /// </summary>
    private static string TypeNameFor(FieldCategory category)
    {
        return category switch
        {
            FieldCategory.ValueType => "int",
            FieldCategory.IClonable => "string",
            FieldCategory.OwnedType => "OwnedChild",
            FieldCategory.NonOwnedNonClonable => "Gadget",
            _ => "int",
        };
    }

    public static IEnumerable<object[]> SingleFieldCategories()
    {
        foreach(var category in Enum.GetValues<FieldCategory>())
        {
            yield return new object[] { category };
        }
    }

    /// <summary>
    /// For each single field category, the owned type compiles without violation
    /// if and only if the category satisfies the restriction.
    ///
    /// Verifies both directions of the biconditional for single-field types.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleFieldCategories))]
    public void SingleField_BiconditionalHolds(FieldCategory category)
    {
        var typeDecls = TypeDeclarationsFor(category);
        var typeName = TypeNameFor(category);
        var shouldPass = SatisfiesRestriction(category);

        var source = typeDecls + $$"""

            owned class Target
            {
                public {{typeName}} field0;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        if(shouldPass)
        {
            Assert.Empty(violations);
        }
        else
        {
            Assert.NotEmpty(violations);
            Assert.All(violations, v => Assert.Equal(DiagnosticSeverity.Error, v.Severity));
        }
    }

    public static IEnumerable<object[]> TwoFieldCombinations()
    {
        foreach(var catA in Enum.GetValues<FieldCategory>())
        {
            foreach(var catB in Enum.GetValues<FieldCategory>())
            {
                yield return new object[] { catA, catB };
            }
        }
    }

    /// <summary>
    /// For each pair of field categories, the owned type compiles without any
    /// MIST_OWN_FIELD_TYPE_VIOLATION if and only if BOTH fields satisfy the
    /// restriction. When one or both violate, the number of violations equals
    /// the count of violating fields.
    ///
    /// Exhaustive over 4×4 = 16 combinations.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Theory]
    [MemberData(nameof(TwoFieldCombinations))]
    public void TwoFields_BiconditionalHolds(FieldCategory catA, FieldCategory catB)
    {
        // Combine type declarations, deduplicating if both use the same helper type.
        var declA = TypeDeclarationsFor(catA);
        var declB = TypeDeclarationsFor(catB);
        var combinedDecls = declA == declB ? declA : declA + "\n" + declB;

        var typeA = TypeNameFor(catA);
        var typeB = TypeNameFor(catB);

        var source = combinedDecls + $$"""

            owned class Target
            {
                public {{typeA}} fieldA;
                public {{typeB}} fieldB;
            }
            """ + MainProgram;

        var diagnostics = CompileAndGetDiagnostics(source);
        var violations = FindFieldTypeViolations(diagnostics).ToList();

        var expectedViolationCount = (SatisfiesRestriction(catA) ? 0 : 1)
                                   + (SatisfiesRestriction(catB) ? 0 : 1);

        Assert.Equal(expectedViolationCount, violations.Count);

        if(expectedViolationCount == 0)
        {
            // Forward: all fields valid → no violations
            Assert.Empty(violations);
        }
        else
        {
            // Backward: violating fields produce errors
            Assert.All(violations, v => Assert.Equal(DiagnosticSeverity.Error, v.Severity));
        }
    }
}