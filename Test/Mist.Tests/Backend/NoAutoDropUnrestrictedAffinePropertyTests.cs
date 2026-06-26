// Feature: drop-semantics, Property 8: No auto-drop for unrestricted and affine

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
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Tests for Property 8: No auto-drop for unrestricted and affine at scope exit.
///
/// Feature: drop-semantics, Property 8: No auto-drop for unrestricted and affine
///
/// **Validates: Requirements 2.6, 2.7, 4.8, 5.3**
///
/// For any local variable of an unrestricted type (no modifier) or an affine type,
/// the compiler SHALL NOT auto-insert a scope-exit <c>Drop()</c> call. Drop only fires
/// via explicit <c>with</c>-block mechanisms for those disciplines. Linear and relevant
/// locals, by contrast, DO get a scope-exit <c>Drop()</c> auto-inserted — that contrast
/// confirms the absence assertion is meaningful (the emitter is capable of emitting the
/// call, it simply must not for affine/unrestricted).
///
/// These tests compile in-process via a shared <see cref="CompilerDriver"/> and inspect
/// the emitted C# text. They spawn 0 external processes.
/// </summary>
public class NoAutoDropUnrestrictedAffinePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Compiles the given Mist source in-process and returns the concatenated emitted
    /// C# (all non-csproj files joined), or <c>null</c> when compilation fails.
    /// </summary>
    private static string? CompileAndGetEmittedCSharp(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_noautodrop_" + Guid.NewGuid().ToString("N"));
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
            if(!result.Succeeded || result.EmittedFiles is null)
            {
                return null;
            }

            var csharp = result.EmittedFiles
                .Where(kvp => !kvp.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Value);

            return string.Join("\n", csharp);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Builds a program declaring an affine, an unrestricted, and (for contrast) a
    /// linear type — each with a <c>Drop()</c> method — and a <c>Main</c> that creates
    /// one local of each, reaching scope exit WITHOUT being placed in a <c>with</c> form
    /// and WITHOUT any explicit <c>Drop()</c> call.
    /// </summary>
    private static string BuildProgram(string affineLocal, string unrestrictedLocal, string linearLocal)
    {
        return $$"""
            affine class Token
            {
                public void Drop()
                {
                }

                public int GetValue()
                {
                    return 1;
                }
            }
            class PlainResource
            {
                public void Drop()
                {
                }

                public int GetValue()
                {
                    return 2;
                }
            }
            linear class FileHandle
            {
                public void Drop()
                {
                }

                public int GetValue()
                {
                    return 3;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Token {{affineLocal}} = new Token();
                    PlainResource {{unrestrictedLocal}} = new PlainResource();
                    FileHandle {{linearLocal}} = new FileHandle();
                }
            }
            """;
    }

    /// <summary>
    /// Returns <c>true</c> when the emitted C# contains a scope-exit drop call
    /// <c>{localName}.Drop()</c> as a whole identifier — i.e. <paramref name="localName"/>
    /// is preceded by a non-identifier character. This avoids a single-char name like
    /// <c>a</c> spuriously matching inside a longer name like <c>gamma.Drop()</c>.
    /// </summary>
    private static bool ContainsScopeExitDrop(string emitted, string localName)
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(emitted, pattern);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Example-based: the core absence assertion plus the linear contrast.
    // **Validates: Requirements 2.6, 2.7, 4.8, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoScopeExitDrop_ForAffineOrUnrestrictedLocals()
    {
        var emitted = CompileAndGetEmittedCSharp(BuildProgram("tok", "plain", "file"));

        Assert.NotNull(emitted);

        // Affine and unrestricted locals must NOT have an auto-inserted scope-exit Drop().
        Assert.False(ContainsScopeExitDrop(emitted, "tok"),
            "Affine local 'tok' must not get a scope-exit Drop().");
        Assert.False(ContainsScopeExitDrop(emitted, "plain"),
            "Unrestricted local 'plain' must not get a scope-exit Drop().");

        // Contrast: the linear local DOES get a scope-exit Drop(), proving the emitter is
        // capable of emitting the call and the absence above is a real signal, not an
        // accident of the emitter never emitting Drop() at all.
        Assert.True(ContainsScopeExitDrop(emitted, "file"),
            "Linear local 'file' must get a scope-exit Drop() (contrast guard).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: across generated local names, no scope-exit Drop() is emitted
    // for affine/unrestricted locals (the linear local always is).
    // **Validates: Requirements 2.6, 2.7, 4.8, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] NamePool =
        ["a", "b", "c", "x", "y", "z", "alpha", "beta", "gamma", "handle"];

#pragma warning disable CA1034 // Nested types are the conventional FsCheck scenario-record style in this suite.

    public sealed record LocalNames(string Affine, string Unrestricted, string Linear)
    {
        public override string ToString() => $"affine={Affine}, unrestricted={Unrestricted}, linear={Linear}";
    }

#pragma warning restore CA1034

    private static Gen<LocalNames> GenLocalNames()
    {
        // Pick three DISTINCT names so the per-name substring assertions cannot collide.
        return from picked in Gen.Elements(NamePool).ListOf(3)
                   .Where(names => names.Distinct().Count() == 3)
               select new LocalNames(picked[0], picked[1], picked[2]);
    }

    public static Arbitrary<LocalNames> ArbitraryLocalNames() => GenLocalNames().ToArbitrary();

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NoAutoDropUnrestrictedAffinePropertyTests) })]
    public Property AffineAndUnrestricted_NeverGetScopeExitDrop(LocalNames names)
    {
        var emitted = CompileAndGetEmittedCSharp(BuildProgram(names.Affine, names.Unrestricted, names.Linear));

        if(emitted is null)
        {
            return false.Label("Compilation failed");
        }

        if(ContainsScopeExitDrop(emitted, names.Affine))
        {
            return false.Label($"Affine local '{names.Affine}' got an unexpected scope-exit Drop().");
        }

        if(ContainsScopeExitDrop(emitted, names.Unrestricted))
        {
            return false.Label($"Unrestricted local '{names.Unrestricted}' got an unexpected scope-exit Drop().");
        }

        // Contrast guard: the linear local MUST get a scope-exit Drop(), otherwise the
        // absence assertions above are vacuous.
        if(!ContainsScopeExitDrop(emitted, names.Linear))
        {
            return false.Label($"Linear local '{names.Linear}' was missing its scope-exit Drop() (contrast guard).");
        }

        return true.Label("No auto-drop for affine/unrestricted; linear drops as expected.");
    }
}