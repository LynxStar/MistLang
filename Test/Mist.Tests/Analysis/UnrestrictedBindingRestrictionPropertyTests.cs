// Feature: deep-ownership-types, Property 9: Unrestricted binding restrictions

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
/// Property-based tests for unrestricted binding restrictions (Pass 4).
///
/// Property 9: Unrestricted Binding Restrictions
/// For any unrestricted binding holding an owned type instance, the type checker SHALL
/// reject taking a 'borrowed' or 'frozen' reference to any field in the object graph,
/// SHALL permit taking a 'readonly ref' to direct (top-level) fields only, and SHALL
/// reject passing the binding to a 'borrowed' or 'frozen' parameter.
///
/// **Validates: Requirements 7.2, 7.3, 7.4, 12.5, 12.6**
/// </summary>
public class UnrestrictedBindingRestrictionPropertyTests
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
    /// Scenario where an unrestricted binding is lent as 'borrowed' — should be rejected.
    /// </summary>
    public sealed record UnrestrictedLentBorrowedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an unrestricted binding is lent as 'frozen' — should be rejected.
    /// </summary>
    public sealed record UnrestrictedLentFrozenScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an unrestricted binding is lent as 'readonly ref' — should be permitted.
    /// </summary>
    public sealed record UnrestrictedLentReadonlyRefScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an unrestricted binding is passed to a 'borrowed' parameter — should be rejected.
    /// </summary>
    public sealed record UnrestrictedPassedBorrowedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an unrestricted binding is passed to a 'frozen' parameter — should be rejected.
    /// </summary>
    public sealed record UnrestrictedPassedFrozenScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an unrestricted binding is passed to a 'readonly ref' parameter — should be permitted.
    /// </summary>
    public sealed record UnrestrictedPassedReadonlyRefScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage"];

    private static readonly string[] FieldNames =
        ["data", "value", "count", "size", "length", "index"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "item", "src"];

    private static readonly string[] RefVarNames =
        ["r", "view", "ref1", "handle", "ptr", "alias"];

    private static readonly string[] MethodNames =
        ["Process", "Handle", "Accept", "Consume", "Transform", "Inspect"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where an unrestricted binding is lent as 'borrowed'.
    /// This should be rejected with MIST_OWN_UNRESTRICTED_TO_REF.
    /// </summary>
    private static Gen<UnrestrictedLentBorrowedScenario> GenUnrestrictedLentBorrowed()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refName in Gen.Elements(RefVarNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           borrowed {{className}} {{refName}} = {{varName}};
                       }
                   }
                   """
               select new UnrestrictedLentBorrowedScenario(source,
                   $"unrestricted {varName}:{className} lent as borrowed {refName}");
    }

    /// <summary>
    /// Generates scenarios where an unrestricted binding is lent as 'frozen'.
    /// This should be rejected with MIST_OWN_UNRESTRICTED_TO_REF.
    /// </summary>
    private static Gen<UnrestrictedLentFrozenScenario> GenUnrestrictedLentFrozen()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refName in Gen.Elements(RefVarNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           frozen {{className}} {{refName}} = {{varName}};
                       }
                   }
                   """
               select new UnrestrictedLentFrozenScenario(source,
                   $"unrestricted {varName}:{className} lent as frozen {refName}");
    }

    /// <summary>
    /// Generates scenarios where an unrestricted binding is lent as 'readonly ref'.
    /// This should be permitted (no error) — readonly ref to the binding itself is allowed.
    /// </summary>
    private static Gen<UnrestrictedLentReadonlyRefScenario> GenUnrestrictedLentReadonlyRef()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refName in Gen.Elements(RefVarNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           readonly ref {{className}} {{refName}} = {{varName}};
                       }
                   }
                   """
               select new UnrestrictedLentReadonlyRefScenario(source,
                   $"unrestricted {varName}:{className} lent as readonly ref {refName}");
    }

    /// <summary>
    /// Generates scenarios where an unrestricted binding is passed to a 'borrowed' parameter.
    /// This should be rejected with MIST_OWN_UNRESTRICTED_TO_REF.
    /// </summary>
    private static Gen<UnrestrictedPassedBorrowedScenario> GenUnrestrictedPassedBorrowed()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void {{methodName}}(borrowed {{className}} param) {
                       }
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           {{methodName}}({{varName}});
                       }
                   }
                   """
               select new UnrestrictedPassedBorrowedScenario(source,
                   $"unrestricted {varName}:{className} passed to borrowed param in {methodName}");
    }

    /// <summary>
    /// Generates scenarios where an unrestricted binding is passed to a 'frozen' parameter.
    /// This should be rejected with MIST_OWN_UNRESTRICTED_TO_REF.
    /// </summary>
    private static Gen<UnrestrictedPassedFrozenScenario> GenUnrestrictedPassedFrozen()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void {{methodName}}(frozen {{className}} param) {
                       }
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           {{methodName}}({{varName}});
                       }
                   }
                   """
               select new UnrestrictedPassedFrozenScenario(source,
                   $"unrestricted {varName}:{className} passed to frozen param in {methodName}");
    }

    /// <summary>
    /// Generates scenarios where an unrestricted binding is passed to a 'readonly ref' parameter.
    /// This should be permitted (no error).
    /// </summary>
    private static Gen<UnrestrictedPassedReadonlyRefScenario> GenUnrestrictedPassedReadonlyRef()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               from valueType in Gen.Elements(ValueTypes)
               let source = $$"""
                   owned class {{className}} {
                       public {{valueType}} {{fieldName}};
                   }
                   class Program {
                       public static void {{methodName}}(readonly ref {{className}} param) {
                       }
                       public static void Main() {
                           var {{varName}} = new {{className}}();
                           {{methodName}}({{varName}});
                       }
                   }
                   """
               select new UnrestrictedPassedReadonlyRefScenario(source,
                   $"unrestricted {varName}:{className} passed to readonly ref param in {methodName}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<UnrestrictedLentBorrowedScenario> ArbitraryUnrestrictedLentBorrowedScenario() =>
        GenUnrestrictedLentBorrowed().ToArbitrary();

    public static Arbitrary<UnrestrictedLentFrozenScenario> ArbitraryUnrestrictedLentFrozenScenario() =>
        GenUnrestrictedLentFrozen().ToArbitrary();

    public static Arbitrary<UnrestrictedLentReadonlyRefScenario> ArbitraryUnrestrictedLentReadonlyRefScenario() =>
        GenUnrestrictedLentReadonlyRef().ToArbitrary();

    public static Arbitrary<UnrestrictedPassedBorrowedScenario> ArbitraryUnrestrictedPassedBorrowedScenario() =>
        GenUnrestrictedPassedBorrowed().ToArbitrary();

    public static Arbitrary<UnrestrictedPassedFrozenScenario> ArbitraryUnrestrictedPassedFrozenScenario() =>
        GenUnrestrictedPassedFrozen().ToArbitrary();

    public static Arbitrary<UnrestrictedPassedReadonlyRefScenario> ArbitraryUnrestrictedPassedReadonlyRefScenario() =>
        GenUnrestrictedPassedReadonlyRef().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9a: Unrestricted binding lent as 'borrowed' — rejected
    //
    // **Validates: Requirements 7.2, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedLentAsBorrowed_EmitsUnrestrictedToRef(UnrestrictedLentBorrowedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: Unrestricted binding lent as 'frozen' — rejected
    //
    // **Validates: Requirements 7.3, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedLentAsFrozen_EmitsUnrestrictedToRef(UnrestrictedLentFrozenScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9c: Unrestricted binding lent as 'readonly ref' — permitted
    //
    // **Validates: Requirements 7.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedLentAsReadonlyRef_NoUnrestrictedToRefDiagnostic(UnrestrictedLentReadonlyRefScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9d: Unrestricted binding passed to 'borrowed' parameter — rejected
    //
    // **Validates: Requirements 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedPassedToBorrowed_EmitsUnrestrictedToRef(UnrestrictedPassedBorrowedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9e: Unrestricted binding passed to 'frozen' parameter — rejected
    //
    // **Validates: Requirements 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedPassedToFrozen_EmitsUnrestrictedToRef(UnrestrictedPassedFrozenScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9f: Unrestricted binding passed to 'readonly ref' parameter — permitted
    //
    // **Validates: Requirements 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(UnrestrictedBindingRestrictionPropertyTests) })]
    public bool UnrestrictedPassedToReadonlyRef_NoUnrestrictedToRefDiagnostic(UnrestrictedPassedReadonlyRefScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");
    }
}