// Feature: deep-ownership-types, Property 5: Owned binding requires owned type and unique initializer

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
/// Property-based tests for owned binding type validation (Pass 2b).
///
/// Property 5: Owned Binding Requires Owned Type and Unique Initializer
/// For any variable declared with the 'owned' specifier, the type checker SHALL accept
/// the declaration if and only if (a) the variable's type is an owned type AND (b) the
/// initializer yields unique ownership (constructor call, move from another owned binding,
/// or call to an owned-returning function). If either condition is violated, the type
/// checker SHALL report an error.
///
/// **Validates: Requirements 4.3, 4.4, 4.5, 4.6**
/// </summary>
public class OwnedBindingValidationPropertyTests
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
    /// Scenario for a valid owned binding: owned type + unique initializer.
    /// Should compile without MIST_OWN_TYPE_REQUIRES_OWNED.
    /// </summary>
    public sealed record ValidOwnedBindingScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned binding with a non-owned type.
    /// Should report MIST_OWN_TYPE_REQUIRES_OWNED.
    /// </summary>
    public sealed record NonOwnedTypeScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for an owned binding with an invalid initializer
    /// (not new, not owned-returning function, not move from owned binding).
    /// Should report MIST_OWN_TYPE_REQUIRES_OWNED.
    /// </summary>
    public sealed record InvalidInitializerScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] OwnedTypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage"];

    private static readonly string[] NonOwnedTypeNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "instance", "item"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    // ─── Valid initializer kinds ─────────────────────────────────────────

    private enum ValidInitializerKind
    {
        /// <summary>owned var x = new T()</summary>
        ConstructorCall,
        /// <summary>owned var x = existingOwnedVar (move from owned binding)</summary>
        MoveFromOwnedBinding,
        /// <summary>owned var x = CreateOwned() (call to owned-returning function)</summary>
        OwnedReturningFunction,
    }

    // ─── Invalid initializer kinds ───────────────────────────────────────

    private enum InvalidInitializerKind
    {
        /// <summary>owned var x = unrestrictedVar (reference to unrestricted binding)</summary>
        UnrestrictedBinding,
        /// <summary>owned var x = obj.field (field access, not a unique value)</summary>
        FieldAccess,
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates valid owned binding scenarios: owned type + unique initializer.
    /// Covers constructor calls, moves from owned bindings, and owned-returning functions.
    /// </summary>
    private static Gen<ValidOwnedBindingScenario> GenValidOwnedBindingScenario()
    {
        return from typeName in Gen.Elements(OwnedTypeNames)
               from varName in Gen.Elements(VarNames)
               from valueType in Gen.Elements(ValueTypes)
               from initKind in Gen.Elements(
                   ValidInitializerKind.ConstructorCall,
                   ValidInitializerKind.MoveFromOwnedBinding,
                   ValidInitializerKind.OwnedReturningFunction)
               let source = BuildValidOwnedBindingSource(typeName, varName, valueType, initKind)
               let label = $"owned var {varName} : {typeName} via {initKind}"
               select new ValidOwnedBindingScenario(source, label);
    }

    private static string BuildValidOwnedBindingSource(
        string typeName, string varName, string valueType, ValidInitializerKind initKind)
    {
        var (setup, initializer) = initKind switch
        {
            ValidInitializerKind.ConstructorCall =>
                ("", $"new {typeName}()"),
            ValidInitializerKind.MoveFromOwnedBinding =>
                ($"owned var source = new {typeName}();\n            ", "source"),
            ValidInitializerKind.OwnedReturningFunction =>
                ("", $"CreateOwned()"),
            _ => throw new ArgumentOutOfRangeException(nameof(initKind))
        };

        var helperMethod = initKind == ValidInitializerKind.OwnedReturningFunction
            ? $"\n    public static owned {typeName} CreateOwned() {{\n        return new {typeName}();\n    }}"
            : "";

        return $$"""
            owned class {{typeName}} {
                {{valueType}} value;
            }
            class Program {
                public static void Main() {
                    {{setup}}owned var {{varName}} = {{initializer}};
                }{{helperMethod}}
            }
            """;
    }

    /// <summary>
    /// Generates owned binding scenarios with non-owned types.
    /// The non-owned type must have at least one non-owned reference-type field
    /// so that SatisfiesOwnedTypeFieldRestriction returns false.
    /// These should report MIST_OWN_TYPE_REQUIRES_OWNED.
    /// </summary>
    private static Gen<NonOwnedTypeScenario> GenNonOwnedTypeScenario()
    {
        return from typeName in Gen.Elements(NonOwnedTypeNames)
               from varName in Gen.Elements(VarNames)
               from refFieldType in Gen.Elements("Dependency", "Resource", "Connection")
               let source = $$"""
                   class {{refFieldType}} {
                   }
                   class {{typeName}} {
                       {{refFieldType}} dep;
                   }
                   class Program {
                       public static void Main() {
                           owned var {{varName}} = new {{typeName}}();
                       }
                   }
                   """
               select new NonOwnedTypeScenario(source,
                   $"owned var {varName} of non-owned type {typeName} (has non-owned ref field)");
    }

    /// <summary>
    /// Generates owned binding scenarios with invalid initializers.
    /// The type is owned, but the initializer does not yield unique ownership.
    /// These should report MIST_OWN_TYPE_REQUIRES_OWNED.
    /// </summary>
    private static Gen<InvalidInitializerScenario> GenInvalidInitializerScenario()
    {
        return from typeName in Gen.Elements(OwnedTypeNames)
               from varName in Gen.Elements(VarNames)
               from valueType in Gen.Elements(ValueTypes)
               from initKind in Gen.Elements(
                   InvalidInitializerKind.UnrestrictedBinding,
                   InvalidInitializerKind.FieldAccess)
               let source = BuildInvalidInitializerSource(typeName, varName, valueType, initKind)
               let label = $"owned var {varName} : {typeName} via invalid {initKind}"
               select new InvalidInitializerScenario(source, label);
    }

    private static string BuildInvalidInitializerSource(
        string typeName, string varName, string valueType, InvalidInitializerKind initKind)
    {
        return initKind switch
        {
            InvalidInitializerKind.UnrestrictedBinding =>
                $$"""
                owned class {{typeName}} {
                    {{valueType}} value;
                }
                class Program {
                    public static void Main() {
                        var source = new {{typeName}}();
                        owned var {{varName}} = source;
                    }
                }
                """,
            InvalidInitializerKind.FieldAccess =>
                $$"""
                owned class {{typeName}} {
                    {{valueType}} value;
                }
                owned class OuterContainer {
                    public owned {{typeName}} child;
                }
                class Program {
                    public static void Main() {
                        owned var w = new OuterContainer();
                        owned var {{varName}} = w.child;
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(initKind))
        };
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<ValidOwnedBindingScenario> ArbitraryValidOwnedBindingScenario() =>
        GenValidOwnedBindingScenario().ToArbitrary();

    public static Arbitrary<NonOwnedTypeScenario> ArbitraryNonOwnedTypeScenario() =>
        GenNonOwnedTypeScenario().ToArbitrary();

    public static Arbitrary<InvalidInitializerScenario> ArbitraryInvalidInitializerScenario() =>
        GenInvalidInitializerScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a: Valid owned bindings (owned type + unique initializer)
    // produce no MIST_OWN_TYPE_REQUIRES_OWNED diagnostics
    //
    // **Validates: Requirements 4.3, 4.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedBindingValidationPropertyTests) })]
    public bool ValidOwnedBinding_NoTypeRequiresOwnedDiagnostic(ValidOwnedBindingScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b: Owned binding with non-owned type produces
    // MIST_OWN_TYPE_REQUIRES_OWNED
    //
    // **Validates: Requirements 4.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedBindingValidationPropertyTests) })]
    public bool OwnedBindingWithNonOwnedType_EmitsTypeRequiresOwned(NonOwnedTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5c: Owned binding with invalid initializer produces
    // MIST_OWN_TYPE_REQUIRES_OWNED
    //
    // **Validates: Requirements 4.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedBindingValidationPropertyTests) })]
    public bool OwnedBindingWithInvalidInitializer_EmitsTypeRequiresOwned(InvalidInitializerScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_TYPE_REQUIRES_OWNED");
    }
}
