// Feature: ownership-system, Property 8: Ownership signature matching

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
/// Property-based tests for ownership signature matching.
///
/// Property 8: Ownership signature matching
/// For any interface method with ownership specifiers on parameters or return types,
/// an implementing method SHALL match those specifiers exactly. Any mismatch SHALL
/// produce a diagnostic error. The same rule applies to structural trait matching.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
/// </summary>
public class OwnershipSignaturePropertyTests
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
    /// Scenario where an interface method and its implementation have matching
    /// ownership specifiers (should produce no MIST_OWN_SIGNATURE_MISMATCH).
    /// </summary>
    public sealed record MatchingSignatureScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an interface method and its implementation have mismatching
    /// ownership specifiers on a parameter (should produce MIST_OWN_SIGNATURE_MISMATCH).
    /// </summary>
    public sealed record MismatchedParamScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an interface method and its implementation have mismatching
    /// ownership specifiers on the return type (should produce MIST_OWN_SIGNATURE_MISMATCH).
    /// </summary>
    public sealed record MismatchedReturnScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where a trait method and its structural implementation have mismatching
    /// ownership specifiers (should produce MIST_OWN_SIGNATURE_MISMATCH).
    /// </summary>
    public sealed record TraitMismatchScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where a method has a non-owned specifier on its return type
    /// (should produce MIST_OWN_INVALID_RETURN_SPEC).
    /// </summary>
    public sealed record InvalidReturnSpecScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNames =
        ["IProcessor", "IHandler", "IService", "IWorker", "IManager", "IProvider"];

    private static readonly string[] ClassNames =
        ["Processor", "Handler", "Service", "Worker", "Manager", "Provider"];

    private static readonly string[] TraitNames =
        ["Processable", "Handleable", "Serviceable", "Workable", "Manageable", "Providable"];

    private static readonly string[] MethodNames =
        ["Execute", "Process", "Handle", "Transform", "Compute", "Evaluate"];

    private static readonly string[] ParamNames =
        ["input", "data", "item", "value", "source", "target"];

    private static readonly string[] ParamTypeNames =
        ["Widget", "Payload", "Resource", "Entity", "Record", "Element"];

    /// <summary>
    /// Ownership specifiers that are valid on parameters (all tiers).
    /// Note: "readonly ref" is excluded because the grammar change (ref-parameters feature)
    /// now parses "readonly ref" in parameter position as modifier=READONLY + refModifier=REF,
    /// making it a ref kind annotation rather than an ownership specifier.
    /// </summary>
    private static readonly (string Specifier, string Label)[] ParamSpecifiers =
    [
        ("owned ", "owned"),
        ("borrowed ", "borrowed"),
        ("frozen ", "frozen"),
        ("", "unrestricted"),
    ];

    /// <summary>
    /// Ownership specifiers valid on return types (only owned or unrestricted).
    /// </summary>
    private static readonly (string Specifier, string Label)[] ReturnSpecifiers =
    [
        ("owned ", "owned"),
        ("", "unrestricted"),
    ];

    /// <summary>
    /// Non-owned specifiers that are invalid on return types.
    /// </summary>
    private static readonly (string Specifier, string Label)[] InvalidReturnSpecs =
    [
        ("borrowed ", "borrowed"),
        ("readonly ref ", "readonly ref"),
        ("frozen ", "frozen"),
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates an interface/implementation pair where the ownership specifiers
    /// on parameters match exactly (should compile without MIST_OWN_SIGNATURE_MISMATCH).
    /// </summary>
    private static Gen<MatchingSignatureScenario> GenMatchingSignatureScenario()
    {
        return from ifaceName in Gen.Elements(InterfaceNames)
               from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(ParamTypeNames)
               from specIdx in Gen.Choose(0, ParamSpecifiers.Length - 1)
               from retIdx in Gen.Choose(0, ReturnSpecifiers.Length - 1)
               let paramSpec = ParamSpecifiers[specIdx]
               let retSpec = ReturnSpecifiers[retIdx]
               let source = $$"""
                   class {{paramType}} {
                   }
                   interface {{ifaceName}} {
                       {{retSpec.Specifier}}{{paramType}} {{methodName}}({{paramSpec.Specifier}}{{paramType}} {{paramName}});
                   }
                   class {{className}} : {{ifaceName}} {
                       public {{retSpec.Specifier}}{{paramType}} {{methodName}}({{paramSpec.Specifier}}{{paramType}} {{paramName}}) {
                           return new {{paramType}}();
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new MatchingSignatureScenario(source,
                   $"matching: {ifaceName}.{methodName}({paramSpec.Label} {paramType}) -> {retSpec.Label} {paramType}, impl {className}");
    }

    /// <summary>
    /// Generates an interface/implementation pair where the ownership specifier
    /// on a parameter differs between interface and implementation.
    /// </summary>
    private static Gen<MismatchedParamScenario> GenMismatchedParamScenario()
    {
        return from ifaceName in Gen.Elements(InterfaceNames)
               from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(ParamTypeNames)
               from ifaceSpecIdx in Gen.Choose(0, ParamSpecifiers.Length - 1)
               from implSpecIdx in Gen.Choose(0, ParamSpecifiers.Length - 1)
               where ifaceSpecIdx != implSpecIdx
               let ifaceSpec = ParamSpecifiers[ifaceSpecIdx]
               let implSpec = ParamSpecifiers[implSpecIdx]
               let source = $$"""
                   class {{paramType}} {
                   }
                   interface {{ifaceName}} {
                       void {{methodName}}({{ifaceSpec.Specifier}}{{paramType}} {{paramName}});
                   }
                   class {{className}} : {{ifaceName}} {
                       public void {{methodName}}({{implSpec.Specifier}}{{paramType}} {{paramName}}) {
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new MismatchedParamScenario(source,
                   $"mismatch param: {ifaceName}.{methodName}({ifaceSpec.Label}) vs {className}.{methodName}({implSpec.Label})");
    }

    /// <summary>
    /// Generates an interface/implementation pair where the ownership specifier
    /// on the return type differs between interface and implementation.
    /// </summary>
    private static Gen<MismatchedReturnScenario> GenMismatchedReturnScenario()
    {
        return from ifaceName in Gen.Elements(InterfaceNames)
               from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramType in Gen.Elements(ParamTypeNames)
               from ifaceRetIdx in Gen.Choose(0, ReturnSpecifiers.Length - 1)
               from implRetIdx in Gen.Choose(0, ReturnSpecifiers.Length - 1)
               where ifaceRetIdx != implRetIdx
               let ifaceRet = ReturnSpecifiers[ifaceRetIdx]
               let implRet = ReturnSpecifiers[implRetIdx]
               let source = $$"""
                   class {{paramType}} {
                   }
                   interface {{ifaceName}} {
                       {{ifaceRet.Specifier}}{{paramType}} {{methodName}}();
                   }
                   class {{className}} : {{ifaceName}} {
                       public {{implRet.Specifier}}{{paramType}} {{methodName}}() {
                           return new {{paramType}}();
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new MismatchedReturnScenario(source,
                   $"mismatch return: {ifaceName}.{methodName}() -> {ifaceRet.Label} vs {className}.{methodName}() -> {implRet.Label}");
    }

    /// <summary>
    /// Generates a trait/class pair where the ownership specifier on a parameter
    /// differs between the trait method and the structurally matching method.
    /// </summary>
    private static Gen<TraitMismatchScenario> GenTraitMismatchScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               from paramType in Gen.Elements(ParamTypeNames)
               from traitSpecIdx in Gen.Choose(0, ParamSpecifiers.Length - 1)
               from implSpecIdx in Gen.Choose(0, ParamSpecifiers.Length - 1)
               where traitSpecIdx != implSpecIdx
               let traitSpec = ParamSpecifiers[traitSpecIdx]
               let implSpec = ParamSpecifiers[implSpecIdx]
               let source = $$"""
                   class {{paramType}} {
                   }
                   trait {{traitName}} {
                       void {{methodName}}({{traitSpec.Specifier}}{{paramType}} {{paramName}});
                   }
                   class {{className}} {
                       public void {{methodName}}({{implSpec.Specifier}}{{paramType}} {{paramName}}) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           {{traitName}} t = new {{className}}();
                       }
                   }
                   """
               select new TraitMismatchScenario(source,
                   $"trait mismatch: {traitName}.{methodName}({traitSpec.Label}) vs {className}.{methodName}({implSpec.Label})");
    }

    /// <summary>
    /// Generates a method with a non-owned specifier on the return type
    /// (ref, readonly ref, or frozen ref — all invalid on return types).
    /// </summary>
    private static Gen<InvalidReturnSpecScenario> GenInvalidReturnSpecScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ParamTypeNames)
               from specIdx in Gen.Choose(0, InvalidReturnSpecs.Length - 1)
               let spec = InvalidReturnSpecs[specIdx]
               let source = $$"""
                   class {{returnType}} {
                   }
                   class {{className}} {
                       public {{spec.Specifier}}{{returnType}} {{methodName}}() {
                           return new {{returnType}}();
                       }
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new InvalidReturnSpecScenario(source,
                   $"invalid return spec: {className}.{methodName}() -> {spec.Label} {returnType}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<MatchingSignatureScenario> ArbitraryMatchingSignatureScenario() =>
        GenMatchingSignatureScenario().ToArbitrary();

    public static Arbitrary<MismatchedParamScenario> ArbitraryMismatchedParamScenario() =>
        GenMismatchedParamScenario().ToArbitrary();

    public static Arbitrary<MismatchedReturnScenario> ArbitraryMismatchedReturnScenario() =>
        GenMismatchedReturnScenario().ToArbitrary();

    public static Arbitrary<TraitMismatchScenario> ArbitraryTraitMismatchScenario() =>
        GenTraitMismatchScenario().ToArbitrary();

    public static Arbitrary<InvalidReturnSpecScenario> ArbitraryInvalidReturnSpecScenario() =>
        GenInvalidReturnSpecScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Matching ownership specifiers on interface implementations
    // produce no MIST_OWN_SIGNATURE_MISMATCH
    // **Validates: Requirements 9.1, 9.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipSignaturePropertyTests) })]
    public bool MatchingSignatures_NoSignatureMismatch(MatchingSignatureScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_SIGNATURE_MISMATCH");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: Mismatched ownership specifiers on interface parameters
    // produce MIST_OWN_SIGNATURE_MISMATCH
    // **Validates: Requirements 9.1, 9.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipSignaturePropertyTests) })]
    public bool MismatchedParamSpecifiers_EmitsSignatureMismatch(MismatchedParamScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_SIGNATURE_MISMATCH");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8c: Mismatched ownership specifiers on interface return types
    // produce MIST_OWN_SIGNATURE_MISMATCH
    // **Validates: Requirements 9.2, 9.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipSignaturePropertyTests) })]
    public bool MismatchedReturnSpecifiers_EmitsSignatureMismatch(MismatchedReturnScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_SIGNATURE_MISMATCH");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8d: Mismatched ownership specifiers on trait structural matching
    // produce MIST_OWN_SIGNATURE_MISMATCH
    // **Validates: Requirements 9.3, 9.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipSignaturePropertyTests) })]
    public bool TraitMismatchedSpecifiers_EmitsSignatureMismatch(TraitMismatchScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_SIGNATURE_MISMATCH");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8e: Non-owned specifiers on return types produce
    // MIST_OWN_INVALID_RETURN_SPEC
    // **Validates: Requirement 2.5 (only owned is valid on return types)**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipSignaturePropertyTests) })]
    public bool NonOwnedReturnSpec_EmitsInvalidReturnSpec(InvalidReturnSpecScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_INVALID_RETURN_SPEC");
    }
}
