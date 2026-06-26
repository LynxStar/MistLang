// Feature: ownership-system, Property 3: Non-storability enforcement

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for non-storability enforcement.
///
/// Property 3: Non-storability enforcement
/// For any variable declared as <c>ref</c>, <c>readonly ref</c>, or <c>frozen ref</c>,
/// any attempt to assign it to a field, collection element, or return it from a method
/// SHALL produce a diagnostic error. The variable's lifetime SHALL NOT exceed its
/// declaring scope.
///
/// **Validates: Requirements 4.4, 4.8, 5.2, 5.8, 6.2, 6.9, 11.1, 11.4, 11.5**
/// </summary>
public class OwnershipStorabilityPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for a ref-tier variable assigned to a field (should produce
    /// MIST_OWN_NOT_STORABLE).
    /// </summary>
    public sealed record RefToFieldScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for a ref-tier specifier on a method return type (should produce
    /// MIST_OWN_INVALID_RETURN_SPEC).
    /// </summary>
    public sealed record RefReturnScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for a ref-tier specifier on a field declaration (should produce
    /// MIST_OWN_NOT_STORABLE).
    /// </summary>
    public sealed record RefFieldDeclScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for a valid non-storable ref usage that stays within scope
    /// (should produce no storability diagnostics).
    /// </summary>
    public sealed record ValidRefUsageScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Holder", "Wrapper", "Storage", "Channel"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "stored", "cached", "saved"];

    private static readonly string[] MethodNames =
        ["GetItem", "Retrieve", "Fetch", "Load", "Obtain", "Extract"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "item", "obj", "val"];

    private static readonly string[] RefTierKeywords =
        ["borrowed", "frozen"];

    // ─── Generators ──────────────────────────────────────────────────────

    // Generator: ref-tier specifier on a field declaration (non-storable violation)
    private static Gen<RefFieldDeclScenario> GenRefFieldDeclScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from refTier in Gen.Elements(RefTierKeywords)
               from refTypeName in Gen.Elements(new[] { "Widget", "Handler", "Service" })
               let source = $$"""
                   class {{refTypeName}} {
                   }
                   class {{className}} {
                       {{refTier}} {{refTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new RefFieldDeclScenario(source, $"field '{fieldName}' declared as '{refTier} {refTypeName}' in class {className}");
    }

    // Generator: ref-tier specifier on a method return type (non-storable violation)
    private static Gen<RefReturnScenario> GenRefReturnScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from refTier in Gen.Elements(RefTierKeywords)
               from refTypeName in Gen.Elements(new[] { "Widget", "Handler", "Service" })
               let source = $$"""
                   class {{refTypeName}} {
                   }
                   class {{className}} {
                       public {{refTier}} {{refTypeName}} {{methodName}}() {
                           return new {{refTypeName}}();
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new RefReturnScenario(source, $"method '{methodName}' returns '{refTier} {refTypeName}' in class {className}");
    }

    // Generator: ref-tier variable assigned to a field in a method body (non-storable violation)
    private static Gen<RefToFieldScenario> GenRefToFieldScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refTier in Gen.Elements(RefTierKeywords)
               from refTypeName in Gen.Elements(new[] { "Widget", "Handler", "Service" })
               let source = $$"""
                   class {{refTypeName}} {
                   }
                   class {{className}} {
                       {{refTypeName}} {{fieldName}};
                       public void Store({{refTier}} {{refTypeName}} {{varName}}) {
                           {{fieldName}} = {{varName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new RefToFieldScenario(source, $"assigning '{refTier}' param '{varName}' to field '{fieldName}' in class {className}");
    }

    // Generator: valid ref usage that stays within scope (no storability violation)
    private static Gen<ValidRefUsageScenario> GenValidRefUsageScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from refTier in Gen.Elements(RefTierKeywords)
               from refTypeName in Gen.Elements(new[] { "Widget", "Handler", "Service" })
               let source = $$"""
                   owned class {{refTypeName}} {
                       public readonly int value;
                       public {{refTypeName}}() {
                           value = 42;
                       }
                   }
                   class {{className}} {
                       public static void Use({{refTier}} {{refTypeName}} {{varName}}) {
                           int v = {{varName}}.value;
                       }
                       public static void Main() {
                           owned {{refTypeName}} owner = new {{refTypeName}}();
                           Use(owner);
                       }
                   }
                   """
               select new ValidRefUsageScenario(source, $"valid '{refTier}' param '{varName}' used within scope in class {className}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<RefFieldDeclScenario> ArbitraryRefFieldDeclScenario()
    {
        return GenRefFieldDeclScenario().ToArbitrary();
    }

    public static Arbitrary<RefReturnScenario> ArbitraryRefReturnScenario()
    {
        return GenRefReturnScenario().ToArbitrary();
    }

    public static Arbitrary<RefToFieldScenario> ArbitraryRefToFieldScenario()
    {
        return GenRefToFieldScenario().ToArbitrary();
    }

    public static Arbitrary<ValidRefUsageScenario> ArbitraryValidRefUsageScenario()
    {
        return GenValidRefUsageScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3a: Ref-tier specifiers on field declarations produce
    // MIST_OWN_NOT_STORABLE
    // **Validates: Requirements 4.4, 5.2, 6.2, 11.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipStorabilityPropertyTests) })]
    public bool RefTierOnFieldDecl_EmitsNotStorable(RefFieldDeclScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_NOT_STORABLE");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3b: Ref-tier specifiers on method return types produce
    // MIST_OWN_INVALID_RETURN_SPEC (refs cannot escape method scope)
    // **Validates: Requirements 4.8, 5.8, 6.9, 11.4, 11.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipStorabilityPropertyTests) })]
    public bool RefTierOnReturnType_EmitsInvalidReturnSpec(RefReturnScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_INVALID_RETURN_SPEC");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3c: Assigning a ref-tier variable to a field produces
    // MIST_OWN_NOT_STORABLE
    // **Validates: Requirements 4.4, 5.2, 6.2, 11.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipStorabilityPropertyTests) })]
    public bool RefTierAssignedToField_EmitsNotStorable(RefToFieldScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_NOT_STORABLE");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3d: Valid ref usage within scope produces no storability
    // diagnostics
    // **Validates: Requirements 4.4, 5.2, 6.2 (negative case)**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipStorabilityPropertyTests) })]
    public bool ValidRefUsageWithinScope_NoStorabilityDiagnostics(ValidRefUsageScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_NOT_STORABLE" ||
            d.Id.Value == "MIST_OWN_REF_ESCAPE" ||
            d.Id.Value == "MIST_OWN_INVALID_RETURN_SPEC");
    }
}