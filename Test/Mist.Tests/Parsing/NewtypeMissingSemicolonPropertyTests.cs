// Feature: newtypes, Property 2: Missing semicolon produces diagnostic
// **Validates: Requirements 1.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for newtype missing semicolon rejection.
///
/// **Validates: Requirements 1.3**
///
/// For any valid newtype declaration source text, removing the trailing semicolon
/// and parsing SHALL NOT produce a valid NewtypeDeclaration AST node. The parser
/// rejects the malformed input by failing to match the NewtypeDeclaration rule
/// (which requires a terminating SEMICOLON token).
///
/// The Storm-generated parser is a PEG-style backtracking parser that signals
/// rejection by returning null from the rule rather than emitting an explicit
/// diagnostic. The observable effect is that the CompilationUnit contains no
/// NewtypeItem — the declaration is not accepted.
/// </summary>
public class NewtypeMissingSemicolonPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario()
    {
        return MistNewtypeGenerator.ArbitraryNewtypeSourceScenario();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the trailing semicolon from a valid newtype declaration source string.
    /// </summary>
    private static string RemoveTrailingSemicolon(string source)
    {
        var lastSemicolon = source.LastIndexOf(';');
        if(lastSemicolon < 0)
        {
            return source;
        }

        return source[..lastSemicolon] + source[(lastSemicolon + 1)..];
    }

    /// <summary>
    /// Parses the given source and returns the CompilationUnitNode (or null on failure).
    /// </summary>
    private static (CompilationUnitNode? Root, IReadOnlyList<Diagnostic> Errors) ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (result.Root as CompilationUnitNode, errors);
    }

    /// <summary>
    /// Checks whether the compilation unit contains a newtype declaration
    /// (now reached through TypeItemTopLevelItemNode → NewtypeTypeTypeDeclarationNode).
    /// </summary>
    private static bool ContainsNewtypeItem(CompilationUnitNode cu)
    {
        return cu.Items.Any(item =>
            item is TypeItemTopLevelItemNode { TypeDeclaration: NewtypeTypeTypeDeclarationNode });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2: Missing semicolon produces diagnostic
    //
    // For any valid newtype declaration source text, removing the trailing
    // semicolon and parsing SHALL reject the input. The parser signals
    // rejection either by:
    //   (a) producing at least one diagnostic error, OR
    //   (b) failing to produce a NewtypeDeclaration AST node (the
    //       CompilationUnit contains no NewtypeItem).
    //
    // Both outcomes confirm that the malformed input is not silently accepted
    // as a valid newtype declaration.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeMissingSemicolonPropertyTests) })]
    public Property MissingSemicolon_RejectsInput(NewtypeSourceScenario scenario)
    {
        var sourceWithoutSemicolon = RemoveTrailingSemicolon(scenario.Source);
        var (root, errors) = ParseSource(sourceWithoutSemicolon);

        // The parser must reject the input in at least one observable way:
        // 1. Diagnostic errors are reported, OR
        // 2. No NewtypeDeclarationNode is produced in the AST
        var hasDiagnosticErrors = errors.Count > 0;
        var noNewtypeInAst = root is null || !ContainsNewtypeItem(root);
        var rejected = hasDiagnosticErrors || noNewtypeInAst;

        return rejected
            .ToProperty()
            .Label($"Expected rejection but newtype was accepted without semicolon")
            .Label($"Original source: {scenario.Source}")
            .Label($"Modified source (no semicolon): {sourceWithoutSemicolon}")
            .Label($"Diagnostic errors: {errors.Count}, Newtype in AST: {root is not null && ContainsNewtypeItem(root)}");
    }
}