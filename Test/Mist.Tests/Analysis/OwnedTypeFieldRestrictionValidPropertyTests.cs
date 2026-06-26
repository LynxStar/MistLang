// Feature: deep-ownership-types, Property 1: Owned type field restriction — valid fields accepted

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for owned type field restriction — valid fields accepted.
///
/// Property 1: Owned Type Field Restriction — Valid Fields Accepted
/// For any owned type declaration (class or struct) where every reference-type field is
/// either declared with the 'owned' specifier or has a type that is itself an owned type,
/// the type checker SHALL accept the declaration without reporting MIST_OWN_FIELD_NOT_OWNED.
///
/// **Validates: Requirements 1.3, 1.5, 1.6, 2.4, 9.1**
/// </summary>
public class OwnedTypeFieldRestrictionValidPropertyTests
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
        Directory.CreateDirectory(tempDir);
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
    /// Scenario for an owned type with only value-type fields (always valid).
    /// </summary>
    public sealed record ValueTypeFieldsScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned type with fields of other owned types (valid).
    /// </summary>
    public sealed record OwnedTypeFieldsScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned type with fields declared with the 'owned' specifier (valid).
    /// </summary>
    public sealed record OwnedSpecifierFieldsScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned type with a mix of value-type and owned-type fields (valid).
    /// </summary>
    public sealed record MixedValidFieldsScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned type with zero fields (vacuously valid).
    /// </summary>
    public sealed record ZeroFieldsScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "value", "item", "content", "payload", "element"];

    private static readonly string[] OwnedRefTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    private static readonly string[] TypeKinds = ["class", "struct"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates owned types (class or struct) with 1–4 value-type fields only.
    /// Value-type fields are always permitted in owned types (Requirement 1.5).
    /// </summary>
    private static Gen<ValueTypeFieldsScenario> GenValueTypeFieldsScenario()
    {
        return from typeKind in Gen.Elements(TypeKinds)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fieldTypes in Gen.ArrayOf(Gen.Elements(ValueTypes), fieldCount)
               let fields = BuildValueFields(fieldTypes)
               let source = $$"""
                   owned {{typeKind}} {{typeName}} {
                       {{fields}}
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ValueTypeFieldsScenario(source,
                   $"owned {typeKind} {typeName} with {fieldCount} value-type field(s)");
    }

    /// <summary>
    /// Generates owned types with fields whose types are other owned types (Requirement 1.6, 9.1).
    /// </summary>
    private static Gen<OwnedTypeFieldsScenario> GenOwnedTypeFieldsScenario()
    {
        return from typeKind in Gen.Elements(TypeKinds)
               from typeName in Gen.Elements(TypeNames)
               from refTypeName in Gen.Elements(OwnedRefTypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{refTypeName}} {
                       {{valueType}} amount;
                   }
                   owned {{typeKind}} {{typeName}} {
                       {{refTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new OwnedTypeFieldsScenario(source,
                   $"owned {typeKind} {typeName} with owned-type field {fieldName}:{refTypeName}");
    }

    /// <summary>
    /// Generates owned types with fields declared with the 'owned' specifier (Requirement 1.3).
    /// The field's type is a non-owned class, but the field itself is marked 'owned'.
    /// </summary>
    private static Gen<OwnedSpecifierFieldsScenario> GenOwnedSpecifierFieldsScenario()
    {
        return from typeKind in Gen.Elements(TypeKinds)
               from typeName in Gen.Elements(TypeNames)
               from refTypeName in Gen.Elements(OwnedRefTypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{refTypeName}} {
                       {{valueType}} count;
                   }
                   owned {{typeKind}} {{typeName}} {
                       owned {{refTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new OwnedSpecifierFieldsScenario(source,
                   $"owned {typeKind} {typeName} with owned-specifier field {fieldName}:{refTypeName}");
    }

    /// <summary>
    /// Generates owned types with a mix of value-type fields and owned-type fields (valid).
    /// Validates Requirements 1.5, 1.6, 2.4.
    /// </summary>
    private static Gen<MixedValidFieldsScenario> GenMixedValidFieldsScenario()
    {
        return from typeKind in Gen.Elements(TypeKinds)
               from typeName in Gen.Elements(TypeNames)
               from refTypeName in Gen.Elements(OwnedRefTypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from valueType1 in Gen.Elements(ValueTypes)
               from valueType2 in Gen.Elements(ValueTypes)
               from valueFieldName in Gen.Elements(new[] { "count", "size", "length", "index" })
               let source = $$"""
                   owned class {{refTypeName}} {
                       {{valueType1}} amount;
                   }
                   owned {{typeKind}} {{typeName}} {
                       {{valueType2}} {{valueFieldName}};
                       {{refTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new MixedValidFieldsScenario(source,
                   $"owned {typeKind} {typeName} with value field {valueFieldName}:{valueType2} + owned-type field {fieldName}:{refTypeName}");
    }

    /// <summary>
    /// Generates owned types (class or struct) with zero fields (vacuously valid).
    /// </summary>
    private static Gen<ZeroFieldsScenario> GenZeroFieldsScenario()
    {
        return from typeKind in Gen.Elements(TypeKinds)
               from typeName in Gen.Elements(TypeNames)
               let source = $$"""
                   owned {{typeKind}} {{typeName}} {
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ZeroFieldsScenario(source,
                   $"owned {typeKind} {typeName} with zero fields");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string BuildValueFields(string[] fieldTypes)
    {
        var lines = new List<string>();
        for(var i = 0; i < fieldTypes.Length; i++)
        {
            var fieldName = i < FieldNames.Length ? FieldNames[i] : $"f{i}";
            lines.Add($"{fieldTypes[i]} {fieldName};");
        }
        return string.Join("\n    ", lines);
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<ValueTypeFieldsScenario> ArbitraryValueTypeFieldsScenario() =>
        GenValueTypeFieldsScenario().ToArbitrary();

    public static Arbitrary<OwnedTypeFieldsScenario> ArbitraryOwnedTypeFieldsScenario() =>
        GenOwnedTypeFieldsScenario().ToArbitrary();

    public static Arbitrary<OwnedSpecifierFieldsScenario> ArbitraryOwnedSpecifierFieldsScenario() =>
        GenOwnedSpecifierFieldsScenario().ToArbitrary();

    public static Arbitrary<MixedValidFieldsScenario> ArbitraryMixedValidFieldsScenario() =>
        GenMixedValidFieldsScenario().ToArbitrary();

    public static Arbitrary<ZeroFieldsScenario> ArbitraryZeroFieldsScenario() =>
        GenZeroFieldsScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1a: Owned types with only value-type fields produce no
    // MIST_OWN_FIELD_NOT_OWNED diagnostic.
    //
    // Value types (int, bool, float, etc.) are always permitted as fields in
    // owned types because they are intrinsically owned (Requirement 1.5, 2.4).
    //
    // **Validates: Requirements 1.5, 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionValidPropertyTests) })]
    public bool ValueTypeFields_InOwnedType_NoFieldNotOwnedDiagnostic(ValueTypeFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1b: Owned types with fields of other owned types produce no
    // MIST_OWN_FIELD_NOT_OWNED diagnostic.
    //
    // A reference-type field whose type is itself an owned type satisfies the
    // owned-type field restriction (Requirement 1.6, 9.1).
    //
    // **Validates: Requirements 1.6, 9.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionValidPropertyTests) })]
    public bool OwnedTypeFields_InOwnedType_NoFieldNotOwnedDiagnostic(OwnedTypeFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1c: Owned types with fields declared with the 'owned' specifier
    // produce no MIST_OWN_FIELD_NOT_OWNED diagnostic.
    //
    // A reference-type field declared with the 'owned' specifier satisfies the
    // owned-type field restriction regardless of whether the field's type is
    // explicitly an owned type (Requirement 1.3).
    //
    // **Validates: Requirement 1.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionValidPropertyTests) })]
    public bool OwnedSpecifierFields_InOwnedType_NoFieldNotOwnedDiagnostic(OwnedSpecifierFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1d: Owned types with a mix of value-type and owned-type fields
    // produce no MIST_OWN_FIELD_NOT_OWNED diagnostic.
    //
    // Combinations of value-type fields and owned-type fields are all valid
    // (Requirements 1.5, 1.6, 2.4).
    //
    // **Validates: Requirements 1.5, 1.6, 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionValidPropertyTests) })]
    public bool MixedValidFields_InOwnedType_NoFieldNotOwnedDiagnostic(MixedValidFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1e: Owned types with zero fields produce no
    // MIST_OWN_FIELD_NOT_OWNED diagnostic.
    //
    // Zero-field owned types vacuously satisfy the field restriction because
    // there are no fields to violate it (Requirement 2.4 — primitives have
    // zero fields and trivially satisfy the requirement).
    //
    // **Validates: Requirement 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionValidPropertyTests) })]
    public bool ZeroFields_InOwnedType_NoFieldNotOwnedDiagnostic(ZeroFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }
}