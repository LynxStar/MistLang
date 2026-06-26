// Feature: global-usings, Property 5: Global using resolution equivalence
// **Validates: Requirements 4.1**
//
// For any valid namespace N containing a type T, if N is declared as a global using,
// then referencing T by simple name in any source file within that project SHALL resolve
// successfully — equivalent to having `using N;` at the top of the file.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based test verifying that global usings resolve types by simple name,
/// equivalent to an explicit <c>using</c> declaration.
/// Uses in-process <see cref="CompilerDriver.Compile"/> with <c>EmitToDisk = false</c>.
/// No external process is spawned per iteration.
///
/// **Validates: Requirements 4.1**
/// </summary>
public sealed class GlobalUsingResolutionEquivalencePropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(), new MistParser(), new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string scratchDir;

    public GlobalUsingResolutionEquivalencePropertyTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist_pbt_gu_resolve_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a valid identifier: starts with a letter (a-z, A-Z),
    /// followed by 1–6 alphanumeric/underscore characters.
    /// Avoids starting with underscore to prevent conflicts with Mist keywords.
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(1, 6).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a valid namespace name: 1–3 dot-separated identifier segments.
    /// Ensures the namespace name does not collide with Mist keywords or stdlib namespaces.
    /// </summary>
    private static Gen<string> GenNamespaceName()
    {
        return Gen.Choose(1, 3).SelectMany(segCount =>
            Gen.ArrayOf(GenIdentifier(), segCount)
                .Select(segs => string.Join(".", segs)));
    }

    /// <summary>
    /// Generates a valid type name (PascalCase-ish): starts with uppercase letter,
    /// followed by 2–8 alphanumeric characters.
    /// </summary>
    private static Gen<string> GenTypeName()
    {
        var startChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(2, 8).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a (namespace, type) pair for testing global using resolution.
    /// </summary>
    private static Gen<(string Namespace, string TypeName)> GenNamespaceTypePair()
    {
        return GenNamespaceName().SelectMany(ns =>
            GenTypeName().Select(typeName => (ns, typeName)));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> MistKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "let", "var", "if", "else", "while", "for", "return", "class", "struct",
        "interface", "enum", "namespace", "using", "public", "private",
        "protected", "internal", "static", "readonly", "override", "virtual",
        "abstract", "sealed", "new", "true", "false", "null", "void", "int",
        "float", "double", "bool", "string", "char", "byte", "long", "short",
        "object", "type", "owned", "ref", "out", "in", "is", "as", "this",
        "base", "where", "extends", "implements", "import", "export", "module",
        "match", "case", "default", "break", "continue", "throw", "try", "catch",
        "finally", "async", "await", "yield", "from", "select", "get", "set",
        "init", "required", "operator", "implicit", "explicit", "checked",
        "unchecked", "sizeof", "typeof", "nameof", "stackalloc", "fixed",
        "volatile", "extern", "unsafe", "params", "const", "event", "delegate",
        "Standard", "System", "Console", "Program", "Main", "Consumer",
        "TestProject", "Object", "Type", "Array", "List", "Dictionary",
        "Task", "Action", "Func", "Exception", "Math", "Convert",
        "uint", "ulong", "ushort", "sbyte", "decimal", "regex",
    };

    /// <summary>
    /// Checks if a name (or any segment of a dotted namespace) is a Mist keyword.
    /// </summary>
    private static bool ContainsKeyword(string name)
    {
        var segments = name.Split('.');
        return segments.Any(MistKeywords.Contains);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5: Global using resolution equivalence.
    /// For any valid namespace N containing a type T, if N is declared as a global using,
    /// then referencing T by simple name in any source file within that project SHALL
    /// resolve successfully (no MIST_UNDEFINED_TYPE error for T).
    /// </summary>
    [Fact]
    public void P5_GlobalUsingResolutionEquivalence_TypeResolvesBySimpleName()
    {
        var gen = GenNamespaceTypePair();
        // Use a fixed size to generate more samples, ensuring we always get 100+ valid ones
        // after filtering. The generator produces short identifiers that rarely collide
        // with keywords, but we generate extra headroom to be safe.
        var samples = gen.Sample(200).ToList();

        // Filter out samples that contain keywords or reserved names to avoid parse issues
        var validSamples = samples
            .Where(s => !ContainsKeyword(s.Namespace) && !ContainsKeyword(s.TypeName))
            .Take(100)
            .ToList();

        Assert.True(validSamples.Count >= 50,
            $"Expected at least 50 valid samples but got {validSamples.Count}");

        for(var idx = 0; idx < validSamples.Count; idx++)
        {
            var (ns, typeName) = validSamples[idx];

            // Create a unique temp directory for this iteration
            var iterDir = Path.Combine(scratchDir, $"iter_{idx}");
            _ = Directory.CreateDirectory(iterDir);

            try
            {
                // File 1: defines namespace with a type
                var defSource = $"namespace {ns};\n\npublic class {typeName}\n{{\n}}\n";
                var defPath = Path.Combine(iterDir, "Definition.mist");
                File.WriteAllText(defPath, defSource);

                // File 2: uses the type by simple name without explicit using
                // (relies on global using for the namespace)
                var useSource = $"namespace Consumer;\n\npublic class Use{typeName}\n{{\n    public {typeName} GetValue()\n    {{\n        return new {typeName}();\n    }}\n}}\n";
                var usePath = Path.Combine(iterDir, "Consumer.mist");
                File.WriteAllText(usePath, useSource);

                var options = new CompilerOptions(
                    ProjectFolder: iterDir,
                    SourcePaths: ImmutableArray.Create(defPath, usePath),
                    Backend: new BackendId("csharp"),
                    OutputDirectory: Path.Combine(iterDir, "out"),
                    BinDirectory: Path.Combine(iterDir, "bin"),
                    ProjectName: "TestProject",
                    NamespaceRoot: "TestProject",
                    EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                    EmitToDisk: false,
                    GlobalUsings: ImmutableArray.Create(ns));

                var result = SharedDriver.Compile(options);

                // Assert: no MIST_UNDEFINED_TYPE error for the type name
                var undefinedTypeErrors = result.Diagnostics
                    .Where(d => d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains(typeName))
                    .ToList();

                if(undefinedTypeErrors.Count > 0)
                {
                    var allDiags = string.Join("\n  ",
                        result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
                    throw new Exception(
                        $"Property failed on sample {idx}/{validSamples.Count}: " +
                        $"namespace='{ns}', type='{typeName}'\n" +
                        $"Expected type '{typeName}' to resolve via global using '{ns}', " +
                        $"but got MIST_UNDEFINED_TYPE.\n" +
                        $"All diagnostics:\n  {allDiags}");
                }
            }
            finally
            {
                // Cleanup per-iteration directory
                try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}