// Task 37 of the Mist Standard Library spec: property-based test that
// primitive keyword resolution bypasses all scoping — even when a user
// declares a class/struct with the same simple name as a stdlib type
// (e.g., `class Int32 { }` in a user namespace), the keyword `int`
// always resolves to `Standard.Int32`, never to the user's type.
//
// Design § *Correctness Properties → Property 2*:
//
//   For any user-authored Mist Compilation_Unit U and any primitive
//   keyword k whose mapped stdlib type's simple name is S, even if U
//   declares or imports a type named S in any namespace and regardless
//   of whether `using Standard;` is present, every occurrence of k in U
//   resolves to the stdlib type KeywordAliases.Map[k] — never to any
//   user-declared or imported type named S. Conversely, an occurrence
//   of the unqualified simple name S in U resolves via ordinary
//   name-resolution scoping rules and may resolve to a user-declared S
//   when such a type shadows.
//
// **Validates: Requirements 7.6, 7.7, 7.8, 12.9**

using System.Collections.Immutable;
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
/// Property-based tests for Task 37 of the Mist Standard Library spec:
/// keyword bypasses scoping (adversarial user-declared types).
///
/// Design § <em>Correctness Properties → Property 2</em>.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Picks a random primitive keyword from
///         <see cref="KeywordAliases.Map"/> (e.g., "int").</item>
///   <item>Determines the corresponding stdlib simple name (e.g.,
///         "Int32" for "int").</item>
///   <item>Generates a Mist program with a user-declared class with
///         that simple name in a user namespace.</item>
///   <item>Includes a field typed with the keyword (e.g.,
///         <c>int keywordField;</c>) — this should resolve to
///         <c>Standard.Int32</c>.</item>
///   <item>Builds the semantic model with stdlib loaded so keyword
///         resolution targets the real stdlib types.</item>
///   <item>Asserts that the keyword-typed field resolves to the stdlib
///         type in the <c>Standard</c> namespace, never to the user's
///         type.</item>
///   <item>Asserts that the simple-name-typed field (when present)
///         resolves to the user's type via ordinary scoping.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class KeywordBypassesScopingPropertyTests
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
    /// A single generated scenario: a primitive keyword and the
    /// corresponding stdlib simple name. The test generates a Mist program
    /// where the user declares a class with the stdlib simple name in a
    /// user namespace, then uses the keyword to type a field.
    /// </summary>
#pragma warning disable CA1034
    public record AdversarialScenario(string Keyword, string StdlibSimpleName, string UserNamespace)
    {
        public override string ToString()
        {
            return $"Keyword={Keyword}, StdlibSimpleName={StdlibSimpleName}, UserNs={UserNamespace}";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// All primitive keywords from <see cref="KeywordAliases.Map"/>.
    /// </summary>
    private static readonly string[] AllKeywords = KeywordAliases.Map.Keys.Where(k => k != "void").ToArray();

    /// <summary>
    /// A set of user namespace names to pick from.
    /// </summary>
    private static readonly string[] UserNamespaces =
        ["UserNs", "MyApp", "Game", "Lib", "Data", "Core"];

    /// <summary>
    /// Extracts the simple name from a fully-qualified stdlib type name.
    /// E.g., "Standard.Int32" → "Int32".
    /// </summary>
    private static string GetSimpleName(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot >= 0 ? qualifiedName.Substring(lastDot + 1) : qualifiedName;
    }

    /// <summary>
    /// Generates <see cref="AdversarialScenario"/> instances. Each scenario
    /// picks a random primitive keyword and a random user namespace.
    /// </summary>
    public static Arbitrary<AdversarialScenario> ArbitraryAdversarialScenarios()
    {
        var gen =
            from keyword in Gen.Elements(AllKeywords)
            from userNs in Gen.Elements(UserNamespaces)
            let simpleName = GetSimpleName(KeywordAliases.Map[keyword])
            select new AdversarialScenario(keyword, simpleName, userNs);

        return gen.ToArbitrary();
    }

    // ─── Source generation ────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid Mist source program from a scenario. The program
    /// declares a user class with the same simple name as the stdlib type
    /// in a user namespace, then declares a test class with:
    /// <list type="bullet">
    ///   <item>A field typed with the keyword (should resolve to stdlib type)</item>
    ///   <item>A field typed with the simple name (should resolve to user's type
    ///         via ordinary scoping since the user type shadows in the current
    ///         namespace)</item>
    /// </list>
    /// </summary>
    private static string GenerateSource(AdversarialScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine($"namespace {scenario.UserNamespace};");
        _ = sb.AppendLine();
        // User-declared class with the same simple name as the stdlib type.
        _ = sb.AppendLine($"public class {scenario.StdlibSimpleName} {{");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();
        // Test class with fields using both the keyword and the simple name.
        _ = sb.AppendLine("public class TestClass {");
        _ = sb.AppendLine($"    public {scenario.Keyword} keywordField;");
        _ = sb.AppendLine($"    public {scenario.StdlibSimpleName} simpleNameField;");
        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    // ─── Semantic model building ─────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model combining stdlib units with the user source,
    /// using the given user namespace as the namespace root.
    /// </summary>
    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildModelForScenario(
        string userSource, string userNamespace)
    {
        var (stdlibUnits, stdlibAsts) = StdlibParsed.Value;

        var userSourceText = new SourceText("test.mist", userSource);
        var userUnit = new CompilationUnit(userSourceText) { IsStdlib = false };
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException("Failed to parse user source");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: userNamespace,
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
        var namespaceName = qualifiedName.Substring(0, lastDot);
        var simpleName = qualifiedName.Substring(lastDot + 1);

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
    /// Finds the user-declared type with the given simple name in the given
    /// user namespace from the semantic model.
    /// </summary>
    private static IType? LookupUserType(
        ISemanticModel model, string simpleName, string userNamespace)
    {
        return model.AllSymbols
            .Where(s => s.Name == simpleName
                        && s.Kind == SymbolKind.Class
                        && s.DeclaredType is ClassType ct
                        && ct.ContainingNamespace == userNamespace)
            .Select(s => s.DeclaredType!)
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds the resolved type of a field by name on the TestClass type.
    /// </summary>
    private static IType? GetFieldType(ISemanticModel model, string fieldName)
    {
        var testClassSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "TestClass"
            && s.Kind == SymbolKind.Class);

        if(testClassSymbol?.DeclaredType is not ClassType testClass)
        {
            return null;
        }

        var field = testClass.MemberScope.Members
            .FirstOrDefault(m => m.Name == fieldName && m.Kind == SymbolKind.Field);

        return field?.DeclaredType;
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any user-authored Mist Compilation_Unit that
    /// declares a type with the same simple name as a stdlib type (e.g.,
    /// <c>class Int32 { }</c> in a user namespace), every occurrence of
    /// the primitive keyword (e.g., <c>int</c>) resolves to the stdlib
    /// type (<c>Standard.Int32</c>), never to the user's type. The
    /// unqualified simple name (<c>Int32</c>) follows ordinary scoping
    /// and resolves to the user's type when it shadows.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 7.6, 7.7, 7.8, 12.9**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(KeywordBypassesScopingPropertyTests) })]
    public Property KeywordBypassesScoping_AdversarialUserDeclaredType(
        AdversarialScenario scenario)
    {
        {
            var source = GenerateSource(scenario);
            var (model, diagnostics) = BuildModelForScenario(source, scenario.UserNamespace);

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

            // ── Assertion 1: Keyword bypasses scoping ────────────────────
            // The field typed with the keyword (e.g., `int keywordField`)
            // must resolve to the stdlib type (Standard.Int32), not the
            // user's type.
            var keywordFieldType = GetFieldType(model, "keywordField");
            if(keywordFieldType is null)
            {
                return false.ToProperty()
                    .Label($"Keyword field 'keywordField' resolved to null.\nSource:\n{source}");
            }

            var expectedStdlibType = LookupStdlibTypeForKeyword(model, scenario.Keyword);
            if(expectedStdlibType is null)
            {
                return false.ToProperty()
                    .Label($"Could not find stdlib type for keyword '{scenario.Keyword}'.\nSource:\n{source}");
            }

            // The keyword-typed field must be the stdlib type.
            var isStdlibType = false;
            if(keywordFieldType is StructType kst
                && kst.ContainingNamespace == "Standard"
                && kst.DisplayName == scenario.StdlibSimpleName)
            {
                isStdlibType = true;
            }
            else if(keywordFieldType is ClassType kct
                     && kct.ContainingNamespace == "Standard"
                     && kct.DisplayName == scenario.StdlibSimpleName)
            {
                isStdlibType = true;
            }

            if(!isStdlibType)
            {
                var actualNs = keywordFieldType switch
                {
                    ClassType ct => ct.ContainingNamespace,
                    StructType st => st.ContainingNamespace,
                    _ => "unknown"
                };
                return false.ToProperty()
                    .Label($"Keyword '{scenario.Keyword}' resolved to " +
                           $"{keywordFieldType.DisplayName} in namespace '{actualNs}' " +
                           $"but expected Standard.{scenario.StdlibSimpleName}.\nSource:\n{source}");
            }

            // The keyword-typed field must NOT be the user's type.
            var userType = LookupUserType(model, scenario.StdlibSimpleName, scenario.UserNamespace);
            if(userType is not null && ReferenceEquals(keywordFieldType, userType))
            {
                return false.ToProperty()
                    .Label($"Keyword '{scenario.Keyword}' resolved to the USER's " +
                           $"{scenario.UserNamespace}.{scenario.StdlibSimpleName} " +
                           $"instead of Standard.{scenario.StdlibSimpleName}.\nSource:\n{source}");
            }

            // ── Assertion 2: Simple name follows ordinary scoping ────────
            // The field typed with the simple name (e.g., `Int32 simpleNameField`)
            // should resolve to the user's type when the user's type shadows
            // in the current namespace scope.
            var simpleNameFieldType = GetFieldType(model, "simpleNameField");
            if(simpleNameFieldType is not null && userType is not null)
            {
                // The simple name should resolve to the user's type (shadowing).
                if(!ReferenceEquals(simpleNameFieldType, userType))
                {
                    var simpleNs = simpleNameFieldType switch
                    {
                        ClassType ct => ct.ContainingNamespace,
                        StructType st => st.ContainingNamespace,
                        _ => "unknown"
                    };
                    return false.ToProperty()
                        .Label($"Simple name '{scenario.StdlibSimpleName}' resolved to " +
                               $"{simpleNameFieldType.DisplayName} in namespace '{simpleNs}' " +
                               $"but expected user's {scenario.UserNamespace}.{scenario.StdlibSimpleName}.\nSource:\n{source}");
                }
            }

            return true.ToProperty()
                .Label($"Keyword '{scenario.Keyword}' correctly resolved to " +
                       $"Standard.{scenario.StdlibSimpleName} (bypassing user's " +
                       $"{scenario.UserNamespace}.{scenario.StdlibSimpleName}). " +
                       $"Scenario: {scenario}");
        }
    }
}