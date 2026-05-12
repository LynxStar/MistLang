// Feature: deep-ownership-types, Property 2: Owned type field restriction — invalid fields rejected

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for owned type field restriction — invalid fields rejected.
///
/// Property 2: Owned Type Field Restriction — Invalid Fields Rejected
/// For any owned type declaration that contains at least one reference-type field whose
/// type is not an owned type AND the field does not have the 'owned' specifier, the type
/// checker SHALL report exactly one MIST_OWN_FIELD_NOT_OWNED error per violating field.
///
/// **Validates: Requirements 1.4, 13.1, 13.6**
/// </summary>
public class OwnedTypeFieldRestrictionInvalidPropertyTests
{
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
    /// Scenario for an owned type with a single non-owned reference-type field.
    /// Should report exactly one MIST_OWN_FIELD_NOT_OWNED.
    /// </summary>
    public sealed record SingleInvalidFieldScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned type with multiple non-owned reference-type fields.
    /// Should report one MIST_OWN_FIELD_NOT_OWNED per violating field.
    /// </summary>
    public sealed record MultipleInvalidFieldsScenario(string Source, int ExpectedErrorCount, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] OwnedTypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage"];

    private static readonly string[] NonOwnedRefTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "item", "content", "payload", "element", "link"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    /// <summary>
    /// Whether the owned type is a class or struct.
    /// </summary>
    private enum OwnedTypeKind
    {
        Class,
        Struct,
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates an owned type (class or struct) with exactly one non-owned
    /// reference-type field. The field's type is a plain class (not owned).
    /// Should produce exactly one MIST_OWN_FIELD_NOT_OWNED diagnostic.
    /// </summary>
    private static Gen<SingleInvalidFieldScenario> GenSingleInvalidFieldScenario()
    {
        return from ownedTypeName in Gen.Elements(OwnedTypeNames)
               from nonOwnedTypeName in Gen.Elements(NonOwnedRefTypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from typeKind in Gen.Elements(OwnedTypeKind.Class, OwnedTypeKind.Struct)
               let typeKeyword = typeKind == OwnedTypeKind.Class ? "class" : "struct"
               let source = $$"""
                   class {{nonOwnedTypeName}} {
                   }
                   owned {{typeKeyword}} {{ownedTypeName}} {
                       {{nonOwnedTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new SingleInvalidFieldScenario(source,
                   $"owned {typeKeyword} {ownedTypeName} with non-owned field {fieldName}:{nonOwnedTypeName}");
    }

    /// <summary>
    /// Generates an owned type with multiple non-owned reference-type fields (2–4).
    /// Each violating field should produce its own MIST_OWN_FIELD_NOT_OWNED diagnostic.
    /// May also include valid fields (value types or owned ref types) to ensure
    /// only the invalid ones are flagged.
    /// </summary>
    private static Gen<MultipleInvalidFieldsScenario> GenMultipleInvalidFieldsScenario()
    {
        return from ownedTypeName in Gen.Elements(OwnedTypeNames)
               from typeKind in Gen.Elements(OwnedTypeKind.Class, OwnedTypeKind.Struct)
               from invalidCount in Gen.Choose(2, 4)
               from hasValidField in Gen.Elements(true, false)
               from valueType in Gen.Elements(ValueTypes)
               let typeKeyword = typeKind == OwnedTypeKind.Class ? "class" : "struct"
               let scenario = BuildMultipleInvalidFieldsScenario(
                   ownedTypeName, typeKeyword, invalidCount, hasValidField, valueType)
               select scenario;
    }

    private static MultipleInvalidFieldsScenario BuildMultipleInvalidFieldsScenario(
        string ownedTypeName, string typeKeyword, int invalidCount, bool hasValidField, string valueType)
    {
        // Generate distinct non-owned types and field names for each invalid field.
        var nonOwnedTypes = NonOwnedRefTypeNames.Take(invalidCount).ToArray();
        var fieldNames = FieldNames.Take(invalidCount).ToArray();

        // Build non-owned type declarations.
        var nonOwnedDecls = string.Join("\n",
            nonOwnedTypes.Select(t => $"class {t} {{\n}}"));

        // Build field declarations for the owned type.
        var invalidFields = string.Join("\n    ",
            nonOwnedTypes.Zip(fieldNames, (t, f) => $"{t} {f};"));

        var validField = hasValidField ? $"\n    {valueType} count;" : "";

        var source = $$"""
            {{nonOwnedDecls}}
            owned {{typeKeyword}} {{ownedTypeName}} {
                {{invalidFields}}{{validField}}
            }
            class Program {
                public static void Main() {
                }
            }
            """;

        var label = $"owned {typeKeyword} {ownedTypeName} with {invalidCount} invalid fields" +
                    (hasValidField ? " + 1 valid value field" : "");

        return new MultipleInvalidFieldsScenario(source, invalidCount, label);
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<SingleInvalidFieldScenario> ArbitrarySingleInvalidFieldScenario() =>
        GenSingleInvalidFieldScenario().ToArbitrary();

    public static Arbitrary<MultipleInvalidFieldsScenario> ArbitraryMultipleInvalidFieldsScenario() =>
        GenMultipleInvalidFieldsScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2a: Owned type with a single non-owned reference-type field
    // produces exactly one MIST_OWN_FIELD_NOT_OWNED diagnostic
    //
    // **Validates: Requirements 1.4, 13.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionInvalidPropertyTests) })]
    public bool SingleInvalidField_EmitsExactlyOneFieldNotOwned(SingleInvalidFieldScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var fieldNotOwnedCount = diagnostics.Count(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
        return fieldNotOwnedCount == 1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2b: Owned type with multiple non-owned reference-type fields
    // produces one MIST_OWN_FIELD_NOT_OWNED per violating field
    //
    // **Validates: Requirements 1.4, 13.1, 13.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeFieldRestrictionInvalidPropertyTests) })]
    public bool MultipleInvalidFields_EmitsOneErrorPerViolatingField(MultipleInvalidFieldsScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var fieldNotOwnedCount = diagnostics.Count(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
        return fieldNotOwnedCount == scenario.ExpectedErrorCount;
    }
}
