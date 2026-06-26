// Feature: ownership-system, Property 7: Owned type field constraints

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
/// Property-based tests for owned type field constraints.
///
/// Property 7: Owned type field constraints
/// For any type declared as <c>owned class</c> or <c>owned struct</c>, all reference-type
/// fields SHALL themselves be declared as <c>owned</c> or be of an owned type. Any
/// reference-type field without the <c>owned</c> specifier SHALL produce a diagnostic error.
/// Instances of owned types SHALL always be held in <c>owned</c> variables. Owned types
/// SHALL NOT implement abstract types.
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
    /// Scenario for owned type with field configurations that should produce
    /// MIST_OWN_FIELD_NOT_OWNED diagnostics.
    /// </summary>
    public sealed record FieldNotOwnedScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for owned type with valid field configurations (all owned or value types).
    /// </summary>
    public sealed record ValidOwnedTypeScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for owned type implementing an abstract type (should produce
    /// MIST_OWN_ABSTRACT_FORBIDDEN).
    /// </summary>
    public sealed record AbstractForbiddenScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for owned type instance held without the owned specifier (now permitted
    /// under the semantic shift — verifies NO MIST_OWN_TYPE_REQUIRES_OWNED is emitted).
    /// </summary>
    public sealed record TypeRequiresOwnedScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario for Property 4: owned type used in various binding contexts
    /// (unrestricted with var, unrestricted with explicit type, owned binding,
    /// and passed to unrestricted parameters).
    /// </summary>
    public sealed record SemanticShiftScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "value", "item", "content", "payload", "element"];

    private static readonly string[] RefTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] AbstractNames =
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

    // Generator: owned type implementing an abstract type (should fail)
    private static Gen<AbstractForbiddenScenario> GenAbstractForbiddenScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from abstractName in Gen.Elements(AbstractNames)
               let source = $$"""
                   abstract {{abstractName}} {
                       void DoWork();
                   }
                   owned class {{typeName}} : {{abstractName}} {
                       public void DoWork() {
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new AbstractForbiddenScenario(source, $"owned class {typeName} implementing {abstractName}");
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

    public static Arbitrary<FieldNotOwnedScenario> ArbitraryFieldNotOwnedScenario()
    {
        return GenFieldNotOwnedScenario().ToArbitrary();
    }

    public static Arbitrary<ValidOwnedTypeScenario> ArbitraryValidOwnedTypeScenario()
    {
        return GenValidOwnedTypeScenario().ToArbitrary();
    }

    public static Arbitrary<AbstractForbiddenScenario> ArbitraryAbstractForbiddenScenario()
    {
        return GenAbstractForbiddenScenario().ToArbitrary();
    }

    public static Arbitrary<TypeRequiresOwnedScenario> ArbitraryTypeRequiresOwnedScenario()
    {
        return GenTypeRequiresOwnedScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ownership v2: owned types may declare non-owned reference-type fields — the
    // v1 field restriction (MIST_OWN_FIELD_NOT_OWNED) is removed (Req 10 AC 1–3).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool NonOwnedRefField_InOwnedType_EmitsFieldNotOwned(FieldNotOwnedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // v2: the owned-type field restriction is removed — a non-owned ref field in an
        // owned type is permitted and MIST_OWN_FIELD_NOT_OWNED is never emitted (Req 10).
        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
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
    // Ownership v2: owned types MAY implement abstract types — the v1 "owned types
    // cannot implement abstract types" rejection is removed (Req 5 AC 6 narrows
    // MIST_OWN_ABSTRACT_FORBIDDEN to owned abstract type DECLARATIONS only).
    // An owned class implementing an abstract type (e.g. IClonable) is now valid.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool OwnedTypeWithAbstract_EmitsAbstractForbidden(AbstractForbiddenScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // v2: an owned class implementing an abstract type is permitted; the rejection
        // applies only to `owned abstract` declarations (Req 5 AC 6).
        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_ABSTRACT_FORBIDDEN");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7d: Owned type instances held without owned specifier are now
    // permitted (semantic shift — deep-ownership-types spec, Requirement 8).
    // Previously this tested that MIST_OWN_TYPE_REQUIRES_OWNED was emitted;
    // now it verifies that NO such diagnostic is produced.
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4 (deep-ownership-types)**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool OwnedTypeInUnrestrictedBinding_IsPermitted(TypeRequiresOwnedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Feature: deep-ownership-types, Property 4: Semantic shift — owned types usable in any binding context
    //
    // For any owned type, instances SHALL be permitted in both owned bindings
    // (owned var x = new T()) and unrestricted bindings (var x = new T())
    // without the type checker reporting an error. Passing an owned type
    // instance to an unrestricted parameter SHALL also be permitted.
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 9.2, 9.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents the binding context in which an owned type instance is used.
    /// </summary>
    private enum BindingContext
    {
        /// <summary>var x = new T() — unrestricted with type inference</summary>
        UnrestrictedVar,
        /// <summary>T x = new T() — unrestricted with explicit type</summary>
        UnrestrictedExplicit,
        /// <summary>owned var x = new T() — owned binding with var</summary>
        OwnedVar,
        /// <summary>owned T x = new T() — owned binding with explicit type</summary>
        OwnedExplicit,
        /// <summary>Pass owned type instance to unrestricted parameter</summary>
        UnrestrictedParameter,
    }

    private static Gen<SemanticShiftScenario> GenSemanticShiftScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               from context in Gen.Elements(
                   BindingContext.UnrestrictedVar,
                   BindingContext.UnrestrictedExplicit,
                   BindingContext.OwnedVar,
                   BindingContext.OwnedExplicit,
                   BindingContext.UnrestrictedParameter)
               let source = BuildSemanticShiftSource(typeName, varName, context)
               let label = $"owned class {typeName} in {context} context (var={varName})"
               select new SemanticShiftScenario(source, label);
    }

    private static string BuildSemanticShiftSource(string typeName, string varName, BindingContext context)
    {
        var declaration = context switch
        {
            BindingContext.UnrestrictedVar =>
                $"var {varName} = new {typeName}();",
            BindingContext.UnrestrictedExplicit =>
                $"{typeName} {varName} = new {typeName}();",
            BindingContext.OwnedVar =>
                $"owned var {varName} = new {typeName}();",
            BindingContext.OwnedExplicit =>
                $"owned {typeName} {varName} = new {typeName}();",
            BindingContext.UnrestrictedParameter =>
                $"var {varName} = new {typeName}();\n            AcceptUnrestricted({varName});",
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };

        var helperMethod = context == BindingContext.UnrestrictedParameter
            ? $"\n    public static void AcceptUnrestricted({typeName} param) {{\n    }}"
            : "";

        return $$"""
            owned class {{typeName}} {
                int value;
            }
            class Program {
                public static void Main() {
                    {{declaration}}
                }{{helperMethod}}
            }
            """;
    }

    public static Arbitrary<SemanticShiftScenario> ArbitrarySemanticShiftScenario()
    {
        return GenSemanticShiftScenario().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedTypeConstraintPropertyTests) })]
    public bool SemanticShift_OwnedTypeInAnyBindingContext_NoErrors(SemanticShiftScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // No ownership-related errors should be reported — owned types are usable
        // in any binding context after the semantic shift.
        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED" ||
            d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }
}