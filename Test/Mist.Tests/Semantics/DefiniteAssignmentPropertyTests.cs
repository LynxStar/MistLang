// Feature: optional-defaultability, Properties 4, 5, 6: Definite assignment analysis
// Property 4: Definite assignment completeness — all fields assigned → no diagnostic
// Property 5: Definite assignment error detection — missing assignment → diagnostic
// Property 6: Synthetic default injection for defaultable fields — defaultable uninitialized fields don't require assignment
// **Validates: Requirements 3.1, 3.3, 3.4, 3.5, 3.6**

using System.Collections.Immutable;
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
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

#pragma warning disable CA1034

/// <summary>
/// Scenario for Property 4: a constructor that assigns ALL non-defaultable fields.
/// No MIST_FIELD_NOT_DEFINITELY_ASSIGNED diagnostic should be emitted.
/// </summary>
public sealed record AllFieldsAssignedScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    int NonDefaultableFieldCount,
    int DefaultableFieldCount)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName} (ctor assigns all {NonDefaultableFieldCount} non-defaultable fields, {DefaultableFieldCount} defaultable)";
    }
}

/// <summary>
/// Scenario for Property 5: a constructor that does NOT assign at least one non-defaultable field.
/// MIST_FIELD_NOT_DEFINITELY_ASSIGNED diagnostic should be emitted naming the unassigned field.
/// </summary>
public sealed record MissingAssignmentScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    string UnassignedFieldName,
    int TotalNonDefaultableFields)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName} (missing assignment to '{UnassignedFieldName}', {TotalNonDefaultableFields} non-defaultable fields)";
    }
}

/// <summary>
/// Scenario for Property 6: a constructor with defaultable fields that are NOT assigned.
/// No diagnostic should be emitted for the unassigned defaultable fields (synthetic default injection).
/// </summary>
public sealed record DefaultableFieldInjectionScenario(
    string Source,
    string TypeName,
    bool IsStruct,
    int DefaultableFieldCount,
    int NonDefaultableFieldCount)
{
    public override string ToString()
    {
        return $"{(IsStruct ? "struct" : "class")} {TypeName} ({DefaultableFieldCount} defaultable unassigned, {NonDefaultableFieldCount} non-defaultable assigned)";
    }
}

#pragma warning restore CA1034

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for definite assignment analysis in constructors.
///
/// Property 4: For any constructor body where all fields are explicitly assigned on
/// every control-flow path, the compiler SHALL produce no definite-assignment diagnostic errors.
///
/// Property 5: For any constructor body where at least one non-defaultable field (with no
/// initializer) is NOT assigned on some control-flow path, the compiler SHALL emit a
/// MIST_FIELD_NOT_DEFINITELY_ASSIGNED diagnostic naming that field.
///
/// Property 6: For any class or struct type with a constructor, fields whose types are
/// defaultable and have no explicit initializer SHALL be considered definitely assigned
/// at constructor entry (no diagnostic required, even if the constructor body doesn't assign them).
///
/// **Validates: Requirements 3.1, 3.3, 3.4, 3.5, 3.6**
/// </summary>
public sealed class DefiniteAssignmentPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_def_assign_pbt_" + Guid.NewGuid().ToString("N"));
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

    private static bool HasDefiniteAssignmentDiagnosticForType(
        ImmutableArray<Diagnostic> diagnostics, string typeName)
    {
        return diagnostics.Any(d => d.Id.Value == "MIST_FIELD_NOT_DEFINITELY_ASSIGNED" &&
            d.Message.Contains($"constructor '{typeName}'"));
    }

    private static bool HasDefiniteAssignmentDiagnosticForField(
        ImmutableArray<Diagnostic> diagnostics, string fieldName, string typeName)
    {
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_FIELD_NOT_DEFINITELY_ASSIGNED" &&
            d.Message.Contains($"'{fieldName}'") &&
            d.Message.Contains($"constructor '{typeName}'"));
    }

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double", "float"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent", "Record"];

    private static readonly string[] AbstractNames =
        ["IRenderer", "ILogger", "IService", "IHandler"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios for Property 4: all non-defaultable fields are assigned.
    /// The constructor body assigns every non-defaultable field via this.field = default_value.
    /// </summary>
    private static Gen<AllFieldsAssignedScenario> GenAllFieldsAssignedScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from nonDefaultableCount in Gen.Choose(1, 3)
               from defaultableCount in Gen.Choose(0, 2)
               from abstractName in Gen.Elements(AbstractNames)
               from primitiveType in Gen.Elements(PrimitiveTypes)
               let source = BuildAllAssignedSource(
                   isStruct, typeName, nonDefaultableCount, defaultableCount, abstractName, primitiveType)
               select new AllFieldsAssignedScenario(
                   source, typeName, isStruct, nonDefaultableCount, defaultableCount);
    }

    /// <summary>
    /// Generates scenarios for Property 5: at least one non-defaultable field is NOT assigned.
    /// The constructor body assigns some fields but leaves at least one non-defaultable field unassigned.
    /// </summary>
    private static Gen<MissingAssignmentScenario> GenMissingAssignmentScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from totalNonDefaultable in Gen.Choose(1, 3)
               from missingIndex in Gen.Choose(0, totalNonDefaultable - 1)
               from abstractName in Gen.Elements(AbstractNames)
               let unassignedField = FieldNames[missingIndex]
               let source = BuildMissingAssignmentSource(
                   isStruct, typeName, totalNonDefaultable, missingIndex, abstractName)
               select new MissingAssignmentScenario(
                   source, typeName, isStruct, unassignedField, totalNonDefaultable);
    }

    /// <summary>
    /// Generates scenarios for Property 6: defaultable fields are not assigned but should
    /// be pre-assigned via synthetic default injection.
    /// </summary>
    private static Gen<DefaultableFieldInjectionScenario> GenDefaultableFieldInjectionScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from defaultableCount in Gen.Choose(1, 3)
               from nonDefaultableCount in Gen.Choose(0, 2)
               from abstractName in Gen.Elements(AbstractNames)
               from primitiveType in Gen.Elements(PrimitiveTypes)
               let source = BuildDefaultableInjectionSource(
                   isStruct, typeName, defaultableCount, nonDefaultableCount, abstractName, primitiveType)
               select new DefaultableFieldInjectionScenario(
                   source, typeName, isStruct, defaultableCount, nonDefaultableCount);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<AllFieldsAssignedScenario> ArbitraryAllFieldsAssigned()
    {
        return GenAllFieldsAssignedScenario().ToArbitrary();
    }

    public static Arbitrary<MissingAssignmentScenario> ArbitraryMissingAssignment()
    {
        return GenMissingAssignmentScenario().ToArbitrary();
    }

    public static Arbitrary<DefaultableFieldInjectionScenario> ArbitraryDefaultableFieldInjection()
    {
        return GenDefaultableFieldInjectionScenario().ToArbitrary();
    }

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Builds source for Property 4: a type where all non-defaultable fields are assigned in the constructor.
    /// </summary>
    private static string BuildAllAssignedSource(
        bool isStruct, string typeName, int nonDefaultableCount, int defaultableCount,
        string abstractName, string primitiveType)
    {
        var kind = isStruct ? "struct" : "class";
        var fields = new List<string>();
        var assignments = new List<string>();
        var ctorParams = new List<string>();

        // Non-defaultable fields (abstract type) — all assigned via constructor params
        for(var i = 0; i < nonDefaultableCount; i++)
        {
            var fieldName = FieldNames[i];
            fields.Add($"    public {abstractName} {fieldName};");
            assignments.Add($"        this.{fieldName} = svc{i};");
            ctorParams.Add($"{abstractName} svc{i}");
        }

        // Defaultable fields (primitive type) — also assigned for Property 4
        for(var i = 0; i < defaultableCount; i++)
        {
            var fieldName = FieldNames[nonDefaultableCount + i];
            fields.Add($"    public {primitiveType} {fieldName};");
            assignments.Add($"        this.{fieldName} = default({primitiveType});");
        }

        var paramList = string.Join(", ", ctorParams);

        return $$"""
            namespace TestProgram;

            public abstract {{abstractName}}
            {
            }

            public {{kind}} {{typeName}}
            {
            {{string.Join("\n", fields)}}

                public {{typeName}}({{paramList}})
                {
            {{string.Join("\n", assignments)}}
                }
            }
            """;
    }

    /// <summary>
    /// Builds source for Property 5: a type where at least one non-defaultable field is NOT assigned.
    /// </summary>
    private static string BuildMissingAssignmentSource(
        bool isStruct, string typeName, int totalNonDefaultable, int missingIndex,
        string abstractName)
    {
        var kind = isStruct ? "struct" : "class";
        var fields = new List<string>();
        var assignments = new List<string>();
        var ctorParams = new List<string>();

        for(var i = 0; i < totalNonDefaultable; i++)
        {
            var fieldName = FieldNames[i];
            fields.Add($"    public {abstractName} {fieldName};");

            // Skip assignment for the field at missingIndex
            if(i != missingIndex)
            {
                assignments.Add($"        this.{fieldName} = svc{i};");
            }
            ctorParams.Add($"{abstractName} svc{i}");
        }

        var paramList = string.Join(", ", ctorParams);

        return $$"""
            namespace TestProgram;

            public abstract {{abstractName}}
            {
            }

            public {{kind}} {{typeName}}
            {
            {{string.Join("\n", fields)}}

                public {{typeName}}({{paramList}})
                {
            {{string.Join("\n", assignments)}}
                }
            }
            """;
    }

    /// <summary>
    /// Builds source for Property 6: a type with defaultable fields that are NOT assigned
    /// in the constructor, plus non-defaultable fields that ARE assigned.
    /// The defaultable fields should be pre-assigned via synthetic default injection.
    /// </summary>
    private static string BuildDefaultableInjectionSource(
        bool isStruct, string typeName, int defaultableCount, int nonDefaultableCount,
        string abstractName, string primitiveType)
    {
        var kind = isStruct ? "struct" : "class";
        var fields = new List<string>();
        var assignments = new List<string>();
        var ctorParams = new List<string>();

        // Defaultable fields (primitive type) — NOT assigned in constructor
        for(var i = 0; i < defaultableCount; i++)
        {
            var fieldName = FieldNames[i];
            fields.Add($"    public {primitiveType} {fieldName};");
            // Intentionally NOT assigning these — they should get synthetic defaults
        }

        // Non-defaultable fields (abstract type) — assigned in constructor
        for(var i = 0; i < nonDefaultableCount; i++)
        {
            var fieldName = FieldNames[defaultableCount + i];
            fields.Add($"    public {abstractName} {fieldName};");
            assignments.Add($"        this.{fieldName} = svc{i};");
            ctorParams.Add($"{abstractName} svc{i}");
        }

        // Build the parameter list
        var paramList = string.Join(", ", ctorParams);

        return $$"""
            namespace TestProgram;

            public abstract {{abstractName}}
            {
            }

            public {{kind}} {{typeName}}
            {
            {{string.Join("\n", fields)}}

                public {{typeName}}({{paramList}})
                {
            {{string.Join("\n", assignments)}}
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Definite assignment completeness
    //
    // **Validates: Requirements 3.1, 3.4, 3.6**
    //
    // For any constructor body where all fields are explicitly assigned on
    // every control-flow path, the compiler SHALL produce no
    // definite-assignment diagnostic errors.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: When all non-defaultable fields are assigned in the constructor body,
    /// no MIST_FIELD_NOT_DEFINITELY_ASSIGNED diagnostic is emitted.
    ///
    /// **Validates: Requirements 3.1, 3.4, 3.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(DefiniteAssignmentPropertyTests) },
        MaxTest = 100)]
    public Property AllFieldsAssigned_NoDefiniteAssignmentDiagnostic(
        AllFieldsAssignedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasDefAssignError = HasDefiniteAssignmentDiagnosticForType(diagnostics, scenario.TypeName);

        return (!hasDefAssignError)
            .ToProperty()
            .Label($"Expected no MIST_FIELD_NOT_DEFINITELY_ASSIGNED but got one. " +
                   $"Scenario: {scenario}. " +
                   $"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_FIELD_NOT_DEFINITELY_ASSIGNED" && d.Message.Contains(scenario.TypeName)).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Definite assignment error detection
    //
    // **Validates: Requirements 3.5, 11.2**
    //
    // For any constructor body where at least one non-defaultable field
    // (with no initializer) is NOT assigned on some control-flow path,
    // the compiler SHALL emit a MIST_FIELD_NOT_DEFINITELY_ASSIGNED
    // diagnostic naming that field.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5: When a non-defaultable field is not assigned in the constructor,
    /// MIST_FIELD_NOT_DEFINITELY_ASSIGNED is emitted naming that field.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(DefiniteAssignmentPropertyTests) },
        MaxTest = 100)]
    public Property MissingAssignment_EmitsDefiniteAssignmentDiagnostic(
        MissingAssignmentScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasDefAssignForField = HasDefiniteAssignmentDiagnosticForField(
            diagnostics, scenario.UnassignedFieldName, scenario.TypeName);

        return hasDefAssignForField
            .ToProperty()
            .Label($"Expected MIST_FIELD_NOT_DEFINITELY_ASSIGNED for field '{scenario.UnassignedFieldName}'. " +
                   $"Scenario: {scenario}. " +
                   $"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(scenario.TypeName)).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Synthetic default injection for defaultable fields
    //
    // **Validates: Requirements 3.3**
    //
    // For any class or struct type with a constructor, fields whose types
    // are defaultable and have no explicit initializer SHALL be considered
    // definitely assigned at constructor entry (no diagnostic required,
    // even if the constructor body doesn't assign them).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: When defaultable fields are not explicitly assigned in the constructor,
    /// no MIST_FIELD_NOT_DEFINITELY_ASSIGNED diagnostic is emitted for them
    /// (synthetic default injection applies).
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(DefiniteAssignmentPropertyTests) },
        MaxTest = 100)]
    public Property DefaultableFieldsUnassigned_NoDefiniteAssignmentDiagnostic(
        DefaultableFieldInjectionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasDefAssignError = HasDefiniteAssignmentDiagnosticForType(diagnostics, scenario.TypeName);

        return (!hasDefAssignError)
            .ToProperty()
            .Label($"Expected no MIST_FIELD_NOT_DEFINITELY_ASSIGNED for defaultable fields. " +
                   $"Scenario: {scenario}. " +
                   $"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_FIELD_NOT_DEFINITELY_ASSIGNED" && d.Message.Contains(scenario.TypeName)).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}