// Feature: ownership-system, Property 7: Owned type field constraints

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
/// Property-based tests for owned type field constraints.
///
/// Property 7: Owned type field constraints
/// For any type declared as <c>owned class</c> or <c>owned struct</c>, all reference-type
/// fields SHALL themselves be declared as <c>owned</c> or be of an owned type. Any
/// reference-type field without the <c>owned</c> specifier SHALL produce a diagnostic error.
/// Instances of owned types SHALL always be held in <c>owned</c> variables. Owned types
/// SHALL NOT implement interfaces or satisfy traits.
///
/// **Validates: Requirements 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 16.5**
/// </summary>
public class OwnedTypeConstraintPropertyTests
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
    /// Scenario for owned type with field configurations that should produce
    /// MIST_OWN_FIELD_NOT_OWNED diagnostics.
    /// </summary>
    public sealed record FieldNotOwnedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for owned type with valid field configurations (all owned or value types).
    /// </summary>
    public sealed record ValidOwnedTypeScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for owned type implementing an interface (should produce
    /// MIST_OWN_INTERFACE_FORBIDDEN).
    /// </summary>
    public sealed record InterfaceForbiddenScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for owned type instance held without the owned specifier (should produce
    /// MIST_OWN_TYPE_REQUIRES_OWNED).
    /// </summary>
    public sealed record TypeRequiresOwnedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "value", "item", "content", "payload", "element"];

    private static readonly string[] RefTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] InterfaceNames =
        ["IReadable", "IWritable", "IDisposable", "ICloneable", "IComparable", "ISerializable"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "instance", "item", "thing", "target"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    // ─── Generators ──────────────────────────────────────────────────────

    // Generator: owned type with a non-owned reference-type field (should fail)
    private static Gen<FieldNotOwnedScenario> GenFieldNotOwnedScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from refTypeName in Gen.Elements(RefTypeNames)
               let source = $$"""
                   class {{refTypeName}} {
                   }
                   owned class {{typeName}} {
                       {{refTypeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new FieldNotOwnedScenario(source, $"owned class {typeName} with non-owned field {fieldName}:{refTypeName}");
    }

    // Generator: owned type with all valid fields (owned ref fields + value type fields)
    private static Gen<ValidOwnedTypeScenario> GenValidOwnedTypeScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from refTypeName in Gen.Elements(RefTypeNames)
               from valueFieldName in Gen.Elements(new[] { "count", "size", "length", "index" })
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{refTypeName}} {
                       {{valueType}} amount;
                   }
                   owned class {{typeName}} {
                       owned {{refTypeName}} {{fieldName}};
                       {{valueType}} {{valueFieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ValidOwnedTypeScenario(source, $"owned class {typeName} with owned field {fieldName}:{refTypeName} and value field {valueFieldName}:{valueType}");
    }

    // Generator: owned type implementing an interface (should fail)
    private static Gen<InterfaceForbiddenScenario> GenInterfaceForbiddenScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from ifaceName in Gen.Elements(InterfaceNames)
               let source = $$"""
                   interface {{ifaceName}} {
                       void DoWork();
                   }
                   owned class {{typeName}} : {{ifaceName}} {
                       public void DoWork() {
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new InterfaceForbiddenScenario(source, $"owned class {typeName} implementing {ifaceName}");
    }

    // Generator: owned type instance held without owned specifier (should fail)
    private static Gen<TypeRequiresOwnedScenario> GenTypeRequiresOwnedScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = $$"""
                   owned class {{typeName}} {
                       int value;
                   }
                   class Program {
                       public static void Main() {
                           {{typeName}} {{varName}} = new {{typeName}}();
                       }
                   }
                   """
               select new TypeRequiresOwnedScenario(source, $"non-owned variable {varName} of owned type {typeName}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<FieldNotOwnedScenario> ArbitraryFieldNotOwnedScenario() =>
        GenFieldNotOwnedScenario().ToArbitrary();

    public static Arbitrary<ValidOwnedTypeScenario> ArbitraryValidOwnedTypeScenario() =>
        GenValidOwnedTypeScenario().ToArbitrary();

    public static Arbitrary<InterfaceForbiddenScenario> ArbitraryInterfaceForbiddenScenario() =>
        GenInterfaceForbiddenScenario().ToArbitrary();

    public static Arbitrary<TypeRequiresOwnedScenario> ArbitraryTypeRequiresOwnedScenario() =>
        GenTypeRequiresOwnedScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Non-owned reference-type fields in owned types produce
    // MIST_OWN_FIELD_NOT_OWNED
    // **Validates: Requirements 8.3, 16.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool NonOwnedRefField_InOwnedType_EmitsFieldNotOwned(FieldNotOwnedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Valid owned types (owned ref fields + value type fields)
    // produce no MIST_OWN_FIELD_NOT_OWNED diagnostics
    // **Validates: Requirements 8.3, 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool ValidOwnedType_NoFieldNotOwnedDiagnostic(ValidOwnedTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7c: Owned types implementing interfaces produce
    // MIST_OWN_INTERFACE_FORBIDDEN
    // **Validates: Requirements 8.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool OwnedTypeWithInterface_EmitsInterfaceForbidden(InterfaceForbiddenScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_INTERFACE_FORBIDDEN");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7d: Owned type instances held without owned specifier produce
    // MIST_OWN_TYPE_REQUIRES_OWNED
    // **Validates: Requirements 8.5, 8.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool OwnedTypeWithoutOwnedVar_EmitsTypeRequiresOwned(TypeRequiresOwnedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED");
    }
}
