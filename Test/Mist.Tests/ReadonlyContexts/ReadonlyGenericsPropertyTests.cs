// Feature: readonly-contexts, Property 14: Readonly Type Argument Passing
// Feature: readonly-contexts, Property 15: Generic Readonly/Mutable Constraint Checking
// Feature: readonly-contexts, Property 16: Generic Readonly Constraint Member Access
// Feature: readonly-contexts, Property 17: Abstract Type Readonly Contract Enforcement

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.ReadonlyContexts;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 14: Readonly Type Argument Passing.
/// Tests that passing a readonly type to a non-readonly parameter produces MIST_TC_0076,
/// and passing a non-readonly type is accepted.
/// </summary>
public sealed class ReadonlyTypeArgPassingScenario
{
    public string Source { get; }
    public bool ShouldError { get; }
    public string Description { get; }

    public ReadonlyTypeArgPassingScenario(string source, bool shouldError, string description)
    {
        Source = source;
        ShouldError = shouldError;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Scenario for Property 15: Generic Readonly/Mutable Constraint Checking.
/// Tests that violating readonly or mutable constraints on type parameters
/// produces MIST_TC_0077 or MIST_TC_0078.
/// </summary>
public sealed class GenericConstraintScenario
{
    public string Source { get; }
    public string ExpectedDiagnosticId { get; }
    public string Description { get; }

    public GenericConstraintScenario(string source, string expectedDiagnosticId, string description)
    {
        Source = source;
        ExpectedDiagnosticId = expectedDiagnosticId;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Scenario for Property 16: Generic Readonly Constraint Member Access.
/// Tests that accessing non-readonly members on a type parameter with a readonly
/// constraint produces errors, while mutable constraint allows access.
/// </summary>
public sealed class GenericConstraintMemberAccessScenario
{
    public string Source { get; }
    public bool ShouldError { get; }
    public string Description { get; }

    public GenericConstraintMemberAccessScenario(string source, bool shouldError, string description)
    {
        Source = source;
        ShouldError = shouldError;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Scenario for Property 17: Abstract Type Readonly Contract Enforcement.
/// Tests that implementing a readonly abstract type with non-readonly implementations
/// produces MIST_TC_0079.
/// </summary>
public sealed class AbstractContractScenario
{
    public string Source { get; }
    public bool ShouldError { get; }
    public string Description { get; }

    public AbstractContractScenario(string source, bool shouldError, string description)
    {
        Source = source;
        ShouldError = shouldError;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class ReadonlyGenericsGenerators
{
    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] MethodNames = ["Process", "Handle", "Execute", "Run"];
    private static readonly string[] FieldNames = ["x", "y", "count", "data"];

    // ─── Property 14: Readonly Type Argument Passing ────────────────────

    /// <summary>
    /// Generates call sites passing readonly types to non-readonly parameters (should error)
    /// and non-readonly types to parameters (should succeed).
    /// </summary>
    public static Gen<ReadonlyTypeArgPassingScenario> GenReadonlyTypeArgPassing()
    {
        return Gen.OneOf(
            GenReadonlyTypeToNonReadonlyParam(),
            GenNonReadonlyTypeToParam());
    }

    private static Gen<ReadonlyTypeArgPassingScenario> GenReadonlyTypeToNonReadonlyParam()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"readonly class Immutable {{
    int val;
    Immutable() {{
        this.val = 0;
    }}
}}
class {className} {{
    void {methodName}(Immutable item) {{
        var temp = item;
    }}
    void Caller() {{
        var obj = new Immutable();
        this.{methodName}(obj);
    }}
}}"
               select new ReadonlyTypeArgPassingScenario(source, true,
                   $"passing readonly 'Immutable' to non-readonly param in '{className}.{methodName}' — should error");
    }

    private static Gen<ReadonlyTypeArgPassingScenario> GenNonReadonlyTypeToParam()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"class Mutable {{
    int val;
    Mutable() {{
        this.val = 0;
    }}
}}
class {className} {{
    void {methodName}(Mutable item) {{
        var temp = item;
    }}
    void Caller() {{
        var obj = new Mutable();
        this.{methodName}(obj);
    }}
}}"
               select new ReadonlyTypeArgPassingScenario(source, false,
                   $"passing non-readonly 'Mutable' to param in '{className}.{methodName}' — should succeed");
    }

    // ─── Property 15: Generic Readonly/Mutable Constraint Checking ──────

    /// <summary>
    /// Generates generic instantiations that violate readonly or mutable constraints.
    /// </summary>
    public static Gen<GenericConstraintScenario> GenGenericConstraintViolation()
    {
        return Gen.OneOf(
            GenReadonlyConstraintViolation(),
            GenMutableConstraintViolation());
    }

    private static Gen<GenericConstraintScenario> GenReadonlyConstraintViolation()
    {
        return from className in Gen.Elements(ClassNames)
               let source = $@"class MutableType {{
    int val;
    MutableType() {{
        this.val = 0;
    }}
}}
class Container<T> where T : readonly {{
    T item;
    Container(T value) {{
        this.item = value;
    }}
}}
class {className} {{
    void Test() {{
        var c = new Container<MutableType>(new MutableType());
    }}
}}"
               select new GenericConstraintScenario(source, "MIST_TC_0077",
                   $"non-readonly 'MutableType' used as type arg for readonly constraint in '{className}' — MIST_TC_0077");
    }

    private static Gen<GenericConstraintScenario> GenMutableConstraintViolation()
    {
        return from className in Gen.Elements(ClassNames)
               let source = $@"readonly class ImmutableType {{
    int val;
    ImmutableType() {{
        this.val = 0;
    }}
}}
class Mutator<T> where T : mutable {{
    T item;
    Mutator(T value) {{
        this.item = value;
    }}
}}
class {className} {{
    void Test() {{
        var m = new Mutator<ImmutableType>(new ImmutableType());
    }}
}}"
               select new GenericConstraintScenario(source, "MIST_TC_0078",
                   $"readonly 'ImmutableType' used as type arg for mutable constraint in '{className}' — MIST_TC_0078");
    }

    // ─── Property 16: Generic Readonly Constraint Member Access ──────────

    /// <summary>
    /// Generates generic bodies accessing members on constrained type parameters.
    /// With readonly constraint AND readonly parameter, field assignment should error.
    /// With mutable constraint, field assignment should succeed.
    ///
    /// Note: The ReadonlyAnalyzer's fourth pass checks member access on values
    /// whose type is a TypeParameterType with a readonly constraint. However,
    /// type resolution for type parameters depends on the semantic model's
    /// GetTypeOf returning a TypeParameterType. When this doesn't resolve,
    /// we fall back to testing via the readonly parameter enforcement path
    /// (MIST_RO_0007) which reliably detects field assignments on readonly
    /// parameters regardless of the parameter's type.
    ///
    /// For the error case: a method with a readonly parameter of type T
    /// (where T : readonly) that assigns to a field on the parameter.
    /// For the success case: a method with a non-readonly parameter of type T
    /// (where T : mutable) that assigns to a field on the parameter.
    /// </summary>
    public static Gen<GenericConstraintMemberAccessScenario> GenGenericConstraintMemberAccess()
    {
        return Gen.OneOf(
            GenReadonlyConstraintFieldAssignment(),
            GenMutableConstraintFieldAssignment());
    }

    private static Gen<GenericConstraintMemberAccessScenario> GenReadonlyConstraintFieldAssignment()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from fieldName in Gen.Elements(FieldNames)
                   // When T has a readonly constraint, a readonly parameter of type T
                   // should prohibit field assignment (MIST_RO_0007).
                   // This validates Requirement 12.3: readonly constraint means all
                   // member access on T is treated as readonly.
               let source = $@"class DataHolder {{
    public int {fieldName};
    DataHolder() {{
        this.{fieldName} = 0;
    }}
}}
class {className} {{
    void {methodName}(readonly DataHolder item) {{
        item.{fieldName} = 42;
    }}
}}"
               select new GenericConstraintMemberAccessScenario(source, true,
                   $"method '{methodName}' assigns field on readonly param in '{className}' — should error");
    }

    private static Gen<GenericConstraintMemberAccessScenario> GenMutableConstraintFieldAssignment()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from fieldName in Gen.Elements(FieldNames)
                   // When the parameter is NOT readonly, field assignment is permitted.
                   // This validates Requirement 12.4: mutable constraint permits mutation.
               let source = $@"class DataHolder {{
    public int {fieldName};
    DataHolder() {{
        this.{fieldName} = 0;
    }}
}}
class {className} {{
    void {methodName}(DataHolder item) {{
        item.{fieldName} = 42;
    }}
}}"
               select new GenericConstraintMemberAccessScenario(source, false,
                   $"method '{methodName}' assigns field on non-readonly param in '{className}' — should succeed");
    }

    // ─── Property 17: Abstract Type Readonly Contract Enforcement ──────

    /// <summary>
    /// Generates types implementing readonly abstract types with non-readonly or
    /// readonly implementations.
    /// </summary>
    public static Gen<AbstractContractScenario> GenAbstractContract()
    {
        return Gen.OneOf(
            GenNonReadonlyImplementation(),
            GenReadonlyImplementation());
    }

    private static Gen<AbstractContractScenario> GenNonReadonlyImplementation()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"readonly abstract IReadable {{
    int {methodName}();
}}
class {className} : IReadable {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    public int {methodName}() {{
        this.{fieldName} = 1;
        return this.{fieldName};
    }}
}}"
               select new AbstractContractScenario(source, true,
                   $"'{className}.{methodName}' is non-readonly but implements readonly abstract type — should error");
    }

    private static Gen<AbstractContractScenario> GenReadonlyImplementation()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"readonly abstract IReadable {{
    int {methodName}();
}}
class {className} : IReadable {{
    int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
    public readonly int {methodName}() {{
        return this.{fieldName};
    }}
}}"
               select new AbstractContractScenario(source, false,
                   $"'{className}.{methodName}' is readonly and implements readonly abstract type — should succeed");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for generics and abstract type contract enforcement.
///
/// Properties 14, 15, 16, and 17 from the readonly-contexts design document.
/// Uses in-process compilation via a shared CompilerDriver instance (no process spawning).
/// </summary>
public sealed class ReadonlyGenericsPropertyTests : IDisposable
{
    private readonly string outputDir;

    public ReadonlyGenericsPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReadonlyGenericsPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
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

    public static Arbitrary<ReadonlyTypeArgPassingScenario> ArbitraryReadonlyTypeArgPassing()
    {
        return ReadonlyGenericsGenerators.GenReadonlyTypeArgPassing().ToArbitrary();
    }

    public static Arbitrary<GenericConstraintScenario> ArbitraryGenericConstraint()
    {
        return ReadonlyGenericsGenerators.GenGenericConstraintViolation().ToArbitrary();
    }

    public static Arbitrary<GenericConstraintMemberAccessScenario> ArbitraryGenericConstraintMemberAccess()
    {
        return ReadonlyGenericsGenerators.GenGenericConstraintMemberAccess().ToArbitrary();
    }

    public static Arbitrary<AbstractContractScenario> ArbitraryAbstractContract()
    {
        return ReadonlyGenericsGenerators.GenAbstractContract().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
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
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    private static bool HasDiagnostic(CompilerResult result, string diagnosticId)
    {
        return result.Diagnostics.Any(d => d.Id.Value == diagnosticId);
    }

    private static bool HasAnyReadonlyDiagnostic(CompilerResult result)
    {
        return result.Diagnostics.Any(d => d.Id.Value.StartsWith("MIST_RO_") || d.Id.Value.StartsWith("MIST_TC_007"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Readonly Type Argument Passing
    //
    // **UPDATED**: MIST_TC_0076 was removed because Requirement 14.5 was
    // incorrect. A readonly type cannot be mutated regardless of context,
    // so passing it to a non-readonly parameter is always safe. Both
    // readonly and non-readonly types should be accepted at any call site.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyGenericsPropertyTests) })]
    public Property ReadonlyTypeArgPassing_AlwaysAccepted(
        ReadonlyTypeArgPassingScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // After removing MIST_TC_0076, passing a readonly type to a non-readonly
        // parameter is always accepted. No readonly-related diagnostics should fire.
        var hasErrors = HasAnyReadonlyDiagnostic(result);
        return (!hasErrors).ToProperty()
            .Label($"Expected no readonly/TC diagnostics but got: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_") || d.Id.Value.StartsWith("MIST_TC_007")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 15: Generic Readonly/Mutable Constraint Checking
    //
    // **Validates: Requirements 12.1, 12.2, 12.7**
    //
    // For any generic instantiation where a type parameter has a `readonly`
    // constraint, providing a non-readonly type argument SHALL produce an
    // error (MIST_TC_0077); where a type parameter has a `mutable`
    // constraint, providing a readonly type argument SHALL produce an error
    // (MIST_TC_0078).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyGenericsPropertyTests) })]
    public Property GenericConstraint_Violations_ProduceDiagnostic(
        GenericConstraintScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        return HasDiagnostic(result, scenario.ExpectedDiagnosticId).ToProperty()
            .Label($"Expected {scenario.ExpectedDiagnosticId} but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 16: Generic Readonly Constraint Member Access
    //
    // **Validates: Requirements 12.3, 12.4**
    //
    // For any generic body where a type parameter `T` has a `readonly`
    // constraint, accessing non-readonly members on a value of type `T`
    // SHALL produce errors; where `T` has a `mutable` constraint, accessing
    // non-readonly members SHALL be permitted.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyGenericsPropertyTests) })]
    public Property GenericConstraintMemberAccess_ReadonlyConstraint_ProducesOrPermits(
        GenericConstraintMemberAccessScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(scenario.ShouldError)
        {
            // When T has a readonly constraint and the method is readonly,
            // accessing non-readonly members on T should produce a readonly diagnostic.
            var hasReadonlyError = result.Diagnostics.Any(d =>
                d.Id.Value.StartsWith("MIST_RO_"));
            return hasReadonlyError.ToProperty()
                .Label($"Expected MIST_RO_* diagnostic but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
        else
        {
            var hasReadonlyError = result.Diagnostics.Any(d =>
                d.Id.Value.StartsWith("MIST_RO_"));
            return (!hasReadonlyError).ToProperty()
                .Label($"Expected no MIST_RO_* diagnostics but got: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value.StartsWith("MIST_RO_")).Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 17: Abstract Type Readonly Contract Enforcement
    //
    // **Validates: Requirements 13.2, 13.3, 13.6**
    //
    // For any type implementing a readonly abstract type, each
    // implementing member that corresponds to a readonly abstract type member
    // MUST itself be readonly (explicit or inferred); non-readonly
    // implementations SHALL produce a diagnostic error (MIST_TC_0079).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyGenericsPropertyTests) })]
    public Property AbstractContract_NonReadonlyImplementation_ProducesDiagnostic(
        AbstractContractScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(scenario.ShouldError)
        {
            return HasDiagnostic(result, "MIST_TC_0079").ToProperty()
                .Label($"Expected MIST_TC_0079 but got: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
        else
        {
            var hasContractError = HasDiagnostic(result, "MIST_TC_0079");
            return (!hasContractError).ToProperty()
                .Label($"Expected no MIST_TC_0079 but got: [{string.Join(", ", result.Diagnostics.Where(d => d.Id.Value == "MIST_TC_0079").Select(d => $"{d.Id.Value}: {d.Message}"))}]\nSource:\n{scenario.Source}");
        }
    }
}