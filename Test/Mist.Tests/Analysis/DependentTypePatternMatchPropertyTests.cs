// Feature: dependent-types, Property 5: Open Dependent Type Pattern Rejection

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
/// Property-based tests verifying that pattern matching against open dependent types
/// produces the MIST_DT_0007 diagnostic.
///
/// **Validates: Requirements 9.3, 9.4, 15.7**
///
/// For any pattern match expression where the pattern type contains an unresolved
/// dependent type parameter (an open dependent type), the type checker SHALL emit
/// diagnostic MIST_DT_0007.
///
/// An "open dependent type" is one where the dependent argument is an unresolved
/// parameter (e.g., <c>FixedBuffer&lt;N&gt;</c> where <c>N</c> is a method-level
/// dependent type parameter). When the method body tries to pattern match using
/// the open parameter in the pattern position (e.g., <c>if (buf is FixedBuffer&lt;N&gt;)</c>),
/// the type checker should emit MIST_DT_0007 because the pattern type contains an
/// unresolved dependent type parameter.
///
/// This test uses the full compilation pipeline (lexer → parser → semantic model →
/// type checker) via a shared CompilerDriver instance to comply with the
/// test-process-spawning policy.
/// </summary>
public class DependentTypePatternMatchPropertyTests
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

    private static readonly string[] AllValueTypes =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "bool", "char"];

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage", "Channel", "Segment"];

    private static readonly string[] ParamNames =
        ["Size", "Count", "Capacity", "Length", "Width", "Height", "Depth", "Rank"];

    private static readonly string[] MethodParamNames =
        ["N", "M", "K", "P", "Q", "R", "X", "Y"];

    private static readonly string[] MethodNames =
        ["Process", "Handle", "Transform", "Execute", "Compute", "Analyze", "Validate", "Check"];

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario where a method with an open dependent type parameter
    /// attempts to pattern match using that open parameter in the pattern type.
    /// The pattern type contains an unresolved dependent type parameter (e.g.,
    /// <c>if (item is Container&lt;K&gt;)</c> where <c>K</c> is a method-level
    /// dependent type parameter), which should be rejected with MIST_DT_0007.
    /// </summary>
#pragma warning disable CA1034
    public record OpenPatternMatchScenario(
        string ClassName,
        string ClassParamName,
        string ValueType,
        string MethodName,
        string MethodParamName)
    {
        public override string ToString()
        {
            return $"class {ClassName}<{ClassParamName}:{ValueType}>, " +
            $"method {MethodName}<{MethodParamName}:{ValueType}> " +
            $"pattern matches {ClassName}<{MethodParamName}>";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<OpenPatternMatchScenario> GenOpenPatternMatchScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from classParamName in Gen.Elements(ParamNames)
               from valueType in Gen.Elements(AllValueTypes)
               from methodName in Gen.Elements(MethodNames)
               from methodParamName in Gen.Elements(MethodParamNames)
               select new OpenPatternMatchScenario(
                   className,
                   classParamName,
                   valueType,
                   methodName,
                   methodParamName);
    }

    public static Arbitrary<OpenPatternMatchScenario> ArbitraryOpenPatternMatchScenario()
    {
        return GenOpenPatternMatchScenario().ToArbitrary();
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 5: For any pattern match expression where the pattern type contains
    /// an unresolved dependent type parameter (an open dependent type), the type
    /// checker SHALL emit diagnostic MIST_DT_0007.
    ///
    /// This test generates Mist source code where:
    /// 1. A class has a dependent type parameter (e.g., <c>class Buffer&lt;Size:int&gt;</c>)
    /// 2. A method on another class has its own open dependent type parameter
    ///    (e.g., <c>void Process&lt;N:int&gt;(Buffer&lt;N&gt; buf)</c>)
    /// 3. The method body attempts to pattern match using the open parameter in the
    ///    pattern type (e.g., <c>if (buf is Buffer&lt;N&gt;)</c>)
    ///
    /// The compiler should reject this with MIST_DT_0007 because the pattern type
    /// <c>Buffer&lt;N&gt;</c> contains an unresolved dependent type parameter <c>N</c>.
    /// Pattern matching requires concrete dependent values.
    ///
    /// **Validates: Requirements 9.3, 9.4, 15.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DependentTypePatternMatchPropertyTests) })]
    public bool OpenDependentType_PatternMatch_EmitsMistDt0007(OpenPatternMatchScenario scenario)
    {
        // Build Mist source:
        //   class ClassName<ParamName:ValueType> { }
        //   class Processor {
        //       void MethodName<MethodParamName:ValueType>(ClassName<MethodParamName> item) {
        //           if (item is ClassName<MethodParamName>) { }  // ERROR: open type in pattern
        //       }
        //   }
        var source = $$"""
            class {{scenario.ClassName}}<{{scenario.ClassParamName}}:{{scenario.ValueType}}> {
            }
            class Processor {
                void {{scenario.MethodName}}<{{scenario.MethodParamName}}:{{scenario.ValueType}}>({{scenario.ClassName}}<{{scenario.MethodParamName}}> item) {
                    if (item is {{scenario.ClassName}}<{{scenario.MethodParamName}}>) {
                    }
                }
            }
            """;

        var diagnostics = CompileSource(source);

        // Verify the compiler emits MIST_DT_0007 for the open dependent type pattern match.
        var dt0007Diagnostics = diagnostics
            .Where(d => d.Id.Value is "MIST_DT_0007")
            .ToList();

        return dt0007Diagnostics.Count >= 1;
    }
}