// Feature: array-literals, Property 1: Array literal parsing produces correct element count
// **Validates: Requirements 1.1, 11.1, 11.2, 11.3, 11.4, 11.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario: a list of N integer literal expressions
/// that will be formatted as an array literal [e1, e2, ..., eN].
/// </summary>
public sealed class ArrayLiteralElementCountScenario
{
    private readonly int[] elements;

    public IReadOnlyList<int> Elements => elements;

    public int Count => elements.Length;

    public ArrayLiteralElementCountScenario(int[] elements)
    {
        this.elements = elements;
    }

    public override string ToString()
    {
        return $"[{string.Join(", ", elements)}] (N={elements.Length})";
    }
}

/// <summary>
/// Property-based tests for array literal element count correctness.
///
/// Property 1: For any list of N valid expressions (where N ∈ [1, 20]),
/// parsing the source text [e1, e2, ..., eN] in expression position SHALL
/// produce an ArrayLiteralExpressionPrimaryNode whose total element count
/// (1 for First + Expressions.Length) equals N.
/// </summary>
public class ArrayLiteralElementCountPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<ArrayLiteralElementCountScenario> ArbitraryScenario()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from elements in Gen.ArrayOf(Gen.Choose(-1000, 1000), count)
            select new ArrayLiteralElementCountScenario(elements);

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
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
    /// Builds a complete Mist source file with the array literal as a variable initializer.
    /// </summary>
    private static string BuildSource(IReadOnlyList<int> elements)
    {
        var elementList = string.Join(", ", elements);
        return $"class Test {{ void Run() {{ var x = [{elementList}]; }} }}";
    }

    /// <summary>
    /// Extracts the ArrayLiteralExpressionPrimaryNode from a parsed source.
    /// </summary>
    private static ArrayLiteralExpressionPrimaryNode? ExtractArrayLiteral(CompilationUnitNode cu)
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

        if(block.Statements[0] is not VariableDeclarationStatementStatementNode stmt)
        {
            return null;
        }

        if(stmt.VariableDeclaration is not InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            return null;
        }

        var primary = UnwrapToPrimary(varDecl.Initializer);
        return primary as ArrayLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Unwraps a simple expression down to its primary node.
    /// </summary>
    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryNode primary)
            {
                return primary;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
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

    // ═════════════════════════════════════════════════════════════════════════
    // Property 1: Array literal parsing produces correct element count
    //
    // For any list of N valid expressions (N ∈ [1, 20]), constructing source
    // [e1, e2, ..., eN], parsing in-process, and verifying that the
    // ArrayLiteralExpressionPrimaryNode total element count equals N.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralElementCountPropertyTests) })]
    public Property ArrayLiteral_ElementCount_EqualsN(ArrayLiteralElementCountScenario scenario)
    {
        var source = BuildSource(scenario.Elements);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var arrayLiteral = ExtractArrayLiteral(root);

        if(arrayLiteral is null)
        {
            return false.ToProperty().Label($"No ArrayLiteralExpressionPrimaryNode found in: {source}");
        }

        // Total element count = 1 (First) + Expressions.Length
        var actualCount = 1 + arrayLiteral.Expressions.Length;
        var expectedCount = scenario.Count;

        return (actualCount == expectedCount)
            .ToProperty()
            .Label($"Expected {expectedCount} elements but got {actualCount} for: {source}");
    }
}