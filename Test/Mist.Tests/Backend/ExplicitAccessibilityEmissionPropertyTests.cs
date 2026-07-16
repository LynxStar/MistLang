// Feature: full-accessibility-model, Property 18: Emitted C# carries the explicit accessibility and sealed keywords
//
// **Validates: Requirements 15.1, 15.2, 15.3**

using System.Collections.Immutable;
using System.Text;
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
/// Property-based tests verifying that the emitted C# carries explicit accessibility
/// keywords and <c>sealed</c> for every declaration accepted by the classic pipeline.
///
/// Feature: full-accessibility-model, Property 18: Emitted C# carries the explicit
/// accessibility and sealed keywords.
///
/// For any declaration accepted by the classic pipeline, the emitted C# includes an
/// explicit accessibility keyword sequence corresponding to the declaration's resolved
/// Accessibility value — emitting an explicit <c>private</c> when the effective
/// accessibility is Private rather than relying on the C# implicit default — and a
/// <c>sealed</c> keyword on any emitted class whose Sealed_Flag is set.
///
/// Compilation runs entirely in-process via a shared <see cref="CompilerDriver"/>
/// with <c>EmitToDisk: false</c>; the emitted C# files are read back from
/// <c>result.EmittedFiles</c> and concatenated for inspection.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 15.1, 15.2, 15.3**
/// </summary>
public sealed class ExplicitAccessibilityEmissionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Compiles the source in-process and returns the emitted C# files (excluding
    /// the project file), or <c>null</c> if compilation failed.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_access_emit_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
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

            return result.EmittedFiles;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Concatenates the emitted C# source files (skipping <c>.csproj</c> files).
    /// </summary>
    private static string ConcatEmittedCSharp(IReadOnlyDictionary<string, string> emittedFiles)
    {
        var sb = new StringBuilder();
        foreach(var (fileName, content) in emittedFiles)
        {
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario for testing accessibility emission: a Mist source,
    /// the expected accessibility keyword in the emitted C#, the type name to find
    /// in the emitted output, and whether sealed/class/struct.
    /// </summary>
    public sealed record EmissionScenario(
        string Source,
        string TypeName,
        string ExpectedAccessKeyword,
        bool ExpectSealed,
        bool IsClass,
        string Description)
    {
        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] StructNames =
        ["Point", "Coord", "Cell", "Pixel", "Slot", "Node"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<EmissionScenario> GenEmissionScenario()
    {
        var genAccess = Gen.Elements("public", "private", "protected", "internal");
        var genSealed = Gen.Elements(true, false);
        var genIsClass = Gen.Elements(true, false);
        var genClassIdx = Gen.Choose(0, ClassNames.Length - 1);
        var genStructIdx = Gen.Choose(0, StructNames.Length - 1);

        return from access in genAccess
               from isSealed in genSealed
               from isClass in genIsClass
               from classIdx in genClassIdx
               from structIdx in genStructIdx
               select BuildScenario(access, isSealed, isClass, classIdx, structIdx);
    }

    private static EmissionScenario BuildScenario(
        string access, bool isSealed, bool isClass, int classIdx, int structIdx)
    {
        var typeName = isClass ? ClassNames[classIdx] : StructNames[structIdx];
        var sealedMod = isSealed ? "sealed " : "";
        var typeKind = isClass ? "class" : "struct";

        // For private/protected types, nest inside a public class to make the declaration valid.
        var source = access is "private" or "protected"
            ? $$"""
                public class Outer
                {
                    {{access}} {{sealedMod}}{{typeKind}} {{typeName}}
                    {
                        public int Value;

                        public {{typeName}}()
                        {
                            this.Value = 0;
                        }
                    }
                }
                class Program
                {
                    static void Main()
                    {
                    }
                }
                """
            : $$"""
                {{access}} {{sealedMod}}{{typeKind}} {{typeName}}
                {
                    private int value;

                    public {{typeName}}()
                    {
                        this.value = 0;
                    }
                }
                class Program
                {
                    static void Main()
                    {
                    }
                }
                """;

        // For structs, sealed is never emitted in C# (C# structs are implicitly sealed).
        var expectSealed = isSealed && isClass;

        var desc = $"{access} {(isSealed ? "sealed " : "")}{typeKind} {typeName}";
        return new EmissionScenario(source, typeName, access, expectSealed, isClass, desc);
    }

    public static Arbitrary<EmissionScenario> ArbitraryEmissionScenario()
    {
        return GenEmissionScenario().ToArbitrary();
    }

    // ─── Assertions ──────────────────────────────────────────────────────

    /// <summary>
    /// Finds the line in the emitted C# that declares the given type (class or struct).
    /// Returns <c>null</c> if not found.
    /// </summary>
    private static string? FindTypeDeclarationLine(string emitted, string typeName, bool isClass)
    {
        var typeKeyword = isClass ? "class" : "struct";
        // Match a line that contains the type keyword followed by the type name.
        // The regex accounts for modifiers preceding the keyword.
        var pattern = $@"^[^\n]*\b{typeKeyword}\s+{Regex.Escape(typeName)}\b[^\n]*$";
        var match = Regex.Match(emitted, pattern, RegexOptions.Multiline);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Checks that the given declaration line starts with the expected access keyword.
    /// </summary>
    private static bool HasExplicitAccessKeyword(string declarationLine, string expectedKeyword)
    {
        // The accessibility keyword should appear as a standalone word in the declaration line.
        return Regex.IsMatch(declarationLine, $@"\b{Regex.Escape(expectedKeyword)}\b");
    }

    /// <summary>
    /// Checks that 'sealed' appears in the declaration line.
    /// </summary>
    private static bool HasSealedKeyword(string declarationLine)
    {
        return Regex.IsMatch(declarationLine, @"\bsealed\b");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 18: Emitted C# carries the explicit accessibility and sealed keywords
    //
    // For any declaration accepted by the classic pipeline, the emitted C# includes
    // an explicit accessibility keyword and, for sealed classes, the sealed keyword.
    //
    // **Validates: Requirements 15.1, 15.2, 15.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ExplicitAccessibilityEmissionPropertyTests) })]
    public Property EmittedCSharp_CarriesExplicitAccessibility_AndSealedOnClasses(EmissionScenario scenario)
    {
        var emittedFiles = CompileAndGetEmittedFiles(scenario.Source);

        if(emittedFiles is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        var emitted = ConcatEmittedCSharp(emittedFiles);

        // Find the emitted type declaration line.
        var declLine = FindTypeDeclarationLine(emitted, scenario.TypeName, scenario.IsClass);

        if(declLine is null)
        {
            return false.ToProperty()
                .Label($"Could not find '{scenario.TypeName}' declaration in emitted C#.\n"
                    + $"Scenario: {scenario.Description}\nEmitted:\n{emitted}");
        }

        // Assert 1: The explicit accessibility keyword is present.
        var hasAccess = HasExplicitAccessKeyword(declLine, scenario.ExpectedAccessKeyword);
        if(!hasAccess)
        {
            return false.ToProperty()
                .Label($"Missing explicit '{scenario.ExpectedAccessKeyword}' keyword in declaration.\n"
                    + $"Declaration line: {declLine}\n"
                    + $"Scenario: {scenario.Description}");
        }

        // Assert 2: sealed appears on sealed classes, does not appear on structs.
        var hasSealed = HasSealedKeyword(declLine);
        if(scenario.ExpectSealed && !hasSealed)
        {
            return false.ToProperty()
                .Label($"Expected 'sealed' keyword but it is missing.\n"
                    + $"Declaration line: {declLine}\n"
                    + $"Scenario: {scenario.Description}");
        }

        if(!scenario.ExpectSealed && hasSealed)
        {
            return false.ToProperty()
                .Label($"Unexpected 'sealed' keyword present.\n"
                    + $"Declaration line: {declLine}\n"
                    + $"Scenario: {scenario.Description}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit private emission (Req 15.2)
    //
    // Specifically validates that `private` is emitted explicitly and never
    // omitted (relying on the C# default).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ExplicitAccessibilityEmissionPropertyTests) })]
    public Property EmittedCSharp_PrivateIsAlwaysExplicit(PrivateEmissionScenario scenario)
    {
        var emittedFiles = CompileAndGetEmittedFiles(scenario.Source);

        if(emittedFiles is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        var emitted = ConcatEmittedCSharp(emittedFiles);

        // Find the emitted type declaration line for a private nested type.
        var declLine = FindTypeDeclarationLine(emitted, scenario.TypeName, scenario.IsClass);

        if(declLine is null)
        {
            return false.ToProperty()
                .Label($"Could not find '{scenario.TypeName}' declaration in emitted C#.\n"
                    + $"Scenario: {scenario.Description}\nEmitted:\n{emitted}");
        }

        // The declaration must explicitly contain 'private' — not just have no access modifier.
        var hasPrivate = HasExplicitAccessKeyword(declLine, "private");
        return hasPrivate.ToProperty()
            .Label($"'private' not explicitly emitted.\n"
                + $"Declaration line: {declLine}\n"
                + $"Scenario: {scenario.Description}\nEmitted:\n{emitted}");
    }

    // ─── Private emission scenario ──────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario specifically for testing explicit private emission.
    /// These are private nested types (the only way to have a private type).
    /// </summary>
    public sealed record PrivateEmissionScenario(
        string Source,
        string TypeName,
        bool IsClass,
        string Description)
    {
        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    private static readonly string[] PrivateTypeNames =
        ["Inner", "Nested", "Hidden", "Secret", "Impl", "Core"];

    private static Gen<PrivateEmissionScenario> GenPrivateEmissionScenario()
    {
        var genIsClass = Gen.Elements(true, false);
        var genNameIdx = Gen.Choose(0, PrivateTypeNames.Length - 1);
        var genSealed = Gen.Elements(true, false);

        return from isClass in genIsClass
               from nameIdx in genNameIdx
               from isSealed in genSealed
               select BuildPrivateScenario(isClass, nameIdx, isSealed);
    }

    private static PrivateEmissionScenario BuildPrivateScenario(bool isClass, int nameIdx, bool isSealed)
    {
        var typeName = PrivateTypeNames[nameIdx];
        var typeKind = isClass ? "class" : "struct";
        var sealedMod = isSealed ? "sealed " : "";

        var source = $$"""
            public class Container
            {
                private {{sealedMod}}{{typeKind}} {{typeName}}
                {
                    public int Value;

                    public {{typeName}}()
                    {
                        this.Value = 0;
                    }
                }
            }
            class Program
            {
                static void Main()
                {
                }
            }
            """;

        var desc = $"private {(isSealed ? "sealed " : "")}{typeKind} {typeName} (nested)";
        return new PrivateEmissionScenario(source, typeName, isClass, desc);
    }

    public static Arbitrary<PrivateEmissionScenario> ArbitraryPrivateEmissionScenario()
    {
        return GenPrivateEmissionScenario().ToArbitrary();
    }
}