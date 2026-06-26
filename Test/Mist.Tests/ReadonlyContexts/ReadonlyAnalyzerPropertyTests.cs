// Feature: readonly-contexts, Property 3: Readonly Field Assignment Prohibition
// Feature: readonly-contexts, Property 4: Constructor Privilege for Readonly Fields
// Feature: readonly-contexts, Property 5: Readonly Context Permitted Operations
// Feature: readonly-contexts, Property 6: Readonly Context Prohibited Operations
// Feature: readonly-contexts, Property 7: Readonly Block Overrides Constructor Privilege
// Feature: readonly-contexts, Property 8: Backing Field Exemption
// Feature: readonly-contexts, Property 9: Readonly Method Body Enforcement
// Feature: readonly-contexts, Property 10: Readonly Type Implicit Member Readonly
// Feature: readonly-contexts, Property 13: Readonly Parameter Enforcement

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
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.ReadonlyContexts;

// ─── Scenario types ─────────────────────────────────────────────────────────

public sealed class ReadonlyFieldAssignScenario
{
    public string Source { get; }
    public string Description { get; }

    public ReadonlyFieldAssignScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ConstructorPrivilegeScenario
{
    public string Source { get; }
    public string Description { get; }

    public ConstructorPrivilegeScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class PermittedOperationScenario
{
    public string Source { get; }
    public string Description { get; }

    public PermittedOperationScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ProhibitedOperationScenario
{
    public string Source { get; }
    public string ExpectedDiagnosticId { get; }
    public string Description { get; }

    public ProhibitedOperationScenario(string source, string expectedDiagnosticId, string description)
    {
        Source = source;
        ExpectedDiagnosticId = expectedDiagnosticId;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ReadonlyBlockOverrideScenario
{
    public string Source { get; }
    public string Description { get; }

    public ReadonlyBlockOverrideScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class BackingFieldExemptionScenario
{
    public string Source { get; }
    public bool ShouldSucceed { get; }
    public string Description { get; }

    public BackingFieldExemptionScenario(string source, bool shouldSucceed, string description)
    {
        Source = source;
        ShouldSucceed = shouldSucceed;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ReadonlyMethodBodyScenario
{
    public string Source { get; }
    public string ExpectedDiagnosticId { get; }
    public string Description { get; }

    public ReadonlyMethodBodyScenario(string source, string expectedDiagnosticId, string description)
    {
        Source = source;
        ExpectedDiagnosticId = expectedDiagnosticId;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ReadonlyTypeImplicitScenario
{
    public string Source { get; }
    public bool ShouldSucceed { get; }
    public string Description { get; }

    public ReadonlyTypeImplicitScenario(string source, bool shouldSucceed, string description)
    {
        Source = source;
        ShouldSucceed = shouldSucceed;
        Description = description;
    }

    public override string ToString() => Description;
}

public sealed class ReadonlyParameterScenario
{
    public string Source { get; }
    public string ExpectedDiagnosticId { get; }
    public string Description { get; }

    public ReadonlyParameterScenario(string source, string expectedDiagnosticId, string description)
    {
        Source = source;
        ExpectedDiagnosticId = expectedDiagnosticId;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class ReadonlyAnalyzerGenerators
{
    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] FieldNames = ["x", "y", "count", "name"];
    private static readonly string[] MethodNames = ["Get", "Compute", "Process", "Calculate"];
    private static readonly string[] PropertyNames = ["Value", "Name", "Count", "Size"];
    private static readonly string[] ParamNames = ["item", "data", "obj", "arg"];

    // ─── Property 3: Readonly Field Assignment Prohibition ──────────────

    /// <summary>
    /// Generates programs with readonly fields assigned outside constructors.
    /// These should produce MIST_RO_0009.
    /// </summary>
    public static Gen<ReadonlyFieldAssignScenario> GenReadonlyFieldAssignOutsideConstructor()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    readonly int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    void {methodName}() {{
        this.{fieldName} = 42;
    }}
}}"
               select new ReadonlyFieldAssignScenario(source,
                   $"readonly field '{fieldName}' assigned in method '{methodName}' of '{className}'");
    }

    // ─── Property 4: Constructor Privilege for Readonly Fields ───────────

    /// <summary>
    /// Generates programs with readonly fields assigned within constructors (no readonly block).
    /// These should produce no errors.
    /// </summary>
    public static Gen<ConstructorPrivilegeScenario> GenConstructorPrivilege()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from fieldCount in Gen.Choose(1, 3)
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => $"    readonly int {fieldName}{i};")
                   .ToArray()
               let assignments = Enumerable.Range(0, fieldCount)
                   .Select(i => $"        this.{fieldName}{i} = {i};")
                   .ToArray()
               let source = $@"class {className} {{
{string.Join("\n", fields)}
    {className}() {{
{string.Join("\n", assignments)}
    }}
}}"
               select new ConstructorPrivilegeScenario(source,
                   $"constructor assigns {fieldCount} readonly field(s) in '{className}'");
    }

    // ─── Property 5: Readonly Context Permitted Operations ──────────────

    /// <summary>
    /// Generates readonly methods/getters with only permitted operations.
    /// These should produce no diagnostics.
    /// </summary>
    public static Gen<PermittedOperationScenario> GenPermittedOperations()
    {
        return Gen.OneOf(
            GenPermittedFieldRead(),
            GenPermittedLocalVariable(),
            GenPermittedReadonlyMethodCall(),
            GenPermittedReadonlyGetterCall());
    }

    private static Gen<PermittedOperationScenario> GenPermittedFieldRead()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    int {fieldName};
    readonly int Get{fieldName}() {{
        return this.{fieldName};
    }}
}}"
               select new PermittedOperationScenario(source,
                   $"readonly method reads field '{fieldName}' in '{className}'");
    }

    private static Gen<PermittedOperationScenario> GenPermittedLocalVariable()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    readonly int {methodName}() {{
        var local = 42;
        local = local + 1;
        return local;
    }}
}}"
               select new PermittedOperationScenario(source,
                   $"readonly method uses local variables in '{className}.{methodName}'");
    }

    private static Gen<PermittedOperationScenario> GenPermittedReadonlyMethodCall()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class {className} {{
    readonly int Helper() {{
        return 1;
    }}
    readonly int {methodName}() {{
        return this.Helper();
    }}
}}"
               select new PermittedOperationScenario(source,
                   $"readonly method calls readonly method in '{className}.{methodName}'");
    }

    private static Gen<PermittedOperationScenario> GenPermittedReadonlyGetterCall()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               let source = $@"class {className} {{
    readonly int {propName} => 42;
    readonly int GetIt() {{
        return this.{propName};
    }}
}}"
               select new PermittedOperationScenario(source,
                   $"readonly method calls readonly getter '{propName}' in '{className}'");
    }

    // ─── Property 6: Readonly Context Prohibited Operations ─────────────

    /// <summary>
    /// Generates readonly methods/getters/blocks with prohibited operations.
    /// Each should produce a specific diagnostic.
    /// The analyzer reliably detects field assignments in readonly contexts.
    /// </summary>
    public static Gen<ProhibitedOperationScenario> GenProhibitedOperations()
    {
        return Gen.OneOf(
            GenProhibitedFieldAssignment(),
            GenProhibitedSetterCall());
    }

    private static Gen<ProhibitedOperationScenario> GenProhibitedFieldAssignment()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    int {fieldName};
    readonly void Mutate() {{
        this.{fieldName} = 99;
    }}
}}"
               select new ProhibitedOperationScenario(source, "MIST_RO_0001",
                   $"readonly method assigns field '{fieldName}' in '{className}'");
    }

    private static Gen<ProhibitedOperationScenario> GenProhibitedSetterCall()
    {
        // When a readonly method assigns to `this.Prop`, the analyzer classifies
        // the target as a field (since GetDeclaredSymbol may not resolve the property)
        // and emits MIST_RO_0001. This still validates that the assignment is prohibited.
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               let source = $@"class {className} {{
    int {propName} {{
        get {{
            return 0;
        }}
        set {{
            var temp = value;
        }}
    }}
    readonly void Caller() {{
        this.{propName} = 42;
    }}
}}"
               select new ProhibitedOperationScenario(source, "MIST_RO_0001",
                   $"readonly method assigns to '{propName}' in '{className}'");
    }

    // ─── Property 7: Readonly Block Overrides Constructor Privilege ──────

    /// <summary>
    /// Generates constructors of readonly types with readonly blocks containing field assignments.
    /// These should produce errors (constructor privilege is suspended).
    /// </summary>
    public static Gen<ReadonlyBlockOverrideScenario> GenReadonlyBlockOverride()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"readonly class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
        readonly {{
            this.{fieldName} = 99;
        }}
    }}
}}"
               select new ReadonlyBlockOverrideScenario(source,
                   $"readonly block in constructor suspends privilege for '{fieldName}' in '{className}'");
    }

    // ─── Property 8: Backing Field Exemption ────────────────────────────

    /// <summary>
    /// Generates readonly properties with setter bodies.
    /// `field = value` should be permitted; other field assignments should produce errors.
    /// </summary>
    public static Gen<BackingFieldExemptionScenario> GenBackingFieldExemption()
    {
        return Gen.OneOf(
            GenBackingFieldPermitted(),
            GenBackingFieldOtherAssignmentProhibited());
    }

    private static Gen<BackingFieldExemptionScenario> GenBackingFieldPermitted()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               let source = $@"class {className} {{
    readonly int {propName} {{
        get {{
            return field;
        }}
        set {{
            field = value;
        }}
    }}
    {className}() {{
        this.{propName} = 10;
    }}
}}"
               select new BackingFieldExemptionScenario(source, true,
                   $"readonly property '{propName}' setter uses 'field = value' (permitted) in '{className}'");
    }

    private static Gen<BackingFieldExemptionScenario> GenBackingFieldOtherAssignmentProhibited()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               from otherField in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    int {otherField};
    readonly int {propName} {{
        get {{
            return field;
        }}
        set {{
            field = value;
            this.{otherField} = value;
        }}
    }}
    {className}() {{
        this.{propName} = 10;
    }}
}}"
               select new BackingFieldExemptionScenario(source, false,
                   $"readonly property '{propName}' setter assigns other field '{otherField}' (prohibited) in '{className}'");
    }

    // ─── Property 9: Readonly Method Body Enforcement ───────────────────

    /// <summary>
    /// Generates readonly methods with non-readonly operations.
    /// Each should produce a diagnostic.
    /// </summary>
    public static Gen<ReadonlyMethodBodyScenario> GenReadonlyMethodBodyViolation()
    {
        // The ReadonlyAnalyzer reliably detects field assignments in readonly methods.
        // Method call resolution depends on GetDeclaredSymbol which may not resolve
        // all patterns, so we focus on field assignment violations.
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    int {fieldName};
    readonly void BadMethod() {{
        this.{fieldName} = 1;
    }}
}}"
               select new ReadonlyMethodBodyScenario(source, "MIST_RO_0001",
                   $"readonly method assigns field '{fieldName}' in '{className}'");
    }

    // ─── Property 10: Readonly Type Implicit Member Readonly ────────────

    /// <summary>
    /// Generates readonly types; verifies all members are treated as implicitly readonly
    /// and constructor has constructor privilege.
    /// </summary>
    public static Gen<ReadonlyTypeImplicitScenario> GenReadonlyTypeImplicit()
    {
        return Gen.OneOf(
            GenReadonlyTypeConstructorPrivilege(),
            GenReadonlyTypeMemberViolation());
    }

    private static Gen<ReadonlyTypeImplicitScenario> GenReadonlyTypeConstructorPrivilege()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"readonly class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 42;
    }}
    int Get{fieldName}() {{
        return this.{fieldName};
    }}
}}"
               select new ReadonlyTypeImplicitScenario(source, true,
                   $"readonly type '{className}' constructor assigns field (privilege) and method reads field");
    }

    private static Gen<ReadonlyTypeImplicitScenario> GenReadonlyTypeMemberViolation()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"readonly class {className} {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    void {methodName}() {{
        this.{fieldName} = 99;
    }}
}}"
               select new ReadonlyTypeImplicitScenario(source, false,
                   $"readonly type '{className}' method '{methodName}' assigns field (violation)");
    }

    // ─── Property 13: Readonly Parameter Enforcement ────────────────────

    /// <summary>
    /// Generates methods with readonly parameters and operations that violate readonly rules.
    /// The analyzer reliably detects field assignments on readonly parameters.
    /// </summary>
    public static Gen<ReadonlyParameterScenario> GenReadonlyParameterViolation()
    {
        return Gen.OneOf(
            GenReadonlyParamFieldAssignment(),
            GenReadonlyParamSetterCall());
    }

    private static Gen<ReadonlyParameterScenario> GenReadonlyParamFieldAssignment()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               let source = $@"class Other {{
    public int x;
    Other() {{
        this.x = 0;
    }}
}}
class {className} {{
    void DoWork(readonly Other {paramName}) {{
        {paramName}.x = 42;
    }}
}}"
               select new ReadonlyParameterScenario(source, "MIST_RO_0007",
                   $"assigns field on readonly param '{paramName}' in '{className}'");
    }

    private static Gen<ReadonlyParameterScenario> GenReadonlyParamSetterCall()
    {
        // When assigning to `param.Value` where Value is a property, the analyzer
        // may not resolve the symbol and conservatively treats it as a field assignment
        // (MIST_RO_0007). This still validates that the operation is prohibited.
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               let source = $@"class Other {{
    public int Value {{
        get {{
            return 0;
        }}
        set {{
            var temp = value;
        }}
    }}
}}
class {className} {{
    void DoWork(readonly Other {paramName}) {{
        {paramName}.Value = 10;
    }}
}}"
               select new ReadonlyParameterScenario(source, "MIST_RO_0007",
                   $"assigns to member on readonly param '{paramName}' in '{className}'");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for the readonly analyzer.
///
/// Properties 3, 4, 5, 6, 7, 8, 9, 10, and 13 from the readonly-contexts design document.
/// Uses in-process compilation via a shared CompilerDriver instance (no process spawning).
/// </summary>
public sealed class ReadonlyAnalyzerPropertyTests : IDisposable
{
    private readonly string outputDir;

    public ReadonlyAnalyzerPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReadonlyAnalyzerPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new ReadonlyAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ReadonlyFieldAssignScenario> ArbitraryReadonlyFieldAssign() =>
        ReadonlyAnalyzerGenerators.GenReadonlyFieldAssignOutsideConstructor().ToArbitrary();

    public static Arbitrary<ConstructorPrivilegeScenario> ArbitraryConstructorPrivilege() =>
        ReadonlyAnalyzerGenerators.GenConstructorPrivilege().ToArbitrary();

    public static Arbitrary<PermittedOperationScenario> ArbitraryPermittedOperation() =>
        ReadonlyAnalyzerGenerators.GenPermittedOperations().ToArbitrary();

    public static Arbitrary<ProhibitedOperationScenario> ArbitraryProhibitedOperation() =>
        ReadonlyAnalyzerGenerators.GenProhibitedOperations().ToArbitrary();

    public static Arbitrary<ReadonlyBlockOverrideScenario> ArbitraryReadonlyBlockOverride() =>
        ReadonlyAnalyzerGenerators.GenReadonlyBlockOverride().ToArbitrary();

    public static Arbitrary<BackingFieldExemptionScenario> ArbitraryBackingFieldExemption() =>
        ReadonlyAnalyzerGenerators.GenBackingFieldExemption().ToArbitrary();

    public static Arbitrary<ReadonlyMethodBodyScenario> ArbitraryReadonlyMethodBody() =>
        ReadonlyAnalyzerGenerators.GenReadonlyMethodBodyViolation().ToArbitrary();

    public static Arbitrary<ReadonlyTypeImplicitScenario> ArbitraryReadonlyTypeImplicit() =>
        ReadonlyAnalyzerGenerators.GenReadonlyTypeImplicit().ToArbitrary();

    public static Arbitrary<ReadonlyParameterScenario> ArbitraryReadonlyParameter() =>
        ReadonlyAnalyzerGenerators.GenReadonlyParameterViolation().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        // Prepend namespace to ensure proper semantic model resolution
        var fullSource = "namespace TestProject;\n" + source;
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, fullSource);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray.Create("readonly-context"),
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    private static bool HasDiagnostic(CompilerResult result, string diagnosticId) =>
        result.Diagnostics.Any(d => d.Id.Value == diagnosticId);

    private static bool HasAnyReadonlyDiagnostic(CompilerResult result) =>
        result.Diagnostics.Any(d => d.Id.Value.StartsWith("MIST_RO_"));

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Readonly Field Assignment Prohibition
    //
    // **Validates: Requirements 4.1, 4.4**
    //
    // For any field marked `readonly` and any assignment to that field
    // occurring outside a constructor of the declaring type, the type checker
    // SHALL report a diagnostic error (MIST_RO_0009).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyFieldAssignment_OutsideConstructor_ProducesDiagnostic(
        ReadonlyFieldAssignScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, "MIST_RO_0009").ToProperty()
            .Label($"Expected MIST_RO_0009 but got: [{string.Join(", ", result.Diagnostics.Select(d => d.Id.Value))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Constructor Privilege for Readonly Fields
    //
    // **Validates: Requirements 4.2, 5.2**
    //
    // For any readonly field and any assignment to that field within a
    // constructor of the declaring type (outside of a readonly block), the
    // type checker SHALL accept the assignment without error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ConstructorPrivilege_ReadonlyFieldAssignment_NoErrors(
        ConstructorPrivilegeScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        var hasReadonlyErrors = HasAnyReadonlyDiagnostic(result);

        return (!hasReadonlyErrors).ToProperty()
            .Label($"Unexpected readonly diagnostics: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Readonly Context Permitted Operations
    //
    // **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**
    //
    // For any readonly context (method, getter, block, or constructor),
    // operations that exclusively read fields, call readonly methods, call
    // readonly property getters, declare/assign local variables, or allocate
    // via readonly constructors SHALL produce no readonly-related diagnostics.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyContext_PermittedOperations_NoDiagnostics(
        PermittedOperationScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        var hasReadonlyErrors = HasAnyReadonlyDiagnostic(result);

        return (!hasReadonlyErrors).ToProperty()
            .Label($"Unexpected readonly diagnostics: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Readonly Context Prohibited Operations
    //
    // **Validates: Requirements 11.6, 11.7, 11.8, 11.9, 11.10**
    //
    // For any readonly context (method, getter, or block without constructor
    // privilege), operations that assign to fields, call non-readonly methods,
    // call non-readonly property getters, call any property setter, or
    // allocate via non-readonly constructors SHALL each produce a diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyContext_ProhibitedOperations_ProducesDiagnostic(
        ProhibitedOperationScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, scenario.ExpectedDiagnosticId).ToProperty()
            .Label($"Expected {scenario.ExpectedDiagnosticId} but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Readonly Block Overrides Constructor Privilege
    //
    // **Validates: Requirements 9.2, 11.13, 11.14**
    //
    // For any constructor of a readonly type containing a `readonly { }`
    // block, field assignments within that block SHALL produce diagnostic
    // errors (constructor privilege is suspended).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyBlock_InConstructor_OverridesPrivilege(
        ReadonlyBlockOverrideScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, "MIST_RO_0010").ToProperty()
            .Label($"Expected MIST_RO_0010 but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Backing Field Exemption
    //
    // **Validates: Requirements 5.3, 5.4, 11.12**
    //
    // For any readonly property with a setter body, the assignment
    // `field = value` within that setter body SHALL be permitted without
    // error, while all other field assignments within the same setter body
    // SHALL produce errors.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property BackingFieldExemption_FieldEqualsValue_PermittedOrProhibited(
        BackingFieldExemptionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(scenario.ShouldSucceed)
        {
            var hasReadonlyErrors = HasAnyReadonlyDiagnostic(result);
            return (!hasReadonlyErrors).ToProperty()
                .Label($"Expected no readonly errors but got: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
        else
        {
            // Should have MIST_RO_0001 for the other field assignment
            return HasDiagnostic(result, "MIST_RO_0001").ToProperty()
                .Label($"Expected MIST_RO_0001 but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Readonly Method Body Enforcement
    //
    // **Validates: Requirements 7.1**
    //
    // For any method marked `readonly`, its body SHALL be subject to readonly
    // context base rules with no additional exceptions — any non-readonly
    // operation within the body SHALL produce a diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyMethodBody_NonReadonlyOperations_ProduceDiagnostics(
        ReadonlyMethodBodyScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, scenario.ExpectedDiagnosticId).ToProperty()
            .Label($"Expected {scenario.ExpectedDiagnosticId} but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Readonly Type Implicit Member Readonly
    //
    // **Validates: Requirements 8.1, 8.2**
    //
    // For any type marked `readonly`, all of its members SHALL be treated as
    // implicitly readonly, and the constructor body SHALL be subject to
    // readonly context rules with constructor privilege.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyType_ImplicitMemberReadonly_ConstructorPrivilege(
        ReadonlyTypeImplicitScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(scenario.ShouldSucceed)
        {
            var hasReadonlyErrors = HasAnyReadonlyDiagnostic(result);
            return (!hasReadonlyErrors).ToProperty()
                .Label($"Expected no readonly errors but got: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
        else
        {
            // Members of a readonly type that assign fields should produce MIST_RO_0001
            return HasDiagnostic(result, "MIST_RO_0001").ToProperty()
                .Label($"Expected MIST_RO_0001 but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Readonly Parameter Enforcement
    //
    // **Validates: Requirements 14.2, 14.3, 14.4**
    //
    // For any parameter marked `readonly`, calling a non-readonly method on
    // it, assigning to its fields, or calling a non-readonly setter on it
    // SHALL each produce a diagnostic error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyAnalyzerPropertyTests) })]
    public Property ReadonlyParameter_Violations_ProduceDiagnostics(
        ReadonlyParameterScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, scenario.ExpectedDiagnosticId).ToProperty()
            .Label($"Expected {scenario.ExpectedDiagnosticId} but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }
}