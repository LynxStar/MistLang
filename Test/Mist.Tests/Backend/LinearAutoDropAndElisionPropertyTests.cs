// Feature: drop-semantics, Property 5: Linear auto-drop at scope exit
//
// **Validates: Requirements 2.8, 4.1, 4.2, 4.3**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes the fate of a linear local variable: whether it stays live at scope exit,
/// is moved to a function parameter, or is returned from the method.
/// </summary>
public enum LinearLocalFate
{
    /// <summary>The variable stays live at scope exit — Drop() MUST be emitted.</summary>
    LiveAtScopeExit,

    /// <summary>The variable is moved to another function — Drop() MUST NOT be emitted.</summary>
    MovedToFunction,

    /// <summary>The variable is returned from the method — Drop() MUST NOT be emitted.</summary>
    Returned,
}

/// <summary>
/// A generated scenario: a linear local with a specific fate, plus the Mist source and
/// expected assertions about the emitted C#.
/// </summary>
public sealed class LinearDropElisionScenario
{
    public string Source { get; }
    public string LocalName { get; }
    public LinearLocalFate Fate { get; }
    public string Description { get; }

    public LinearDropElisionScenario(string source, string localName, LinearLocalFate fate, string description)
    {
        Source = source;
        LocalName = localName;
        Fate = fate;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class LinearDropElisionGenerators
{
    // Distinct identifier pools for the linear local variable name.
    private static readonly string[] LocalNames = ["res", "handle", "conn", "buf", "stream"];

    public static Gen<LinearDropElisionScenario> GenLinearDropElisionScenario()
    {
        return from local in Gen.Elements(LocalNames)
               from fate in Gen.Elements(
                   LinearLocalFate.LiveAtScopeExit,
                   LinearLocalFate.MovedToFunction,
                   LinearLocalFate.Returned)
               let source = BuildSource(local, fate)
               select new LinearDropElisionScenario(
                   source,
                   local,
                   fate,
                   $"linear local '{local}' fate={fate}");
    }

    private static string BuildSource(string local, LinearLocalFate fate)
    {
        // We build different programs depending on the fate:
        // - LiveAtScopeExit: the method is void, variable is created and used but not moved or returned.
        // - MovedToFunction: the variable is passed to a consuming function (owned parameter).
        // - Returned: the method returns the linear type and the variable is the return expression.
        return fate switch
        {
            LinearLocalFate.LiveAtScopeExit => BuildLiveAtScopeExitProgram(local),
            LinearLocalFate.MovedToFunction => BuildMovedToFunctionProgram(local),
            LinearLocalFate.Returned => BuildReturnedProgram(local),
            _ => throw new ArgumentOutOfRangeException(nameof(fate)),
        };
    }

    private static string BuildLiveAtScopeExitProgram(string local)
    {
        // Linear local is created and used (accessor call) but NOT moved or returned.
        // It should receive a scope-exit Drop().
        return $$"""
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource {{local}} = new Resource(1);
                    int x = {{local}}.GetId();
                }
            }
            """;
    }

    private static string BuildMovedToFunctionProgram(string local)
    {
        // Linear local is passed (moved) to a consuming function.
        // The function takes ownership; no Drop() should be emitted in the caller for this local.
        return $$"""
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Sink
            {
                public static void Consume(Resource r)
                {
                    int x = r.GetId();
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource {{local}} = new Resource(1);
                    Sink.Consume({{local}});
                }
            }
            """;
    }

    private static string BuildReturnedProgram(string local)
    {
        // Linear local is returned from the method — ownership transfers to the caller.
        // No Drop() should be emitted for this local in the method that returns it.
        return $$"""
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static Resource Create()
                {
                    Resource {{local}} = new Resource(1);
                    return {{local}};
                }

                public static void Main()
                {
                    Resource r = Program.Create();
                    int x = r.GetId();
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for linear auto-drop at scope exit (Property 5) and linear drop
/// elision on move or return (Property 6).
///
/// Feature: drop-semantics, Property 5: Linear auto-drop at scope exit
///
/// **Validates: Requirements 2.8, 4.1, 4.2, 4.3**
///
/// Property 5: For any local variable of a linear type that reaches scope exit in the
/// Live state (not moved, returned, or explicitly dropped), the compiler SHALL insert
/// exactly one Drop() call at that scope exit point.
///
/// Property 6: For any local variable of a linear type that has been moved to another
/// variable or function parameter, or returned from the method, the compiler SHALL NOT
/// insert a Drop() call at the source scope exit.
///
/// All compilation runs fully in-process via a shared <see cref="CompilerDriver"/> with
/// <c>EmitToDisk: false</c>; the emitted C# text is inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class LinearAutoDropAndElisionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public LinearAutoDropAndElisionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistLinearDropElisionPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<LinearDropElisionScenario> ArbitraryLinearDropElisionScenario() =>
        LinearDropElisionGenerators.GenLinearDropElisionScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns the emitted user C# file (the one containing the test namespace), or null.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when the emitted C# contains a scope-exit drop call
    /// <c>{localName}.Drop()</c> preceded by a non-identifier character (word-boundary
    /// match), avoiding false positives from longer identifiers.
    /// </summary>
    private static bool ContainsScopeExitDrop(string emitted, string localName)
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(emitted, pattern);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5 & 6: Linear auto-drop at scope exit / drop elision on
    // move or return
    //
    // **Validates: Requirements 2.8, 4.1, 4.2, 4.3**
    //
    // For each generated scenario:
    // - If the linear local stays Live at scope exit → emitted C# MUST contain
    //   `{local}.Drop()` (Property 5).
    // - If the linear local is moved to a function or returned → emitted C# MUST
    //   NOT contain `{local}.Drop()` in the method that moved/returned it
    //   (Property 6).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinearAutoDropAndElisionPropertyTests) })]
    public Property LinearLocal_DropOrElision_DependsOnFate(LinearDropElisionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found.\nSource:\n{scenario.Source}");
        }

        var hasDrop = ContainsScopeExitDrop(emittedCSharp, scenario.LocalName);

        return scenario.Fate switch
        {
            // Property 5: Live linear local → Drop() MUST be present.
            LinearLocalFate.LiveAtScopeExit => hasDrop.ToProperty()
                .Label($"Expected '{scenario.LocalName}.Drop()' in emitted C# (live at scope exit).\n" +
                       $"Source:\n{scenario.Source}\nEmitted C#:\n{emittedCSharp}"),

            // Property 6: Moved linear local → Drop() MUST NOT be present for this local.
            LinearLocalFate.MovedToFunction => (!hasDrop).ToProperty()
                .Label($"Expected NO '{scenario.LocalName}.Drop()' in emitted C# (moved to function).\n" +
                       $"Source:\n{scenario.Source}\nEmitted C#:\n{emittedCSharp}"),

            // Property 6: Returned linear local → Drop() MUST NOT be present for this local.
            LinearLocalFate.Returned => (!hasDrop).ToProperty()
                .Label($"Expected NO '{scenario.LocalName}.Drop()' in emitted C# (returned from method).\n" +
                       $"Source:\n{scenario.Source}\nEmitted C#:\n{emittedCSharp}"),

            _ => false.ToProperty().Label("Unknown fate"),
        };
    }

    // ─── Example-based tests (deterministic) ─────────────────────────────

    /// <summary>
    /// Property 5: a linear local that is live at scope exit gets a scope-exit Drop().
    /// **Validates: Requirements 2.8, 4.1**
    /// </summary>
    [Fact]
    public void LinearLocal_LiveAtScopeExit_EmitsDrop()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource res = new Resource(1);
                    int x = res.GetId();
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);
        Assert.True(ContainsScopeExitDrop(emitted, "res"),
            $"Expected 'res.Drop()' in emitted C# for live linear local.\nEmitted:\n{emitted}");
    }

    /// <summary>
    /// Property 6: a linear local moved to a function does NOT get a scope-exit Drop().
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Fact]
    public void LinearLocal_MovedToFunction_ElidesDrop()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Sink
            {
                public static void Consume(Resource r)
                {
                    int x = r.GetId();
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource res = new Resource(1);
                    Sink.Consume(res);
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);
        Assert.False(ContainsScopeExitDrop(emitted, "res"),
            $"Expected NO 'res.Drop()' in emitted C# for moved linear local.\nEmitted:\n{emitted}");
    }

    /// <summary>
    /// Property 6: a linear local returned from the method does NOT get a scope-exit Drop().
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Fact]
    public void LinearLocal_Returned_ElidesDrop()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int val)
                {
                    this.id = val;
                }

                public void Drop()
                {
                }

                public int GetId()
                {
                    return id;
                }
            }
            class Program
            {
                public static Resource Create()
                {
                    Resource res = new Resource(1);
                    return res;
                }

                public static void Main()
                {
                    Resource r = Program.Create();
                    int x = r.GetId();
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}")));

        var emitted = GetEmittedCSharp(result);
        Assert.NotNull(emitted);

        // Look specifically in the Create() method output - the variable 'res' should not
        // have a Drop() because it's returned (ownership transferred to caller).
        // The Main() method's 'r' local WILL have a Drop() since it's live at scope exit.
        Assert.False(ContainsScopeExitDrop(emitted, "res"),
            $"Expected NO 'res.Drop()' in emitted C# for returned linear local.\nEmitted:\n{emitted}");
    }
}