// Feature: variadics, task 20.3: in-process integration test for Func/Action across
// arities (classic pipeline).
//
// **Validates: Requirements 15.3, 15.4, 15.5, 15.6, 15.7, 15.8**
//
// Compiles a Mist program in-process via the shared classic CompilerDriver that uses
// Func<int, string>, Func<string>, Action<int, string>, and invocations. Asserts:
//   - Compilation succeeds with zero errors (Req 15.3, 15.4, 15.5, 15.6)
//   - Emitted C# contains Func<int, string> (Req 15.3)
//   - Emitted C# contains Func<string> (Req 15.4)
//   - Emitted C# contains Action<int, string> (Req 15.5)
//   - Invocation type-checks at each arity including the empty pack (Req 15.6, 15.7, 15.8)
//
// ZERO external process spawns. Uses VariadicsCompilerFixture.Compile (EmitToDisk:false).

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// In-process integration tests verifying that the single variadic <c>Func</c> and
/// <c>Action</c> declarations bind correctly across arities and emit the expected
/// CLR delegate types. All compilation runs through the shared classic
/// <see cref="VariadicsCompilerFixture"/> with zero process spawns.
/// </summary>
public sealed class VariadicsFuncActionIntegrationTests
{
    /// <summary>
    /// The sample Mist program exercising Func/Action at multiple arities including
    /// the empty pack. Uses the Standard namespace (loaded by the fixture).
    /// </summary>
    private const string Source =
        "using Standard;\n"
        + "\n"
        + "namespace FuncTest;\n"
        + "\n"
        + "public class Program\n"
        + "{\n"
        + "    public static string UseFunc(Func<int, string> f)\n"
        + "    {\n"
        + "        return f(42);\n"
        + "    }\n"
        + "\n"
        + "    public static string UseZeroArg(Func<string> f)\n"
        + "    {\n"
        + "        return f();\n"
        + "    }\n"
        + "\n"
        + "    public static void UseAction(Action<int, string> a)\n"
        + "    {\n"
        + "        a(1, \"x\");\n"
        + "    }\n"
        + "}\n";

    /// <summary>
    /// Compilation succeeds with zero errors -- the variadic Func/Action declarations
    /// bind correctly at all exercised arities (Requirements 15.3, 15.4, 15.5, 15.6).
    /// </summary>
    [Fact]
    public void FuncAction_AcrossArities_CompilesWithZeroErrors()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"Expected zero errors but got {errors.Count}:\n{FormatDiagnostics(errors)}");
    }

    /// <summary>
    /// The emitted C# contains <c>Func&lt;int, string&gt;</c> with a <c>using System;</c>
    /// directive -- the arity-1 case where TArgs=[int], TResult=string (Requirement 15.3).
    /// The emitter uses the short form because it imports System.
    /// </summary>
    [Fact]
    public void FuncIntString_EmitsCorrectDelegateType()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var userFile = GetUserFile(result.EmittedFiles!);

        // The emitter imports System, so Func<int, string> appears unqualified.
        Assert.Contains("using System;", userFile);
        Assert.Contains("Func<int, string>", userFile);
    }

    /// <summary>
    /// The emitted C# contains <c>Func&lt;string&gt;</c> -- the empty-pack case
    /// where TArgs=[], TResult=string (Requirement 15.4).
    /// </summary>
    [Fact]
    public void FuncString_EmitsCorrectDelegateType()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var userFile = GetUserFile(result.EmittedFiles!);
        Assert.Contains("Func<string>", userFile);
    }

    /// <summary>
    /// The emitted C# contains <c>Action&lt;int, string&gt;</c> -- the Action alias
    /// resolves to Func&lt;int, string, void&gt; and the backend maps it to the CLR
    /// Action delegate (Requirement 15.5).
    /// </summary>
    [Fact]
    public void ActionIntString_EmitsCorrectDelegateType()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
        Assert.NotNull(result.EmittedFiles);

        var userFile = GetUserFile(result.EmittedFiles!);
        Assert.Contains("Action<int, string>", userFile);
    }

    /// <summary>
    /// Func invocation at arity-1 (one parameter) type-checks -- calling f(42) on a
    /// Func&lt;int, string&gt; is well-typed (Requirement 15.6, 15.8).
    /// </summary>
    [Fact]
    public void FuncInvocation_Arity1_TypeChecks()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
    }

    /// <summary>
    /// Func invocation at arity-0 (empty pack, zero arguments) type-checks -- calling
    /// f() on a Func&lt;string&gt; is well-typed (Requirement 15.4, 15.6, 15.8).
    /// </summary>
    [Fact]
    public void FuncInvocation_EmptyPack_TypeChecks()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
    }

    /// <summary>
    /// Action invocation at arity-2 type-checks -- calling a(1, "x") on an
    /// Action&lt;int, string&gt; (which resolves to Func&lt;int, string, void&gt;) is
    /// well-typed (Requirement 15.5, 15.6, 15.8).
    /// </summary>
    [Fact]
    public void ActionInvocation_Arity2_TypeChecks()
    {
        var result = VariadicsCompilerFixture.Compile(Source, globalUsings: "Standard");
        Assert.True(result.Succeeded, $"Compilation failed:\n{FormatDiagnostics(result)}");
    }

    /// <summary>
    /// Func invocation with wrong argument count is rejected -- this proves the
    /// generalized rule rejects length mismatches (Requirement 15.7).
    /// </summary>
    [Fact]
    public void FuncInvocation_WrongArgCount_IsRejected()
    {
        const string badSource =
            "using Standard;\n"
            + "\n"
            + "namespace FuncTest;\n"
            + "\n"
            + "public class Bad\n"
            + "{\n"
            + "    public static string WrongCount(Func<int, string> f)\n"
            + "    {\n"
            + "        return f(1, 2);\n"
            + "    }\n"
            + "}\n";

        var result = VariadicsCompilerFixture.Compile(badSource, globalUsings: "Standard");

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count > 0,
            "Expected a type error for wrong argument count, but compilation succeeded.");
    }

    /// <summary>
    /// Func invocation with wrong argument type is rejected -- this proves the
    /// generalized rule rejects type mismatches (Requirement 15.7).
    /// </summary>
    [Fact]
    public void FuncInvocation_WrongArgType_IsRejected()
    {
        const string badSource =
            "using Standard;\n"
            + "\n"
            + "namespace FuncTest;\n"
            + "\n"
            + "public class Bad\n"
            + "{\n"
            + "    public static string WrongType(Func<int, string> f)\n"
            + "    {\n"
            + "        return f(\"not an int\");\n"
            + "    }\n"
            + "}\n";

        var result = VariadicsCompilerFixture.Compile(badSource, globalUsings: "Standard");

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count > 0,
            "Expected a type error for wrong argument type, but compilation succeeded.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the emitted C# content for the user's source file (Program.cs),
    /// excluding stdlib emission files.
    /// </summary>
    private static string GetUserFile(IReadOnlyDictionary<string, string> files)
    {
        // The fixture writes source to Program.mist; the emitter produces Program.cs.
        if(files.TryGetValue("Program.cs", out var content))
        {
            return content;
        }

        // Fallback: combine all files (the assertion message will show what's present).
        return string.Join("\n---FILE BOUNDARY---\n",
            files.Select(kvp => $"[{kvp.Key}]\n{kvp.Value}"));
    }

    private static string FormatDiagnostics(Mist.Driver.Contracts.CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
    }
}