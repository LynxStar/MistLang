// Feature: dependent-types, Property 3: Non-Constant Dependent Arguments Produce Diagnostics

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that non-constant expressions used as dependent
/// type arguments produce diagnostics that reject the program.
///
/// **Validates: Requirements 5.1, 5.3, 15.1, 15.6**
///
/// For any expression that is not a literal or const reference, using it as a
/// dependent type argument SHALL cause the compiler to emit a diagnostic rejecting
/// the non-constant argument. The compiler rejects these via MIST_DT_0002
/// (when the identifier resolves to a non-constant symbol in scope) or
/// MIST_UNDEFINED_TYPE_ARGUMENT (when the identifier cannot be resolved in the
/// type argument resolution scope, which is the case for local variables since
/// type arguments are resolved at file scope).
///
/// Since the grammar only allows specific token types in type argument positions
/// (literals, identifiers, type references), the non-constant cases that can actually
/// be parsed are limited to bare identifiers that resolve to local variables (not consts).
///
/// This test uses the full compilation pipeline (lexer → parser → semantic model →
/// type checker) via a shared CompilerDriver instance to comply with the
/// test-process-spawning policy.
/// </summary>
public class DependentTypeConstantValidationPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        var sourceFile = Path.Combine(tempDir, "test.mist");
        File.WriteAllText(sourceFile, source);

        try
        {
            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: [sourceFile],
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: tempDir,
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
        }
    }

    // ─── Allowed dependent parameter types ───────────────────────────────

    private static readonly string[] AllowedValueTypes =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "bool", "char"];

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private static readonly string[] ParamNames =
        ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Rank"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "n", "m", "k", "val", "num", "sz", "cnt"];

    // ─── Initializer values per type ─────────────────────────────────────

    private static string GetInitializerForType(string valueType) => valueType switch
    {
        "int" => "42",
        "uint" => "42",
        "long" => "42",
        "ulong" => "42",
        "short" => "42",
        "ushort" => "42",
        "byte" => "42",
        "sbyte" => "42",
        "bool" => "true",
        "char" => "'a'",
        _ => "0",
    };

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario where a non-constant local variable is used as a
    /// dependent type argument.
    /// </summary>
#pragma warning disable CA1034
    public record NonConstantArgScenario(
        string ClassName,
        string ParamName,
        string ValueType,
        string VarName)
    {
        public override string ToString() =>
            $"class {ClassName}<{ParamName}:{ValueType}> with var {VarName}";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<NonConstantArgScenario> GenNonConstantArgScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from paramName in Gen.Elements(ParamNames)
               from valueType in Gen.Elements(AllowedValueTypes)
               from varName in Gen.Elements(VarNames)
               select new NonConstantArgScenario(className, paramName, valueType, varName);
    }

    public static Arbitrary<NonConstantArgScenario> ArbitraryNonConstantArgScenario() =>
        GenNonConstantArgScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any expression that is not a literal or const reference,
    /// using it as a dependent type argument SHALL cause the compiler to emit
    /// a diagnostic rejecting the non-constant argument.
    ///
    /// This test generates Mist source code where a bare identifier in a dependent
    /// position resolves to a non-const local variable. The full compilation pipeline
    /// (lexer → parser → semantic model → type checker) is used to verify the diagnostic.
    ///
    /// The compiler rejects non-constant dependent arguments via MIST_DT_0002 (when
    /// the symbol is resolved but is not a compile-time constant) or
    /// MIST_UNDEFINED_TYPE_ARGUMENT (when the local variable is not visible in the
    /// type argument resolution scope). Both diagnostics correctly reject the program.
    ///
    /// **Validates: Requirements 5.1, 5.3, 15.1, 15.6**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypeConstantValidationPropertyTests) })]
    public bool NonConstantArg_EmitsMistDt0002(NonConstantArgScenario scenario)
    {
        // Build Mist source:
        //   class ClassName<ParamName:ValueType> { }
        //   class Program {
        //       public static void Main() {
        //           ValueType varName = initializer;
        //           var obj = new ClassName<varName>();
        //       }
        //   }
        var initializer = GetInitializerForType(scenario.ValueType);
        var source = $$"""
            class {{scenario.ClassName}}<{{scenario.ParamName}}:{{scenario.ValueType}}> {
            }
            class Program {
                public static void Main() {
                    {{scenario.ValueType}} {{scenario.VarName}} = {{initializer}};
                    var obj = new {{scenario.ClassName}}<{{scenario.VarName}}>();
                }
            }
            """;

        var diagnostics = CompileSource(source);

        // Verify the compiler rejects the non-constant dependent argument.
        // MIST_DT_0002: emitted when the identifier resolves to a non-constant symbol.
        // MIST_UNDEFINED_TYPE_ARGUMENT: emitted when the local variable is not visible
        // in the type argument resolution scope (file scope doesn't see method locals).
        // Both correctly reject the program per Requirements 5.1, 5.3.
        var rejectionDiagnostics = diagnostics
            .Where(d => d.Id.Value is "MIST_DT_0002" or "MIST_UNDEFINED_TYPE_ARGUMENT")
            .ToList();

        return rejectionDiagnostics.Count >= 1
            && rejectionDiagnostics.Any(d => d.Message.Contains(scenario.VarName));
    }
}