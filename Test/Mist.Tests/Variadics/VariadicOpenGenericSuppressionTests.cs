// Feature: variadics - Task 27.1 (monomorphize variadic type declarations and
// suppress open-generic emission).
// **Validates: Requirements 15.1, 16.5, 22.1**
//
// The C# backend must SUPPRESS the open-generic form of a variadic type declaration
// (e.g. `class Bundle<TArgs>`, which is invalid C# for a pack) and let the
// VariadicMonomorphizer emit the sole per-arity monomorphized definitions
// (e.g. `class Bundle<T1, T2>`). Before task 27.1 both forms were emitted, producing
// a duplicate/invalid definition of the type.
//
// ZERO external process spawns: in-process compilation via VariadicsCompilerFixture
// with in-memory emit (no dotnet run, no compiled DLL).

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Verifies that emitting a variadic type declaration produces per-arity monomorphized
/// definitions and does NOT emit a conflicting open-generic form (Requirements 15.1,
/// 16.5, 22.1).
/// </summary>
public sealed class VariadicOpenGenericSuppressionTests
{
    /// <summary>
    /// A variadic class used at a concrete arity emits the monomorphized
    /// <c>class Bundle&lt;T1, T2&gt;</c> and suppresses the open-generic
    /// <c>class Bundle&lt;TArgs&gt;</c>.
    /// </summary>
    [Fact]
    public void VariadicClass_EmitsMonomorphizedDefinition_AndSuppressesOpenGeneric()
    {
        const string source = """
            namespace SuppressTest;

            public class Bundle<params TArgs>
            {
            }

            public class Consumer
            {
                public static void Use(Bundle<int, string> b)
                {
                }
            }
            """;

        var combined = CompileAndCombine(source);

        // The sole definition is the monomorphized per-arity form.
        Assert.Contains("class Bundle<T1, T2>", combined);
        // The broken open-generic form must NOT be emitted.
        Assert.DoesNotContain("class Bundle<TArgs>", combined);
        // A pack type parameter must never leak into an emitted C# parameter list.
        Assert.DoesNotContain("params TArgs", combined);
    }

    /// <summary>
    /// A variadic struct used at a concrete arity emits the monomorphized
    /// <c>struct Pair&lt;T1, T2&gt;</c> and suppresses the open-generic form.
    /// </summary>
    [Fact]
    public void VariadicStruct_EmitsMonomorphizedDefinition_AndSuppressesOpenGeneric()
    {
        const string source = """
            namespace SuppressTest;

            public struct Pair<params TArgs>
            {
            }

            public class Consumer
            {
                public static void Use(Pair<int, string> p)
                {
                }
            }
            """;

        var combined = CompileAndCombine(source);

        Assert.Contains("struct Pair<T1, T2>", combined);
        Assert.DoesNotContain("struct Pair<TArgs>", combined);
    }

    /// <summary>
    /// A variadic abstract type used at a concrete arity emits the monomorphized
    /// interface shell <c>interface ICountable&lt;T1, T2&gt;</c>, suppresses the
    /// open-generic form, and does NOT emit the placeholder pack storage field into
    /// the interface (C# interfaces cannot declare instance fields).
    /// </summary>
    [Fact]
    public void VariadicAbstractType_EmitsInterfaceShell_WithoutInstanceFields()
    {
        const string source = """
            namespace SuppressTest;

            public abstract ICountable<params TArgs>
            {
                int Count();
            }

            public class Consumer
            {
                public static void Use(ICountable<int, string> c)
                {
                }
            }
            """;

        var combined = CompileAndCombine(source);

        Assert.Contains("interface ICountable<T1, T2>", combined);
        Assert.DoesNotContain("interface ICountable<TArgs>", combined);
        // Interfaces cannot declare instance fields; the placeholder pack storage
        // must not be emitted for the interface kind.
        Assert.DoesNotContain("PackValue", combined);
    }

    private static string CompileAndCombine(string source)
    {
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(
            result.Succeeded && result.EmittedFiles is not null,
            "Compilation did not succeed: " + string.Join("; ", errors));

        return string.Join("\n", result.EmittedFiles!.Values);
    }
}