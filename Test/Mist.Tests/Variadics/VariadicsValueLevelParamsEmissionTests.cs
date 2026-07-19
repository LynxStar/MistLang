// Feature: variadics - Unit test for C# emitter value-level params output (Task 19.2)
// **Validates: Requirement 13.6**
//
// Verifies that the CSharp transpiler backend emits a Value_Level_Params parameter
// (`params T[] xs`) as a C# `params T[]` array parameter in the method declaration.
// Uses in-memory compilation via the shared VariadicsCompilerFixture (ZERO external
// process spawns: no dotnet run, no compiled DLL).

using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Verifies that the C# transpiler emits the <c>params</c> keyword for value-level
/// params parameters (<c>params int[] xs</c>) in method declarations (Requirement 13.6).
/// </summary>
public sealed class VariadicsValueLevelParamsEmissionTests
{
    /// <summary>
    /// A method with <c>params int[] xs</c> emits with the <c>params</c> keyword in C#.
    /// </summary>
    [Fact]
    public void ValueLevelParams_EmitsParamsKeyword_InMethodDeclaration()
    {
        const string source =
            "public class Demo\n"
            + "{\n"
            + "    public static void SumAll(params int[] xs) { }\n"
            + "}";

        var result = VariadicsCompilerFixture.Compile(source);
        Assert.True(result.Succeeded, $"Compilation failed: {FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var emitted = CombineEmittedFiles(result.EmittedFiles!);

        // The emitted C# must contain the `params int[]` parameter declaration.
        Assert.Contains("params int[] xs", emitted);
    }

    /// <summary>
    /// A plain array parameter (no <c>params</c>) does NOT emit the <c>params</c> keyword.
    /// </summary>
    [Fact]
    public void PlainArrayParameter_DoesNotEmitParamsKeyword()
    {
        const string source =
            "public class Demo\n"
            + "{\n"
            + "    public static void TakeArray(int[] items) { }\n"
            + "}";

        var result = VariadicsCompilerFixture.Compile(source);
        Assert.True(result.Succeeded, $"Compilation failed: {FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var emitted = CombineEmittedFiles(result.EmittedFiles!);

        // The emitted C# must NOT contain `params` before the array parameter.
        Assert.DoesNotContain("params int[] items", emitted);
        // But the parameter itself must still be emitted.
        Assert.Contains("int[] items", emitted);
    }

    /// <summary>
    /// A mixed parameter list with a fixed parameter and trailing <c>params int[] rest</c>
    /// emits correctly with the <c>params</c> keyword only on the trailing parameter.
    /// </summary>
    [Fact]
    public void MixedParameterList_EmitsParamsOnlyOnTrailingParam()
    {
        const string source =
            "public class Demo\n"
            + "{\n"
            + "    public static void Mixed(int first, params int[] rest) { }\n"
            + "}";

        var result = VariadicsCompilerFixture.Compile(source);
        Assert.True(result.Succeeded, $"Compilation failed: {FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var emitted = CombineEmittedFiles(result.EmittedFiles!);

        // The `first` parameter must NOT have `params`.
        Assert.Contains("int first", emitted);
        Assert.DoesNotContain("params int first", emitted);

        // The trailing `rest` parameter must have `params`.
        Assert.Contains("params int[] rest", emitted);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CombineEmittedFiles(IReadOnlyDictionary<string, string> files)
    {
        return string.Join("\n", files.Values);
    }

    private static string FormatDiagnostics(Mist.Driver.Contracts.CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
    }
}