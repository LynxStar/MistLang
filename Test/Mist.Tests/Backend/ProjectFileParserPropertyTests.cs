// Feature: project-based-build-model, Property 1: TOML parsing round-trip
// Feature: project-based-build-model, Property 2: Whitespace-only names are rejected
// Feature: project-based-build-model, Property 3: Name character validation
// Feature: project-based-build-model, Property 4: Project name propagation
// Feature: project-based-build-model, Property 5: Emitter uses project name for .csproj filename

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for <see cref="ProjectFileParser"/> covering TOML round-trip,
/// whitespace rejection, and name character validation.
/// </summary>
public sealed class ProjectFileParserPropertyTests
{
    // ─── Constants ───────────────────────────────────────────────────────

    private static readonly char[] ValidChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    private static readonly char[] WhitespaceChars = [' ', '\t', '\n', '\r'];

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidName(string Value)
    {
        public override string ToString() => Value;
    }

    public record WhitespaceOnly(string Value)
    {
        public override string ToString() => $"[whitespace len={Value.Length}]";
    }

    public record InvalidName(string Value)
    {
        public override string ToString() => Value;
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories (discovered by FsCheck via Arbitrary attr) ──

    public static Arbitrary<ValidName> ArbitraryValidName()
    {
        var gen = from len in Gen.Choose(1, 128)
                  from chars in Gen.ArrayOf(Gen.Elements(ValidChars), len)
                  select new ValidName(new string(chars));
        return gen.ToArbitrary();
    }

    public static Arbitrary<WhitespaceOnly> ArbitraryWhitespaceOnly()
    {
        var gen = from len in Gen.Choose(1, 64)
                  from chars in Gen.ArrayOf(Gen.Elements(WhitespaceChars), len)
                  select new WhitespaceOnly(new string(chars));
        return gen.ToArbitrary();
    }

    public static Arbitrary<InvalidName> ArbitraryInvalidName()
    {
        // Printable ASCII characters outside the valid set
        var invalidChars = Enumerable.Range(0x21, 0x7E - 0x21 + 1)
            .Select(i => (char)i)
            .Where(c => !ValidChars.Contains(c))
            .ToArray();

        var gen = from len in Gen.Choose(1, 127)
                  from validPart in Gen.ArrayOf(Gen.Elements(ValidChars), len)
                  from invalidChar in Gen.Elements(invalidChars)
                  from insertPos in Gen.Choose(0, len)
                  let combined = validPart.Take(insertPos)
                      .Append(invalidChar)
                      .Concat(validPart.Skip(insertPos))
                      .Take(128)
                      .ToArray()
                  select new InvalidName(new string(combined));
        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    /// <summary>
    /// Escapes a string for use inside a TOML basic string (double-quoted).
    /// </summary>
    private static string EscapeForToml(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    // ─── Property 1: TOML parsing round-trip ─────────────────────────────

    /// <summary>
    /// Property 1: For any valid project name (1–128 chars from [a-zA-Z0-9\-_\.]),
    /// writing it into a TOML string and parsing it should produce a ProjectConfiguration
    /// whose Name equals the original string.
    /// **Validates: Requirements 10.2, 12.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool TomlRoundTrip_ValidNameIsPreserved(ValidName name)
    {
        // Feature: project-based-build-model, Property 1: TOML parsing round-trip
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, $"name = \"{name.Value}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            return result is not null
                && result.Name == name.Value
                && !diagnostics.HasErrors;
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Property 2: Whitespace-only names are rejected ──────────────────

    /// <summary>
    /// Property 2: For any string composed entirely of whitespace characters,
    /// parsing a mist.toml containing that string as the name value should fail
    /// with a MIST_PROJECT_EMPTY_NAME diagnostic.
    /// **Validates: Requirements 10.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool WhitespaceOnlyNames_AreRejected(WhitespaceOnly ws)
    {
        // Feature: project-based-build-model, Property 2: Whitespace-only names are rejected
        var projectDir = CreateTempProjectDir();
        try
        {
            var escaped = EscapeForToml(ws.Value);
            WriteToml(projectDir, $"name = \"{escaped}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();
            return result is null
                && diags.Any(d => d.Id.Value == "MIST_PROJECT_EMPTY_NAME");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Property 3: Name character validation ───────────────────────────

    /// <summary>
    /// Property 3a: For any non-empty string of length 1–128 where every character
    /// is in [a-zA-Z0-9\-_\.], ProjectFileParser should accept it.
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool ValidNames_AreAccepted(ValidName name)
    {
        // Feature: project-based-build-model, Property 3: Name character validation
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, $"name = \"{name.Value}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            return result is not null
                && result.Name == name.Value
                && !diagnostics.HasErrors;
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    /// <summary>
    /// Property 3b: For any string with at least one character outside [a-zA-Z0-9\-_\.],
    /// ProjectFileParser should reject it with MIST_PROJECT_INVALID_NAME diagnostic
    /// listing the invalid characters.
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool InvalidNames_AreRejected(InvalidName name)
    {
        // Feature: project-based-build-model, Property 3: Name character validation
        var projectDir = CreateTempProjectDir();
        try
        {
            var escaped = EscapeForToml(name.Value);
            WriteToml(projectDir, $"name = \"{escaped}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();

            // The name contains at least one invalid char, so it should be rejected.
            // It could be MIST_PROJECT_INVALID_NAME (primary case) or
            // MIST_PROJECT_EMPTY_NAME if the string happens to be whitespace-only.
            return result is null
                && diags.Any(d => d.Id.Value == "MIST_PROJECT_INVALID_NAME"
                               || d.Id.Value == "MIST_PROJECT_EMPTY_NAME");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Property 4: Project name propagation ────────────────────────────

    /// <summary>
    /// Property 4: For any valid project name, when CompilerOptions is constructed
    /// with that name as ProjectName, the CompilerDriver should pass that exact name
    /// to the backend's Emit method — never a folder-derived name.
    /// **Validates: Requirements 10.3, 13.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool ProjectNamePropagation_BackendReceivesExactName(ValidName name)
    {
        // Feature: project-based-build-model, Property 4: Project name propagation
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_prop4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a simple .mist source file so the pipeline can proceed
            var sourcePath = Path.Combine(tempDir, "test.mist");
            File.WriteAllText(sourcePath, "let x = 42;");

            // Create a spy backend that captures the projectName argument
            var spy = new SpyBackend();

            var driver = new CompilerDriver(
                new MistLexer(),
                new MistParser(),
                new SemanticModelBuilder(),
                ImmutableArray<IAnalyzer>.Empty,
                ImmutableArray.Create<IBackend>(spy));

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("spy"),
                OutputDirectory: Path.Combine(tempDir, "out"),
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: name.Value,
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            driver.Compile(options);

            // The spy should have captured the exact project name from options
            return spy.CapturedProjectName == name.Value;
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ─── Property 5: Emitter uses project name for .csproj filename ──────

    /// <summary>
    /// Property 5: For any valid project name, when CSharpEmitter.Emit is called
    /// with that name, the generated .csproj file in the output directory should be
    /// named {projectName}.csproj.
    /// **Validates: Requirements 13.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ProjectFileParserPropertyTests) }, MaxTest = 100)]
    public bool EmitterCsprojNaming_FileMatchesProjectName(ValidName name)
    {
        // Feature: project-based-build-model, Property 5: Emitter uses project name for .csproj filename
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_prop5_{Guid.NewGuid():N}");
        try
        {
            // Build a minimal semantic model (empty ASTs are fine for emission)
            var source = new SourceText("test.mist", "let x = 42;");
            var unit = new CompilationUnit(source);
            var compilation = new MistCompilation(ImmutableArray.Create(unit));
            var builder = new SemanticModelBuilder();
            var bag = new DiagnosticBag();
            var model = builder.Build(compilation, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty, null, bag);

            // Emit into the temp directory with the generated project name
            var emitBag = new DiagnosticBag();
            CSharpEmitter.Emit(model, tempDir, name.Value, emitBag);

            // Assert a file named {projectName}.csproj exists
            var expectedPath = Path.Combine(tempDir, $"{name.Value}.csproj");
            return File.Exists(expectedPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ─── Test doubles ────────────────────────────────────────────────────

    /// <summary>
    /// A spy backend that captures the projectName argument passed to Emit.
    /// Used by Property 4 to verify project name propagation.
    /// </summary>
    private sealed class SpyBackend : IBackend
    {
        public string? CapturedProjectName { get; private set; }

        public BackendId Id => new("spy");

        public IAmbientTypeResolver? GetAmbientTypeResolver() => null;

        public BackendResult Emit(
            ISemanticModel model,
            string outputDirectory,
            string binDirectory,
            string projectName,
            DiagnosticBag diagnostics)
        {
            CapturedProjectName = projectName;
            return new BackendResult(Succeeded: true, PrimaryArtifactPath: null, Diagnostics: []);
        }
    }
}
