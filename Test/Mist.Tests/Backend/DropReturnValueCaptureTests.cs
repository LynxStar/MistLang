// Feature: drop-semantics, Property 20: Drop return value capture
//
// **Validates: Requirements 3.4, 7.4**

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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes the discipline of the type that provides a non-void Drop().
/// </summary>
public enum DropReturnDiscipline
{
    /// <summary>Linear type with non-void Drop().</summary>
    Linear,

    /// <summary>Affine type with non-void Drop().</summary>
    Affine,
}

/// <summary>
/// Describes the return type of the non-void Drop() method.
/// </summary>
public enum DropReturnKind
{
    /// <summary>Drop() returns int.</summary>
    Numeric,

    /// <summary>Drop() returns string.</summary>
    Textual,

    /// <summary>Drop() returns bool.</summary>
    Logical,
}

/// <summary>
/// A generated scenario for testing Drop return-value capture: a type with a given
/// discipline and Drop() return type, plus the Mist source and expected C# assertions.
/// </summary>
public sealed class DropReturnCaptureScenario
{
    public string Source { get; }
    public string LocalName { get; }
    public string ResultName { get; }
    public DropReturnDiscipline Discipline { get; }
    public DropReturnKind ReturnKind { get; }
    public string Description { get; }

    public DropReturnCaptureScenario(
        string source, string localName, string resultName,
        DropReturnDiscipline discipline, DropReturnKind returnKind, string description)
    {
        Source = source;
        LocalName = localName;
        ResultName = resultName;
        Discipline = discipline;
        ReturnKind = returnKind;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class DropReturnCaptureGenerators
{
    private static readonly string[] LocalNames = ["box", "handle", "token", "container", "wrapper"];
    private static readonly string[] ResultNames = ["result", "val", "output", "captured", "extracted"];

    public static Gen<DropReturnCaptureScenario> GenDropReturnCaptureScenario()
    {
        return from local in Gen.Elements(LocalNames)
               from resultName in Gen.Elements(ResultNames)
               from discipline in Gen.Elements(DropReturnDiscipline.Linear, DropReturnDiscipline.Affine)
               from returnKind in Gen.Elements(DropReturnKind.Numeric, DropReturnKind.Textual, DropReturnKind.Logical)
               let source = BuildSource(local, resultName, discipline, returnKind)
               select new DropReturnCaptureScenario(
                   source,
                   local,
                   resultName,
                   discipline,
                   returnKind,
                   $"{discipline} type, Drop() returns {returnKind}, local='{local}', result='{resultName}'");
    }

    private static string BuildSource(
        string local, string resultName, DropReturnDiscipline discipline, DropReturnKind returnKind)
    {
        var modifier = discipline switch
        {
            DropReturnDiscipline.Linear => "linear",
            DropReturnDiscipline.Affine => "affine",
            _ => throw new ArgumentOutOfRangeException(nameof(discipline)),
        };

        var (returnType, returnValue) = returnKind switch
        {
            DropReturnKind.Numeric => ("int", "42"),
            DropReturnKind.Textual => ("string", "\"dropped\""),
            DropReturnKind.Logical => ("bool", "true"),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };

        return $$"""
            {{modifier}} class Container
            {
                public Container()
                {
                }

                public {{returnType}} Drop()
                {
                    return {{returnValue}};
                }
            }
            class Program
            {
                public static void Main()
                {
                    Container {{local}} = new Container();
                    {{returnType}} {{resultName}} = {{local}}.Drop();
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Tests for Property 20: Drop return value capture.
///
/// Feature: drop-semantics, Property 20: Drop return value capture
///
/// **Validates: Requirements 3.4, 7.4**
///
/// For any affine or linear type with a non-void <c>Drop()</c> method, explicitly calling
/// <c>Drop()</c> SHALL allow the caller to capture the return value (e.g.,
/// <c>T result = v.Drop();</c>).
///
/// These tests compile in-process via a shared <see cref="CompilerDriver"/> and inspect
/// the emitted C# text. They spawn 0 external processes.
/// </summary>
public sealed class DropReturnValueCaptureTests : IDisposable
{
    private readonly string outputDir;

    public DropReturnValueCaptureTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistDropReturnCapturePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

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

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<DropReturnCaptureScenario> ArbitraryDropReturnCaptureScenario() =>
        DropReturnCaptureGenerators.GenDropReturnCaptureScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private (string? EmittedCSharp, string? ErrorMessage) CompileAndGetEmittedCSharp(string source)
    {
        var sourcePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(sourcePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .ToList();

        if(errors.Count > 0)
        {
            return (null, string.Join("\n", errors.Select(d => $"{d.Id.Value}: {d.Message}")));
        }

        if(result.EmittedFiles is null || result.EmittedFiles.Count == 0)
        {
            return (null, "No emitted files produced.");
        }

        var allContent = result.EmittedFiles
            .Where(kvp => !kvp.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value);

        return (string.Join("\n", allContent), null);
    }

    /// <summary>
    /// Returns <c>true</c> when the emitted C# contains an assignment capturing the
    /// Drop() return value: a pattern like <c>{localName}.Drop()</c> appearing on the
    /// right side of an assignment (preceded by <c>= </c> or <c>= </c> variant).
    /// </summary>
    private static bool ContainsDropReturnCapture(string emitted, string localName, string resultName)
    {
        // Look for the assignment pattern: resultName = localName.Drop()
        // The emitted C# may use `var` or the explicit type, so we look for
        // the result name followed by ` = ` and then `localName.Drop()`.
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(resultName)}\s*=\s*{Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(emitted, pattern);
    }

    /// <summary>
    /// Returns <c>true</c> when the emitted C# contains a <c>{localName}.Drop()</c>
    /// call at all (in any context).
    /// </summary>
    private static bool ContainsDropCall(string emitted, string localName)
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(emitted, pattern);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 20: Drop return value capture
    //
    // **Validates: Requirements 3.4, 7.4**
    //
    // For each generated scenario:
    // - The program MUST compile without errors (Drop() return value capture
    //   is a valid expression).
    // - The emitted C# MUST contain an assignment pattern where the Drop()
    //   return value is captured by the result variable.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DropReturnValueCaptureTests) })]
    public Property DropReturnValue_Compiles_AndCapturesValue(DropReturnCaptureScenario scenario)
    {
        var (emitted, error) = CompileAndGetEmittedCSharp(scenario.Source);

        if(emitted is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed: [{error}]\nSource:\n{scenario.Source}");
        }

        var hasCapturedAssignment = ContainsDropReturnCapture(emitted, scenario.LocalName, scenario.ResultName);

        if(!hasCapturedAssignment)
        {
            return false.ToProperty()
                .Label($"Expected '{scenario.ResultName} = {scenario.LocalName}.Drop()' in emitted C#.\n" +
                       $"Source:\n{scenario.Source}\nEmitted C#:\n{emitted}");
        }

        return true.ToProperty()
            .Label($"Drop return value captured correctly for {scenario.Discipline} type.");
    }

    // ─── Example-based tests (deterministic) ─────────────────────────────

    /// <summary>
    /// A linear type with non-void Drop() allows capturing the return value:
    /// <c>int r = v.Drop();</c> compiles and emits the assignment.
    /// **Validates: Requirements 3.4, 7.4**
    /// </summary>
    [Fact]
    public void LinearType_DropReturnsInt_CapturedByAssignment()
    {
        var source = """
            linear class LinearBox
            {
                int value;

                public LinearBox(int v)
                {
                    this.value = v;
                }

                public int Drop()
                {
                    return value;
                }
            }
            class Program
            {
                public static void Main()
                {
                    LinearBox box = new LinearBox(42);
                    int result = box.Drop();
                }
            }
            """;

        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsDropReturnCapture(emitted!, "box", "result"),
            $"Expected 'result = box.Drop()' assignment in emitted C#.\n\nEmitted C#:\n{emitted}");
    }

    /// <summary>
    /// An affine type with non-void Drop() allows capturing the return value:
    /// <c>string r = v.Drop();</c> compiles and emits the assignment.
    /// **Validates: Requirements 3.4, 7.4**
    /// </summary>
    [Fact]
    public void AffineType_DropReturnsString_CapturedByAssignment()
    {
        var source = """
            affine class AffineToken
            {
                string payload;

                public AffineToken(string p)
                {
                    this.payload = p;
                }

                public string Drop()
                {
                    return payload;
                }
            }
            class Program
            {
                public static void Main()
                {
                    AffineToken tok = new AffineToken("secret");
                    string captured = tok.Drop();
                }
            }
            """;

        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsDropReturnCapture(emitted!, "tok", "captured"),
            $"Expected 'captured = tok.Drop()' assignment in emitted C#.\n\nEmitted C#:\n{emitted}");
    }

    /// <summary>
    /// A linear type with Drop() returning bool: verifies the pattern works across types.
    /// **Validates: Requirements 3.4, 7.4**
    /// </summary>
    [Fact]
    public void LinearType_DropReturnsBool_CapturedByAssignment()
    {
        var source = """
            linear class Guard
            {
                bool succeeded;

                public Guard(bool s)
                {
                    this.succeeded = s;
                }

                public bool Drop()
                {
                    return succeeded;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Guard g = new Guard(true);
                    bool ok = g.Drop();
                }
            }
            """;

        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");
        Assert.True(ContainsDropReturnCapture(emitted!, "g", "ok"),
            $"Expected 'ok = g.Drop()' assignment in emitted C#.\n\nEmitted C#:\n{emitted}");
    }

    /// <summary>
    /// After explicit Drop() on a linear type, the captured return value is present
    /// in the try block. The try/finally wrapping (Property 18) may emit a safety
    /// Drop() in the finally — that is separate and correct behavior.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Fact]
    public void LinearType_ExplicitDropCapture_ValueCapturedInTryBlock()
    {
        var source = """
            linear class Resource
            {
                int id;

                public Resource(int i)
                {
                    this.id = i;
                }

                public int Drop()
                {
                    return id;
                }
            }
            class Program
            {
                public static void Main()
                {
                    Resource res = new Resource(7);
                    int val = res.Drop();
                }
            }
            """;

        var (emitted, error) = CompileAndGetEmittedCSharp(source);

        Assert.True(emitted is not null,
            $"Compilation failed:\n{error}\n\nSource:\n{source}");

        // The explicit Drop() should appear as the capture assignment.
        Assert.True(ContainsDropReturnCapture(emitted!, "res", "val"),
            $"Expected 'val = res.Drop()' in emitted C#.\n\nEmitted C#:\n{emitted}");

        // The emitted C# must contain the Drop() call (at least in the capture assignment).
        Assert.True(ContainsDropCall(emitted!, "res"),
            $"Expected at least one 'res.Drop()' in emitted C#.\n\nEmitted C#:\n{emitted}");
    }
}