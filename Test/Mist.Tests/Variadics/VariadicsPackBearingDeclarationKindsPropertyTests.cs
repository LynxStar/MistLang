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
/// Boundary tests for <b>Property 18 (Pack-bearing declarations accepted on all kinds)</b>
/// over the classic pipeline. The domain is a small, fully enumerable matrix -- five
/// declaration kinds (class, struct, abstract, type alias, method) crossed with pack count
/// (one pack, optionally preceded by a scalar, is accepted; two packs emits MIST_VAR_0001) --
/// so it is covered exhaustively by <see cref="TheoryAttribute"/> cases rather than by
/// sampling the same 15 sources 150 times.
/// <b>Validates: Requirements 12.1, 12.4, 12.5.</b>
/// </summary>
public sealed class VariadicsPackBearingDeclarationKindsPropertyTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 18: single pack accepted on all kinds; multiple packs rejected.
    // **Validates: Requirements 12.1, 12.4, 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    // A single pack -- on any declaration kind, with or without a leading scalar type
    // parameter -- must be accepted with no MIST_VAR_0001 (one-pack-per-list) diagnostic.
    [Theory]
    [InlineData(PackDeclarationKind.Class, false)]
    [InlineData(PackDeclarationKind.Class, true)]
    [InlineData(PackDeclarationKind.Struct, false)]
    [InlineData(PackDeclarationKind.Struct, true)]
    [InlineData(PackDeclarationKind.Abstract, false)]
    [InlineData(PackDeclarationKind.Abstract, true)]
    [InlineData(PackDeclarationKind.TypeAlias, false)]
    [InlineData(PackDeclarationKind.TypeAlias, true)]
    [InlineData(PackDeclarationKind.Method, false)]
    [InlineData(PackDeclarationKind.Method, true)]
    public void SinglePack_AcceptedOnAllKinds(PackDeclarationKind kind, bool hasExtraScalar)
    {
        var source = BuildSinglePackSource(kind, hasExtraScalar);
        var diagnosticIds = VariadicsCompilerFixture.CompileDiagnosticIds(source);

        Assert.DoesNotContain(MultipleParamsPacksId, diagnosticIds);
    }

    // Two packs on a single list -- on any declaration kind -- must emit MIST_VAR_0001.
    [Theory]
    [InlineData(PackDeclarationKind.Class)]
    [InlineData(PackDeclarationKind.Struct)]
    [InlineData(PackDeclarationKind.Abstract)]
    [InlineData(PackDeclarationKind.TypeAlias)]
    [InlineData(PackDeclarationKind.Method)]
    public void MultiplePacks_RejectedOnAllKinds(PackDeclarationKind kind)
    {
        var source = BuildMultiplePackSource(kind);
        var diagnosticIds = VariadicsCompilerFixture.CompileDiagnosticIds(source);

        Assert.Contains(MultipleParamsPacksId, diagnosticIds);
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