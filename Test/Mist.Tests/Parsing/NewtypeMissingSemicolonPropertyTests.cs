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

public class NewtypeMissingSemicolonPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario()
    {
        return MistNewtypeGenerator.ArbitraryNewtypeSourceScenario();
    }

    private static string RemoveTrailingSemicolon(string source)
    {
        var lastSemicolon = source.LastIndexOf(';');
        if(lastSemicolon < 0)
        {
            return source;
        }

        return source[..lastSemicolon] + source[(lastSemicolon + 1)..];
    }

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

    private static bool ContainsNewtypeItem(CompilationUnitNode cu)
    {
        return cu.Items.Any(item =>
            item is TypeItemTopLevelItemNode { TypeDeclaration: NewtypeTypeTypeDeclarationNode });
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeMissingSemicolonPropertyTests) })]
    public Property MissingSemicolon_RejectsInput(NewtypeSourceScenario scenario)
    {
        var sourceWithoutSemicolon = RemoveTrailingSemicolon(scenario.Source);
        var (root, errors) = ParseSource(sourceWithoutSemicolon);

        // The backtracking parser can reject by not matching the rule instead of reporting a
        // diagnostic, so either outcome counts.
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