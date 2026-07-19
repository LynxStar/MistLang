// Feature: variadics, task 17.8: property test for pack-bearing declaration kinds (classic
// pipeline).
//
// Property 18: Pack-bearing declarations accepted on all kinds.
// **Validates: Requirements 12.1, 12.4, 12.5**
//
// Asserts:
//   - A pack on class, struct, abstract, type alias, and method is accepted with no
//     MIST_VAR_0001 (one pack per list) (Req 12.1).
//   - Multiple packs on a single list emits MIST_VAR_0001 (Req 12.4, 12.5).
//
// Drives end-to-end via VariadicsCompilerFixture.Compile (the shared stateless
// CompilerDriver wiring SemanticModelBuilder + TypeCheckerAnalyzer + CSharpTranspilerBackend).
// All tests run entirely in-process with ZERO external process spawns.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Which declaration kind carries the pack in the generated scenario.
/// </summary>
public enum PackDeclarationKind
{
    Class,
    Struct,
    Abstract,
    TypeAlias,
    Method,
}

/// <summary>
/// A generated scenario for testing pack-bearing declaration kinds: a complete, parseable
/// Mist declaration of a specific kind carrying either one pack (accepted) or two packs
/// (MIST_VAR_0001 violation).
/// </summary>
public sealed record PackDeclarationKindScenario(
    PackDeclarationKind Kind,
    int PackCount,
    string Source)
{
    /// <summary>Whether this scenario should be accepted (exactly one pack, no MIST_VAR_0001).</summary>
    public bool ShouldBeAccepted => PackCount == 1;

    public override string ToString()
    {
        return $"{Kind} with {PackCount} pack(s), expect={(ShouldBeAccepted ? "accepted" : "MIST_VAR_0001")}";
    }
}

/// <summary>
/// Property test for <b>Property 18 (Pack-bearing declarations accepted on all kinds)</b>
/// over the classic pipeline. Generates pack type-parameter lists on class, struct, abstract
/// type, type alias, and method; asserts the declaration is accepted with no MIST_VAR_0001
/// for a single pack, and that two packs emits MIST_VAR_0001. MaxTest = 150.
/// <b>Validates: Requirements 12.1, 12.4, 12.5.</b>
/// </summary>
public sealed class VariadicsPackBearingDeclarationKindsPropertyTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 18: single pack accepted on all kinds; multiple packs rejected.
    // **Validates: Requirements 12.1, 12.4, 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsPackBearingDeclarationKindsPropertyTests) }, MaxTest = 150)]
    public bool PackBearingDeclaration_AcceptedOnAllKinds_OrRejectedForMultiplePacks(
        PackDeclarationKindScenario scenario)
    {
        var diagnosticIds = VariadicsCompilerFixture.CompileDiagnosticIds(scenario.Source);

        var hasMultiplePacksDiag = diagnosticIds.Any(id => id == MultipleParamsPacksId);

        if(scenario.ShouldBeAccepted)
        {
            Assert.False(hasMultiplePacksDiag,
                $"Expected no {MultipleParamsPacksId} for {scenario.Kind} with 1 pack.\n" +
                $"Source: {scenario.Source}\n" +
                $"Diagnostics: {string.Join(", ", diagnosticIds)}");
        }
        else
        {
            Assert.True(hasMultiplePacksDiag,
                $"Expected {MultipleParamsPacksId} for {scenario.Kind} with {scenario.PackCount} packs.\n" +
                $"Source: {scenario.Source}");
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    public static Arbitrary<PackDeclarationKindScenario> PackDeclarationKindScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<PackDeclarationKindScenario> GenScenario()
    {
        // Generate scenarios across all declaration kinds, split between 1 pack (accepted)
        // and 2+ packs (rejected). Weighted ~60/40 toward accepted to ensure all kinds get
        // adequate positive coverage.
        return Gen.OneOf(
            GenSinglePackScenario(),
            GenSinglePackScenario(),
            GenSinglePackScenario(),
            GenMultiplePackScenario(),
            GenMultiplePackScenario());
    }

    /// <summary>One pack on a randomly-chosen declaration kind -> accepted.</summary>
    private static Gen<PackDeclarationKindScenario> GenSinglePackScenario()
    {
        return
            from kind in Gen.Elements(
                PackDeclarationKind.Class,
                PackDeclarationKind.Struct,
                PackDeclarationKind.Abstract,
                PackDeclarationKind.TypeAlias,
                PackDeclarationKind.Method)
            from hasExtraScalar in Gen.Elements(true, false)
            let source = BuildSinglePackSource(kind, hasExtraScalar)
            select new PackDeclarationKindScenario(kind, 1, source);
    }

    /// <summary>Two packs on a randomly-chosen declaration kind -> MIST_VAR_0001.</summary>
    private static Gen<PackDeclarationKindScenario> GenMultiplePackScenario()
    {
        return
            from kind in Gen.Elements(
                PackDeclarationKind.Class,
                PackDeclarationKind.Struct,
                PackDeclarationKind.Abstract,
                PackDeclarationKind.TypeAlias,
                PackDeclarationKind.Method)
            let source = BuildMultiplePackSource(kind)
            select new PackDeclarationKindScenario(kind, 2, source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source builders
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildSinglePackSource(PackDeclarationKind kind, bool hasExtraScalar)
    {
        var typeParams = hasExtraScalar ? "T, params TArgs" : "params TArgs";

        return kind switch
        {
            PackDeclarationKind.Class =>
                $"public class TestDecl<{typeParams}> {{ }}",
            PackDeclarationKind.Struct =>
                $"public struct TestDecl<{typeParams}> {{ }}",
            PackDeclarationKind.Abstract =>
                $"abstract TestDecl<{typeParams}> {{ int Count(); }}",
            PackDeclarationKind.TypeAlias =>
                $"type TestDecl<{typeParams}> = int;",
            PackDeclarationKind.Method =>
                $"public class Host {{ public int TestMethod<{typeParams}>() {{ return 0; }} }}",
            _ => throw new InvalidOperationException($"Unhandled kind: {kind}"),
        };
    }

    private static string BuildMultiplePackSource(PackDeclarationKind kind)
    {
        const string typeParams = "params A, params B";

        return kind switch
        {
            PackDeclarationKind.Class =>
                $"public class TestDecl<{typeParams}> {{ }}",
            PackDeclarationKind.Struct =>
                $"public struct TestDecl<{typeParams}> {{ }}",
            PackDeclarationKind.Abstract =>
                $"abstract TestDecl<{typeParams}> {{ int Count(); }}",
            PackDeclarationKind.TypeAlias =>
                $"type TestDecl<{typeParams}> = int;",
            PackDeclarationKind.Method =>
                $"public class Host {{ public int TestMethod<{typeParams}>() {{ return 0; }} }}",
            _ => throw new InvalidOperationException($"Unhandled kind: {kind}"),
        };
    }
}