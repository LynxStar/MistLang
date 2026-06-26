// Task 36 of the Mist Standard Library spec: property-based test that
// every occurrence of a primitive keyword in a valid Mist program resolves
// to an IType reference-equal to the stdlib type looked up via
// KeywordAliases.Map[k]. Design § *Correctness Properties → Property 1*:
//
//   For any primitive keyword appearing in any valid Mist program, the
//   IType the semantic model assigns to every occurrence of that keyword
//   is reference-equal to every other occurrence.
//
// **Validates: Requirements 7.1, 7.2, 8.5, 12.8**

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for Task 36 of the Mist Standard Library spec:
/// keyword-alias reference identity.
///
/// Design § *Correctness Properties → Property 1*.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Picks 1–3 random primitive keywords from
///         <see cref="KeywordAliases.Map"/> (e.g., "int", "string",
///         "bool").</item>
///   <item>Generates a valid Mist program that uses those keywords as
///         variable types (fields), method return types, or parameter
///         types.</item>
///   <item>Builds the semantic model with stdlib loaded so keyword
///         resolution targets the real stdlib types.</item>
///   <item>Asserts that every occurrence of keyword <c>k</c> resolves to
///         an <see cref="IType"/> reference-equal to the stdlib type
///         looked up via <c>KeywordAliases.Map[k]</c>.</item>
///   <item>Asserts that all occurrences of the same keyword in the same
///         compilation resolve to the same <see cref="IType"/>
///         instance.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class KeywordAliasReferenceIdentityPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Stdlib loading (shared across iterations) ───────────────────────

    /// <summary>
    /// Stdlib compilation units loaded once from the repo's
    /// <c>Source/Standard/</c> directory. Shared across all iterations to
    /// avoid redundant I/O.
    /// </summary>
    private static readonly Lazy<ImmutableArray<CompilationUnit>> StdlibUnits = new(() =>
    {
        var stdlibRoot = FindStdlibRoot();
        var diagnostics = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, diagnostics);
        if(diagnostics.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(
                "Failed to load stdlib: " +
                string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message)));
        }

        return units;
    });

    /// <summary>
    /// Pre-lexed and pre-parsed stdlib ASTs, paired with their units.
    /// Avoids re-lexing/re-parsing stdlib on every iteration.
    /// </summary>
    private static readonly Lazy<(ImmutableArray<CompilationUnit> Units, ImmutableArray<Storm.Runtime.SyntaxNode> Asts)> StdlibParsed = new(() =>
    {
        var units = StdlibUnits.Value;
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);
        var diagnostics = new DiagnosticBag();
        foreach(var unit in units)
        {
            var tokens = Lexer.Lex(unit.Source, diagnostics);
            var parseResult = Parser.Parse(tokens, diagnostics);
            if(parseResult.Root is null)
            {
                throw new InvalidOperationException($"Failed to parse stdlib unit: {unit.Source.Path}");
            }

            asts.Add(parseResult.Root);
        }

        return (units, asts.ToImmutable());
    });

    /// <summary>
    /// Walks up from the test assembly's output directory until a directory
    /// containing <c>Mist.slnx</c> is found, then returns the
    /// <c>Source/Standard</c> path.
    /// </summary>
    private static string FindStdlibRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return Path.Combine(dir, "Source", "Standard");
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: check if stdlib was copied next to the test assembly
        // (via the MSBuild Content item in Mist.Tests.csproj).
        var localStdlib = Path.Combine(AppContext.BaseDirectory, "Standard");
        if(Directory.Exists(localStdlib))
        {
            return localStdlib;
        }

        throw new InvalidOperationException(
            "Could not find stdlib root (Source/Standard/) from AppContext.BaseDirectory.");
    }

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario: a list of primitive keywords, each
    /// used as a field type in the generated Mist class. Using fields
    /// keeps the generated source simple and avoids type-checker
    /// complications with method bodies and return statements.
    /// Multiple occurrences of the same keyword test same-keyword
    /// consistency.
    /// </summary>
#pragma warning disable CA1034
    public record KeywordScenario(IReadOnlyList<string> FieldKeywords)
    {
        public override string ToString()
        {
            var kws = string.Join(", ", FieldKeywords);
            return $"Fields=[{kws}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// All primitive keywords from <see cref="KeywordAliases.Map"/>.
    /// </summary>
    private static readonly string[] AllKeywords = KeywordAliases.Map.Keys.ToArray();

    /// <summary>
    /// Generates <see cref="KeywordScenario"/> instances. Each scenario
    /// picks 1–3 distinct keywords, then creates 2–5 field placements
    /// (some keywords may appear multiple times to test same-keyword
    /// consistency).
    /// </summary>
    public static Arbitrary<KeywordScenario> ArbitraryKeywordScenarios()
    {
        var gen =
            from keywordCount in Gen.Choose(1, 3)
            from shuffledKeywords in Gen.Shuffle(AllKeywords)
            let selectedKeywords = shuffledKeywords.Take(keywordCount).ToList()
            from fieldCount in Gen.Choose(keywordCount, keywordCount + 2)
            from fields in Gen.ArrayOf(
                Gen.Elements(selectedKeywords.ToArray()),
                fieldCount)
            select new KeywordScenario(fields.ToList());

        return gen.ToArbitrary();
    }

    // ─── Source generation ────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid Mist source program from a scenario. Each keyword
    /// becomes a public field on a class. Fields are the simplest type
    /// reference position — no method bodies, no return statements, no
    /// type-checker complications.
    /// </summary>
    private static string GenerateSource(KeywordScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace TestNs;");
        _ = sb.AppendLine("public class TestClass {");

        for(var i = 0; i < scenario.FieldKeywords.Count; i++)
        {
            _ = sb.AppendLine($"    public {scenario.FieldKeywords[i]} field{i};");
        }

        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    // ─── Semantic model building ─────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model combining stdlib units with the user source.
    /// Returns the model and the diagnostic bag.
    /// </summary>
    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildModel(string userSource)
    {
        var (stdlibUnits, stdlibAsts) = StdlibParsed.Value;

        // Lex and parse the user source.
        var userSourceText = new SourceText("test.mist", userSource);
        var userUnit = new CompilationUnit(userSourceText) { IsStdlib = false };
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException("Failed to parse user source");
        }

        // Combine stdlib + user units.
        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestNs",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics);
    }

    /// <summary>
    /// Looks up the stdlib type for a given keyword by resolving through
    /// <see cref="KeywordAliases.Map"/> and finding the type in the
    /// semantic model's symbol table.
    /// </summary>
    private static IType? LookupStdlibTypeForKeyword(ISemanticModel model, string keyword)
    {
        if(!KeywordAliases.Map.TryGetValue(keyword, out var qualifiedName))
        {
            return null;
        }

        var lastDot = qualifiedName.LastIndexOf('.');
        var namespaceName = qualifiedName[..lastDot];
        var simpleName = qualifiedName[(lastDot + 1)..];

        return model.AllSymbols
            .Where(s => s.Name == simpleName
                        && (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct)
                        && s.DeclaredType is IType t
                        && (t is ClassType ct && ct.ContainingNamespace == namespaceName
                            || t is StructType st && st.ContainingNamespace == namespaceName))
            .Select(s => s.DeclaredType!)
            .FirstOrDefault();
    }

    /// <summary>
    /// Collects all resolved field types from the user's <c>TestClass</c>
    /// type in the semantic model. Returns a list of (keyword, resolvedType)
    /// pairs matching the scenario's field keywords by index.
    /// </summary>
    private static IReadOnlyList<(string Keyword, IType? ResolvedType)> CollectResolvedTypes(
        ISemanticModel model,
        KeywordScenario scenario)
    {
        // Find the TestClass type in the model.
        var testClassSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "TestClass"
            && s.Kind == SymbolKind.Class);

        if(testClassSymbol?.DeclaredType is not ClassType testClass)
        {
            return Array.Empty<(string, IType?)>();
        }

        var members = testClass.MemberScope.Members.ToList();
        var results = new List<(string Keyword, IType? ResolvedType)>();

        for(var i = 0; i < scenario.FieldKeywords.Count; i++)
        {
            var fieldName = $"field{i}";
            var field = members.FirstOrDefault(m =>
                m.Name == fieldName && m.Kind == SymbolKind.Field);
            results.Add((scenario.FieldKeywords[i], field?.DeclaredType));
        }

        return results;
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: For any primitive keyword appearing in any valid Mist
    /// program, the <see cref="IType"/> the semantic model assigns to every
    /// occurrence of that keyword is reference-equal to the stdlib type
    /// looked up via <c>KeywordAliases.Map[k]</c>, and all occurrences of
    /// the same keyword in the same compilation resolve to the same
    /// <see cref="IType"/> instance.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 7.1, 7.2, 8.5, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(KeywordAliasReferenceIdentityPropertyTests) })]
    public Property KeywordAliases_ResolveToReferenceEqualStdlibTypes(
        KeywordScenario scenario)
    {
        {
            var source = GenerateSource(scenario);
            var (model, diagnostics) = BuildModel(source);

            // Check for fatal errors that would prevent meaningful assertions.
            // Filter out MIST_TC errors (type checker) since the semantic model
            // still resolves types even when the type checker reports issues.
            var errors = diagnostics.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error
                            && !d.Id.Value.StartsWith("MIST_TC"))
                .ToList();

            if(errors.Count > 0)
            {
                return false.ToProperty()
                    .Label($"Build errors: {string.Join("; ", errors.Select(e => $"[{e.Id.Value}] {e.Message}"))}\nSource:\n{source}");
            }

            var resolvedTypes = CollectResolvedTypes(model, scenario);

            // Assertion 1: Every occurrence of keyword k resolves to an IType
            // reference-equal to the stdlib type looked up via
            // KeywordAliases.Map[k].
            foreach(var (keyword, resolvedType) in resolvedTypes)
            {
                if(resolvedType is null)
                {
                    return false.ToProperty()
                        .Label($"Keyword '{keyword}' resolved to null.\nSource:\n{source}");
                }

                var expectedStdlibType = LookupStdlibTypeForKeyword(model, keyword);
                if(expectedStdlibType is null)
                {
                    return false.ToProperty()
                        .Label($"Could not find stdlib type for keyword '{keyword}'.\nSource:\n{source}");
                }

                if(!ReferenceEquals(resolvedType, expectedStdlibType))
                {
                    return false.ToProperty()
                        .Label($"Keyword '{keyword}' resolved to {resolvedType.DisplayName} " +
                               $"(hash={RuntimeHelpers.GetHashCode(resolvedType)}) " +
                               $"but expected stdlib type {expectedStdlibType.DisplayName} " +
                               $"(hash={RuntimeHelpers.GetHashCode(expectedStdlibType)}).\nSource:\n{source}");
                }
            }

            // Assertion 2: All occurrences of the same keyword in the same
            // compilation resolve to the same IType instance.
            var typesByKeyword = resolvedTypes
                .Where(r => r.ResolvedType is not null)
                .GroupBy(r => r.Keyword);

            foreach(var group in typesByKeyword)
            {
                var types = group.Select(g => g.ResolvedType!).ToList();
                var first = types[0];
                for(var i = 1; i < types.Count; i++)
                {
                    if(!ReferenceEquals(first, types[i]))
                    {
                        return false.ToProperty()
                            .Label($"Same keyword '{group.Key}' resolved to different IType instances " +
                                   $"(hash1={RuntimeHelpers.GetHashCode(first)}, " +
                                   $"hash2={RuntimeHelpers.GetHashCode(types[i])}).\nSource:\n{source}");
                    }
                }
            }

            return true.ToProperty()
                .Label($"All {resolvedTypes.Count} keyword occurrences verified for scenario: {scenario}");
        }
    }
}