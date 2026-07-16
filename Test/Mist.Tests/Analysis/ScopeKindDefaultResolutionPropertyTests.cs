// Feature: full-accessibility-model, Property 4: Default accessibility follows the enclosing scope kind
//
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that default accessibility follows the enclosing scope kind.
///
/// Property 4: For any declaration that has no explicit access modifier, the resolved
/// Accessibility equals the scope-kind default of its immediately enclosing scope:
/// Internal for namespace/compilation (global) scope, Private for class or struct scope,
/// Public for interface or enum scope, and Private for a nested type.
///
/// Exercises the full classic pipeline (Lex -> Parse -> Semantic Model construction)
/// in-process with no process spawn. Assertions inspect the resolved Accessibility
/// on the built model's symbols.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**
/// </summary>
public class ScopeKindDefaultResolutionPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Scenario type ───────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scope-kind default resolution scenario: a Mist source program,
    /// the name of the declaration to inspect, and its expected resolved accessibility.
    /// </summary>
    public sealed record ScopeKindDefaultScenario(
        string Source,
        string TargetSymbolName,
        SymbolKind TargetSymbolKind,
        Accessibility ExpectedAccessibility,
        string Description)
    {
        public override string ToString()
        {
            return $"[{Description}] {TargetSymbolName} -> {ExpectedAccessibility}";
        }
    }

#pragma warning restore CA1034

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel? BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return model;
    }

    private static ISymbol? FindSymbol(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols.FirstOrDefault(s => s.Name == name && s.Kind == kind);
    }

    // ─── Scenario data ───────────────────────────────────────────────────

    // Each scenario group represents one row of the scope-kind default table.

    /// <summary>
    /// Top-level types (namespace/compilation scope) -> Internal (Req 5.1, 5.2).
    /// </summary>
    private static readonly string[] TopLevelTypeNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static Gen<ScopeKindDefaultScenario> GenTopLevelClass()
    {
        return from name in Gen.Elements(TopLevelTypeNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
class {name} {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Class,
                   ExpectedAccessibility: Accessibility.Internal,
                   Description: $"Top-level class '{name}' defaults to Internal");
    }

    private static Gen<ScopeKindDefaultScenario> GenTopLevelStruct()
    {
        return from name in Gen.Elements(TopLevelTypeNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
struct {name} {{
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Struct,
                   ExpectedAccessibility: Accessibility.Internal,
                   Description: $"Top-level struct '{name}' defaults to Internal");
    }

    /// <summary>
    /// Class members (class scope) -> Private (Req 5.3).
    /// </summary>
    private static readonly string[] MemberNames =
        ["value", "count", "data", "flag", "item"];

    private static Gen<ScopeKindDefaultScenario> GenClassFieldMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
class Container {{
    int {name};
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Field,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Class field '{name}' defaults to Private");
    }

    private static Gen<ScopeKindDefaultScenario> GenClassMethodMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
class Container {{
    int {name}() {{
        return 0;
    }}
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Class method '{name}' defaults to Private");
    }

    /// <summary>
    /// Struct members (struct scope) -> Private (Req 5.3).
    /// This validates the struct-member flip from the old public default to private.
    /// </summary>
    private static Gen<ScopeKindDefaultScenario> GenStructFieldMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
struct Container {{
    int {name};
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Field,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Struct field '{name}' defaults to Private");
    }

    private static Gen<ScopeKindDefaultScenario> GenStructMethodMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
struct Container {{
    int {name}() {{
        return 0;
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Struct method '{name}' defaults to Private");
    }

    /// <summary>
    /// Interface members (interface/abstract scope) -> Public (Req 5.5).
    /// </summary>
    private static Gen<ScopeKindDefaultScenario> GenInterfaceMember()
    {
        return from name in Gen.Elements(MemberNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
abstract IContract {{
    int {name}();
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Method,
                   ExpectedAccessibility: Accessibility.Public,
                   Description: $"Interface method '{name}' defaults to Public");
    }

    /// <summary>
    /// Nested types inside a class/struct -> Private (Req 5.4).
    /// </summary>
    private static Gen<ScopeKindDefaultScenario> GenNestedClassInClass()
    {
        return from name in Gen.Elements(TopLevelTypeNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
class Outer {{
    class {name} {{
    }}
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Class,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Nested class '{name}' in class defaults to Private");
    }

    private static Gen<ScopeKindDefaultScenario> GenNestedStructInClass()
    {
        return from name in Gen.Elements(TopLevelTypeNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
class Outer {{
    struct {name} {{
    }}
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Struct,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Nested struct '{name}' in class defaults to Private");
    }

    private static Gen<ScopeKindDefaultScenario> GenNestedClassInStruct()
    {
        return from name in Gen.Elements(TopLevelTypeNames)
               select new ScopeKindDefaultScenario(
                   Source: $@"
struct Outer {{
    class {name} {{
    }}
}}
class Program {{
    public static void Main() {{ }}
}}",
                   TargetSymbolName: name,
                   TargetSymbolKind: SymbolKind.Class,
                   ExpectedAccessibility: Accessibility.Private,
                   Description: $"Nested class '{name}' in struct defaults to Private");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ScopeKindDefaultScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTopLevelClass(),
            GenTopLevelStruct(),
            GenClassFieldMember(),
            GenClassMethodMember(),
            GenStructFieldMember(),
            GenStructMethodMember(),
            GenInterfaceMember(),
            GenNestedClassInClass(),
            GenNestedStructInClass(),
            GenNestedClassInStruct());
    }

    public static Arbitrary<ScopeKindDefaultScenario> ArbitraryScopeKindDefaultScenario()
    {
        return Arb.From(GenScenario());
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 4: Default accessibility follows the enclosing scope kind.
    ///
    /// For any declaration that has no explicit access modifier, the resolved
    /// Accessibility value on the symbol matches the scope-kind default table:
    /// - namespace/compilation (global) -> Internal
    /// - class/struct -> Private
    /// - interface (abstract) / enum -> Public
    /// - nested type inside class/struct -> Private
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ScopeKindDefaultResolutionPropertyTests) })]
    public Property DefaultAccessibility_MatchesScopeKindTable(
        ScopeKindDefaultScenario scenario)
    {
        var model = BuildModel(scenario.Source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Failed to build model for: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        var symbol = FindSymbol(model, scenario.TargetSymbolName, scenario.TargetSymbolKind);

        if(symbol is null)
        {
            return false.ToProperty()
                .Label($"Symbol '{scenario.TargetSymbolName}' ({scenario.TargetSymbolKind}) not found in model. " +
                       $"Scenario: {scenario.Description}\n" +
                       $"Available symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        var matches = symbol.Accessibility == scenario.ExpectedAccessibility;

        return matches.ToProperty()
            .Label($"Expected {scenario.ExpectedAccessibility}, got {symbol.Accessibility}. " +
                   $"Scenario: {scenario.Description}");
    }
}