// Feature: array-literals, Property 2: Trailing comma equivalence
// **Validates: Requirements 1.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario: a list of N integer literal expressions
/// that will be formatted as array literals with and without trailing comma.
/// </summary>
public sealed class ArrayLiteralTrailingCommaScenario
{
    private readonly int[] elements;

    public IReadOnlyList<int> Elements => elements;

    public int Count => elements.Length;

    public ArrayLiteralTrailingCommaScenario(int[] elements)
    {
        this.elements = elements;
    }

    public override string ToString()
    {
        return $"[{string.Join(", ", elements)}] (N={elements.Length})";
    }
}

/// <summary>
/// Property-based tests for trailing comma equivalence.
///
/// Property 2: For any valid array literal source text [e1, e2, ..., eN],
/// parsing with a trailing comma [e1, e2, ..., eN,] SHALL produce an AST
/// with the same element count and equivalent element expressions as parsing
/// without the trailing comma.
/// </summary>
public class ArrayLiteralTrailingCommaPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<ArrayLiteralTrailingCommaScenario> ArbitraryScenario()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from elements in Gen.ArrayOf(Gen.Choose(-1000, 1000), count)
            select new ArrayLiteralTrailingCommaScenario(elements);

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
    /// Builds a complete Mist source file with the array literal (no trailing comma).
    /// </summary>
    private static string BuildSourceWithoutTrailingComma(IReadOnlyList<int> elements)
    {
        var elementList = string.Join(", ", elements);
        return $"class Test {{ void Run() {{ var x = [{elementList}]; }} }}";
    }

    /// <summary>
    /// Builds a complete Mist source file with the array literal (with trailing comma).
    /// </summary>
    private static string BuildSourceWithTrailingComma(IReadOnlyList<int> elements)
    {
        var elementList = string.Join(", ", elements);
        return $"class Test {{ void Run() {{ var x = [{elementList},]; }} }}";
    }

    /// <summary>
    /// Extracts the ArrayLiteralExpressionPrimaryNode from a parsed source.
    /// </summary>
    private static ArrayLiteralExpressionPrimaryNode? ExtractArrayLiteral(CompilationUnitNode cu)
    {
        if(cu.GetTypes()[0] is not BodyTypeTypeDeclarationNode typeDecl)
        {
            return null;
        }

        if(typeDecl.TypeBody.Members[0] is not MethodMemberMemberDeclarationNode methodMember)
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

    /// <summary>
    /// Collects all element values from an ArrayLiteralExpressionPrimaryNode
    /// by pretty-printing the full compilation unit and extracting the array literal text.
    /// We compare the pretty-printed output of both variants to verify structural equivalence.
    /// </summary>
    private static string GetPrettyPrintedOutput(CompilationUnitNode cu, string source)
    {
        return Mist.Parsing.MistPrettyPrinter.Print(cu, source);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2: Trailing comma equivalence
    //
    // For any valid array literal source text [e1, e2, ..., eN], parsing with
    // a trailing comma [e1, e2, ..., eN,] SHALL produce an AST with the same
    // element count and equivalent element expressions as parsing without the
    // trailing comma.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralTrailingCommaPropertyTests) })]
    public Property ArrayLiteral_TrailingComma_ProducesSameAst(ArrayLiteralTrailingCommaScenario scenario)
    {
        var sourceWithout = BuildSourceWithoutTrailingComma(scenario.Elements);
        var sourceWith = BuildSourceWithTrailingComma(scenario.Elements);

        var rootWithout = ParseSource(sourceWithout);
        var rootWith = ParseSource(sourceWith);

        if(rootWithout is null)
        {
            return false.ToProperty().Label($"Failed to parse without trailing comma: {sourceWithout}");
        }

        if(rootWith is null)
        {
            return false.ToProperty().Label($"Failed to parse with trailing comma: {sourceWith}");
        }

        var arrayWithout = ExtractArrayLiteral(rootWithout);
        var arrayWith = ExtractArrayLiteral(rootWith);

        if(arrayWithout is null)
        {
            return false.ToProperty().Label($"No ArrayLiteralExpressionPrimaryNode found (without comma): {sourceWithout}");
        }

        if(arrayWith is null)
        {
            return false.ToProperty().Label($"No ArrayLiteralExpressionPrimaryNode found (with comma): {sourceWith}");
        }

        // Verify identical element count
        var countWithout = arrayWithout.GetElements().Length;
        var countWith = arrayWith.GetElements().Length;

        if(countWithout != countWith)
        {
            return false.ToProperty().Label(
                $"Element count mismatch: without={countWithout}, with={countWith} for: {scenario}");
        }

        // Verify equivalent AST structure by comparing pretty-printed output
        var printedWithout = GetPrettyPrintedOutput(rootWithout, sourceWithout);
        var printedWith = GetPrettyPrintedOutput(rootWith, sourceWith);

        if(printedWithout != printedWith)
        {
            return false.ToProperty().Label(
                $"Pretty-printed output differs:\n  without: {printedWithout}\n  with: {printedWith}\n  for: {scenario}");
        }

        return true.ToProperty()
            .Label($"Trailing comma equivalence holds for {scenario.Count} elements");
    }
}