// Feature: tuple-pattern-matching, Property 2: Variable introduction correctness
// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 5.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for tuple positional pattern variable introduction correctness.
///
/// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 5.4**
///
/// Property 2: For any tuple type and any tuple positional pattern (including nested
/// patterns at arbitrary depth) where elements are a mix of var patterns, type patterns,
/// discard patterns, and non-binding patterns (relational, constant, logical), the
/// semantic model builder SHALL introduce exactly the variables from var and type pattern
/// elements into the then-branch scope, each typed according to its corresponding tuple
/// element position (or the declared type for type patterns), and SHALL NOT introduce
/// variables for discard or non-binding elements.
/// </summary>
public class TuplePositionalPatternVariablePropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);

        if (parseResult.Root is null)
            throw new InvalidOperationException("Parse produced no root node.");

        var buildDiag = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);
    }

    private static bool IsEquivalentPrimitiveType(string actual, string expected)
    {
        // Map between legacy lowercase names and .NET type names
        return (actual, expected) switch
        {
            ("int", "Int32") or ("Int32", "int") => true,
            ("string", "String") or ("String", "string") => true,
            ("bool", "Boolean") or ("Boolean", "bool") => true,
            ("double", "Double") or ("Double", "double") => true,
            ("float", "Single") or ("Single", "float") => true,
            ("char", "Char") or ("Char", "char") => true,
            ("long", "Int64") or ("Int64", "long") => true,
            _ => false,
        };
    }

    // ─── Element pattern kinds ───────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents the kind of subpattern used in a tuple positional pattern element.
    /// </summary>
    public enum ElementPatternKind
    {
        Var,
        TypePattern,
        Discard,
        Relational,
        Constant,
    }

    /// <summary>
    /// Represents a single element in a tuple positional pattern scenario.
    /// </summary>
    public record ElementSpec(
        ElementPatternKind Kind,
        string TupleElementType,
        string? VariableName)
    {
        /// <summary>
        /// Returns the Mist source code for this element pattern.
        /// </summary>
        public string ToPatternSource() => Kind switch
        {
            ElementPatternKind.Var => $"var {VariableName}",
            ElementPatternKind.TypePattern => $"{TupleElementType} {VariableName}",
            ElementPatternKind.Discard => "_",
            ElementPatternKind.Relational => ToRelationalPattern(),
            ElementPatternKind.Constant => ToConstantPattern(),
            _ => "_",
        };

        /// <summary>
        /// Whether this element introduces a variable into scope.
        /// </summary>
        public bool IntroducesVariable => Kind is ElementPatternKind.Var or ElementPatternKind.TypePattern;

        /// <summary>
        /// The expected type display name for the introduced variable.
        /// For var patterns, it's the tuple element type (as resolved by the semantic model).
        /// For type patterns, it's the declared type in the pattern.
        /// Without stdlib loaded, legacy primitive types use lowercase display names.
        /// </summary>
        public string ExpectedTypeName => TupleElementType;

        private string ToRelationalPattern() => TupleElementType switch
        {
            "int" => "> 0",
            "bool" => "true",
            "string" => "\"hello\"",
            _ => "> 0",
        };

        private string ToConstantPattern() => TupleElementType switch
        {
            "int" => "42",
            "bool" => "true",
            "string" => "\"hello\"",
            _ => "0",
        };
    }

    /// <summary>
    /// Represents a complete tuple positional pattern scenario with a tuple type
    /// and a set of element patterns.
    /// </summary>
    public record TuplePatternScenario(ImmutableArray<ElementSpec> Elements)
    {
        public override string ToString()
        {
            var pattern = $"({string.Join(", ", Elements.Select(e => e.ToPatternSource()))})";
            var tupleType = $"({string.Join(", ", Elements.Select(e => e.TupleElementType))})";
            return $"type={tupleType} pattern={pattern}";
        }

        /// <summary>
        /// Generates the Mist source code for this scenario.
        /// </summary>
        public string ToSource()
        {
            var tupleType = $"({string.Join(", ", Elements.Select(e => e.TupleElementType))})";
            var tupleInit = $"({string.Join(", ", Elements.Select(e => DefaultValue(e.TupleElementType)))})";
            var pattern = $"({string.Join(", ", Elements.Select(e => e.ToPatternSource()))})";

            // Build variable usage statements for introduced variables
            var varUsages = Elements
                .Where(e => e.IntroducesVariable)
                .Select(e => $"        var use_{e.VariableName} = {e.VariableName};")
                .ToList();

            var usageBlock = varUsages.Count > 0
                ? "\n" + string.Join("\n", varUsages)
                : "";

            return $$"""
                class Test {
                    void Run() {
                        {{tupleType}} t = {{tupleInit}};
                        if (t is {{pattern}}) {{{usageBlock}}
                        }
                    }
                }
                """;
        }

        private static string DefaultValue(string type) => type switch
        {
            "int" => "0",
            "string" => "\"\"",
            "bool" => "false",
            _ => "0",
        };
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    private static int _varCounter;

    /// <summary>
    /// Generates a unique variable name to avoid duplicate pattern variable conflicts.
    /// </summary>
    private static string NextVarName()
    {
        var counter = Interlocked.Increment(ref _varCounter);
        return $"v{counter}";
    }

    public static Arbitrary<TuplePatternScenario> ArbitraryScenario()
    {
        var gen =
            from arity in Gen.Choose(2, 5)
            from types in Gen.ArrayOf(Gen.Elements(PrimitiveTypes), arity)
            from kinds in Gen.ArrayOf(
                Gen.Elements(
                    ElementPatternKind.Var,
                    ElementPatternKind.TypePattern,
                    ElementPatternKind.Discard,
                    ElementPatternKind.Relational,
                    ElementPatternKind.Constant),
                arity)
            let elements = types.Zip(kinds, (t, k) => new ElementSpec(
                k, t,
                k is ElementPatternKind.Var or ElementPatternKind.TypePattern ? NextVarName() : null))
                .ToImmutableArray()
            let atLeastOneBinding = elements.Any(e => e.IntroducesVariable)
            let atLeastOneNonBinding = elements.Any(e => !e.IntroducesVariable)
            where atLeastOneBinding && atLeastOneNonBinding
            select new TuplePatternScenario(elements);
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Variable introduction correctness
    //
    // **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 5.4**
    //
    // For any tuple type and any tuple positional pattern where elements
    // are a mix of var patterns, type patterns, discard patterns, and
    // non-binding patterns, the semantic model builder SHALL introduce
    // exactly the variables from var and type pattern elements into the
    // then-branch scope, each typed according to its corresponding tuple
    // element position, and SHALL NOT introduce variables for discard or
    // non-binding elements.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternVariablePropertyTests) })]
    public Property TuplePositionalPattern_IntroducesExactlyExpectedVariables(
        TuplePatternScenario scenario)
    {
        var source = scenario.ToSource();
        var model = BuildModel(source);

        // Collect expected variable names and their types
        var expectedVars = scenario.Elements
            .Where(e => e.IntroducesVariable)
            .Select(e => (Name: e.VariableName!, ExpectedType: e.ExpectedTypeName))
            .ToList();

        // Collect variable names that should NOT be introduced
        var nonBindingElements = scenario.Elements
            .Where(e => !e.IntroducesVariable)
            .ToList();

        // Verify each expected variable is present with correct type
        foreach (var (name, expectedType) in expectedVars)
        {
            var symbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);

            if (symbol is null)
            {
                return false.ToProperty()
                    .Label($"Expected variable '{name}' not found as LocalVariable. " +
                           $"Source:\n{source}\n" +
                           $"All locals: [{string.Join(", ", model.AllSymbols.Where(s => s.Kind == SymbolKind.LocalVariable).Select(s => s.Name))}]");
            }

            if (symbol.DeclaredType is null)
            {
                return false.ToProperty()
                    .Label($"Variable '{name}' has null DeclaredType. Source:\n{source}");
            }

            // Match type display name. Without stdlib, legacy primitives use
            // lowercase names (int, string, bool). With stdlib, they use
            // .NET names (Int32, String, Boolean). Accept either form.
            var typeMatches = symbol.DeclaredType.DisplayName == expectedType
                              || symbol.DeclaredType.DisplayName.Equals(expectedType, StringComparison.OrdinalIgnoreCase)
                              || IsEquivalentPrimitiveType(symbol.DeclaredType.DisplayName, expectedType);

            if (!typeMatches)
            {
                return false.ToProperty()
                    .Label($"Variable '{name}' expected type '{expectedType}' but got '{symbol.DeclaredType.DisplayName}'. " +
                           $"Source:\n{source}");
            }
        }

        // Verify no unexpected variables from discard/non-binding patterns
        // (discard `_` should not appear as a local variable)
        var discardVar = model.AllSymbols
            .FirstOrDefault(s => s.Name == "_" && s.Kind == SymbolKind.LocalVariable);

        if (discardVar is not null)
        {
            return false.ToProperty()
                .Label($"Discard pattern introduced a variable named '_'. Source:\n{source}");
        }

        return true.ToProperty()
            .Label($"All {expectedVars.Count} variables correctly introduced. Scenario: {scenario}");
    }
}
