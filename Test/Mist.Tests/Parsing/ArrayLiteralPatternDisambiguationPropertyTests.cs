// Feature: array-literals, Property 4: Pattern-context disambiguation
// **Validates: Requirements 4.1, 4.2, 4.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for pattern-context disambiguation:
/// a scrutinee variable name and a relational pattern value used inside
/// a list pattern in an `is` expression.
/// </summary>
public sealed class PatternDisambiguationScenario
{
    public string Scrutinee { get; }
    public int RelationalValue { get; }
    public bool UseRestPattern { get; }

    public PatternDisambiguationScenario(string scrutinee, int relationalValue, bool useRestPattern)
    {
        Scrutinee = scrutinee;
        RelationalValue = relationalValue;
        UseRestPattern = useRestPattern;
    }

    public override string ToString()
    {
        return $"Scrutinee={Scrutinee}, RelationalValue={RelationalValue}, UseRestPattern={UseRestPattern}";
    }
}

/// <summary>
/// Property-based tests for pattern-context disambiguation.
///
/// Property 4: For any expression `scrutinee` and pattern content inside an
/// `is` expression, parsing `scrutinee is [pattern]` SHALL produce an
/// IsExpression whose pattern is a ListPattern (not an ArrayLiteralExpression).
/// </summary>
public class ArrayLiteralPatternDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    private static readonly string[] ValidScrutinees =
        ["arr", "data", "items", "numbers", "values", "list", "buf", "vec", "xs", "ys"];

    public static Arbitrary<PatternDisambiguationScenario> ArbitraryScenario()
    {
        var gen =
            from nameIdx in Gen.Choose(0, ValidScrutinees.Length - 1)
            from relVal in Gen.Choose(-1000, 1000)
            from useRest in Gen.Elements(true, false)
            select new PatternDisambiguationScenario(ValidScrutinees[nameIdx], relVal, useRest);

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return null;
        }

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Builds a complete Mist source file with a scrutinee declared as an array
    /// and an `is` expression using a list pattern.
    /// Pattern format: `scrutinee is [> N, ..]` or `scrutinee is [> N]`
    /// </summary>
    private static string BuildPatternSource(PatternDisambiguationScenario scenario)
    {
        var pattern = scenario.UseRestPattern
            ? $"> {scenario.RelationalValue}, .."
            : $"> {scenario.RelationalValue}";

        return $"class Test {{ void Run() {{ var {scenario.Scrutinee} = [1, 2, 3]; var result = {scenario.Scrutinee} is [{pattern}]; }} }}";
    }

    /// <summary>
    /// Finds an IsExpressionRelationalNode in the expression tree using depth-first search.
    /// </summary>
    private static IsExpressionRelationalNode? FindIsExpression(Storm.Runtime.SyntaxNode root)
    {
        if(root is IsExpressionRelationalNode isExpr)
        {
            return isExpr;
        }

        foreach(var child in root.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindIsExpression(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Unwraps a PatternNode to its PrimaryPatternNode.
    /// </summary>
    private static PrimaryPatternNode? UnwrapToPrimaryPattern(PatternNode pattern)
    {
        Storm.Runtime.SyntaxNode current = pattern;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryPatternNode primaryPattern)
            {
                return primaryPattern;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PrimaryPatternNode found)
                {
                    return found;
                }

                next ??= child;
            }

            if(next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Extracts the initializer expression from the second variable declaration statement.
    /// </summary>
    private static ExpressionNode? ExtractSecondVarInitializer(CompilationUnitNode cu)
    {
        if(cu.GetTypes()[0] is not ClassTypeTypeDeclarationNode typeDecl)
        {
            return null;
        }

        if(typeDecl.ClassDeclaration.Members[0] is not MethodMemberMemberDeclarationNode methodMember)
        {
            return null;
        }

        var block = methodMember.Method.Body.GetBlock();
        if(block is null)
        {
            return null;
        }

        // Second statement (index 1): var result = scrutinee is [pattern];
        if(block.Statements.Length < 2)
        {
            return null;
        }

        if(block.Statements[1] is not VariableDeclarationStatementStatementNode stmt)
        {
            return null;
        }

        if(stmt.VariableDeclaration is not InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            return null;
        }

        return varDecl.Initializer;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 4: Pattern-context disambiguation
    //
    // For any expression `scrutinee` and pattern content inside an `is`
    // expression, parsing `scrutinee is [pattern, ..]` SHALL produce an
    // IsExpression whose pattern is a ListPatternPrimaryPatternNode
    // (not an ArrayLiteralExpressionPrimaryNode).
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralPatternDisambiguationPropertyTests) })]
    public Property IsExpression_WithListPattern_ProducesListPatternNode(PatternDisambiguationScenario scenario)
    {
        var source = BuildPatternSource(scenario);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var initializer = ExtractSecondVarInitializer(root);
        if(initializer is null)
        {
            return false.ToProperty().Label($"Could not extract second var initializer from: {source}");
        }

        // Find the IsExpressionRelationalNode in the expression tree
        var isExpr = FindIsExpression(initializer);
        if(isExpr is null)
        {
            return false.ToProperty().Label($"No IsExpressionRelationalNode found in: {source}");
        }

        // The pattern should be a ListPatternPrimaryPatternNode, not an ArrayLiteralExpression
        var primaryPattern = UnwrapToPrimaryPattern(isExpr.Pattern);
        if(primaryPattern is null)
        {
            return false.ToProperty().Label($"Could not unwrap pattern to PrimaryPatternNode in: {source}");
        }

        var isListPattern = primaryPattern is ListPatternPrimaryPatternNode;

        return isListPattern
            .ToProperty()
            .Label($"Expected ListPatternPrimaryPatternNode but got {primaryPattern.GetType().Name} for: {source}");
    }
}