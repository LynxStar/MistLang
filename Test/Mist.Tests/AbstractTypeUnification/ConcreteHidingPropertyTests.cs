// Feature: abstract-type-unification, Property 18: Concrete class hiding requires new

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// A scenario where a derived class declares a method with the same name as
/// a base class method (without the `new` keyword). The compiler should emit
/// a hiding warning diagnostic.
/// </summary>
public sealed class ConcreteHidingScenario
{
    public string Source { get; }
    public string BaseClassName { get; }
    public string DerivedClassName { get; }
    public string MethodName { get; }
    public string Description { get; }

    public ConcreteHidingScenario(
        string source,
        string baseClassName,
        string derivedClassName,
        string methodName,
        string description)
    {
        Source = source;
        BaseClassName = baseClassName;
        DerivedClassName = derivedClassName;
        MethodName = methodName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with base/derived class pairs where the derived
/// class declares a method with the same name as the base class method WITHOUT
/// the `new` keyword. Used to verify that a hiding warning is always emitted.
/// </summary>
public static class ConcreteHidingGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    private static readonly string[] MethodNames =
        ["Speak", "Compute", "GetValue", "Process", "Execute"];

    public static Gen<ConcreteHidingScenario> GenScenario()
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from methodName in Gen.Elements(MethodNames)
               from baseIndex in Gen.Choose(0, 99)
               from derivedIndex in Gen.Choose(0, 99)
               let baseClassName = $"Base{baseIndex}"
               let derivedClassName = $"Derived{derivedIndex}"
               let returnValue = returnType switch
               {
                   "string" => "\"base\"",
                   "int" => "42",
                   "bool" => "true",
                   _ => "default"
               }
               let derivedReturnValue = returnType switch
               {
                   "string" => "\"derived\"",
                   "int" => "99",
                   "bool" => "false",
                   _ => "default"
               }
               let source = $$"""
                   namespace TestProject;

                   public class {{baseClassName}}
                   {
                       public {{returnType}} {{methodName}}()
                       {
                           return {{returnValue}};
                       }
                   }

                   public class {{derivedClassName}} : {{baseClassName}}
                   {
                       public {{returnType}} {{methodName}}()
                       {
                           return {{derivedReturnValue}};
                       }
                   }

                   public class Program
                   {
                       public static void Main(string[] args)
                       {
                       }
                   }
                   """
               select new ConcreteHidingScenario(
                   source,
                   baseClassName,
                   derivedClassName,
                   methodName,
                   $"{derivedClassName} hides {baseClassName}.{methodName}() : {returnType}");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying concrete class hiding semantics.
///
/// Property 18: Concrete class hiding requires `new` and preserves base behavior.
/// For any Mist concrete class hierarchy where a derived class declares a method with
/// the same name and signature as a base class method:
/// (a) the compiler SHALL emit a warning if `new` is missing,
/// (b) the emitter SHALL generate `new` on the derived method, and
/// (c) calling the method through a base-typed reference SHALL invoke the base
///     implementation (hiding semantics, not override).
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 10.1, 10.2, 10.3**
/// </summary>
public sealed class ConcreteHidingPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public ConcreteHidingPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistConcreteHidingPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

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
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns all emitted C# content concatenated, or null if no files were emitted.
    /// </summary>
    private static string? GetAllEmittedCSharp(CompilerResult result)
    {
        if (result.EmittedFiles is null || result.EmittedFiles.Count == 0)
            return null;

        return string.Join("\n\n", result.EmittedFiles.Values);
    }

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// </summary>
    private static string? ExtractTypeSection(string emittedCSharp, string typeKeyword, string typeName)
    {
        var searchPattern = $"{typeKeyword} {typeName}";
        var typeIndex = emittedCSharp.IndexOf(searchPattern, StringComparison.Ordinal);
        if (typeIndex < 0)
            return null;

        var braceStart = emittedCSharp.IndexOf('{', typeIndex);
        if (braceStart < 0)
            return null;

        var depth = 0;
        for (var i = braceStart; i < emittedCSharp.Length; i++)
        {
            if (emittedCSharp[i] == '{') depth++;
            else if (emittedCSharp[i] == '}') depth--;

            if (depth == 0)
            {
                return emittedCSharp[braceStart..(i + 1)];
            }
        }

        return null;
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ConcreteHidingScenario> ArbitraryConcreteHidingScenario() =>
        ConcreteHidingGenerators.GenScenario().ToArbitrary();

    // ─── Test 1: Hiding WITH `new` keyword emits `new` in C# ────────────

    /// <summary>
    /// A derived class that hides a base method WITH the `new` keyword should
    /// emit `new` in the generated C# without any warning diagnostic.
    /// </summary>
    [Fact]
    public void HidingWithNewKeyword_EmitsNewInCSharp()
    {
        const string source = """
            namespace TestProject;

            public class Base
            {
                public string Speak()
                {
                    return "base";
                }
            }

            public class Derived : Base
            {
                new public string Speak()
                {
                    return "derived";
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        // No errors expected
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        // No hiding warning expected (new is present)
        var hidingWarnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Message.Contains("hides inherited member"))
            .ToList();
        Assert.Empty(hidingWarnings);

        // Verify emitted C# contains `new` keyword before `Speak` in Derived
        var emittedCSharp = GetAllEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        var derivedSection = ExtractTypeSection(emittedCSharp, "class", "Derived");
        Assert.NotNull(derivedSection);

        // The emitted C# should contain `new` before `Speak`
        Assert.Contains("new", derivedSection);

        var newIndex = derivedSection.IndexOf("new", StringComparison.Ordinal);
        var speakIndex = derivedSection.IndexOf("Speak", newIndex, StringComparison.Ordinal);
        Assert.True(speakIndex > newIndex,
            $"Expected 'new' keyword before 'Speak' method in emitted C#.\nDerived section:\n{derivedSection}");
    }

    // ─── Test 2: Hiding WITHOUT `new` keyword emits warning ─────────────

    /// <summary>
    /// A derived class that hides a base method WITHOUT the `new` keyword should
    /// emit a hiding warning diagnostic.
    /// </summary>
    [Fact]
    public void HidingWithoutNewKeyword_EmitsWarning()
    {
        const string source = """
            namespace TestProject;

            public class Base
            {
                public string Speak()
                {
                    return "base";
                }
            }

            public class Derived : Base
            {
                public string Speak()
                {
                    return "derived";
                }
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;

        var result = CompileSource(source);

        // No errors expected
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        // A hiding warning IS expected
        var hidingWarnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Message.Contains("hides inherited member"))
            .ToList();
        Assert.NotEmpty(hidingWarnings);

        // Verify the warning mentions the correct type and member names
        var warning = hidingWarnings.First();
        Assert.Contains("Derived.Speak", warning.Message);
        Assert.Contains("Base.Speak", warning.Message);
    }

    // ─── Test 3: FsCheck property — hiding without `new` always warns ───

    /// <summary>
    /// Property 18: For any base/derived class pair where the derived class declares
    /// a method with the same name as the base class method WITHOUT the `new` keyword,
    /// the compiler SHALL always emit a warning diagnostic about hiding.
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConcreteHidingPropertyTests) })]
    public Property HidingWithoutNew_AlwaysEmitsWarning(ConcreteHidingScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // No compilation errors expected
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
                .Label($"Source:\n{scenario.Source}");
        }

        // A hiding warning MUST be emitted
        var hidingWarnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Message.Contains("hides inherited member"))
            .ToList();

        if (hidingWarnings.Count == 0)
        {
            return false.ToProperty()
                .Label($"No hiding warning emitted for {scenario.DerivedClassName}.{scenario.MethodName} " +
                       $"hiding {scenario.BaseClassName}.{scenario.MethodName}")
                .Label($"All diagnostics: [{string.Join("; ", result.Diagnostics.Select(d => $"{d.Severity}: {d.Message}"))}]")
                .Label($"Source:\n{scenario.Source}");
        }

        // Verify the warning references the correct derived type and method
        var warningMentionsDerived = hidingWarnings.Any(w =>
            w.Message.Contains($"{scenario.DerivedClassName}.{scenario.MethodName}"));
        var warningMentionsBase = hidingWarnings.Any(w =>
            w.Message.Contains($"{scenario.BaseClassName}.{scenario.MethodName}"));

        var allChecksPass = warningMentionsDerived && warningMentionsBase;

        return allChecksPass.ToProperty()
            .Label($"Warning doesn't mention derived: {warningMentionsDerived}, base: {warningMentionsBase}")
            .Label($"Warnings: [{string.Join("; ", hidingWarnings.Select(w => w.Message))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
