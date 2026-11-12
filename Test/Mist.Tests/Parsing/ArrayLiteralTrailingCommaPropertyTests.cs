// Feature: array-literals, Property 2: Trailing comma equivalence
// **Validates: Requirements 1.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

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

public class ArrayLiteralTrailingCommaPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    public static Arbitrary<ArrayLiteralTrailingCommaScenario> ArbitraryScenario()
    {
        var gen =
            from count in Gen.Choose(1, 20)
            from elements in Gen.ArrayOf(Gen.Choose(-1000, 1000), count)
            select new ArrayLiteralTrailingCommaScenario(elements);

        return gen.ToArbitrary();
    }

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

    private static string BuildSourceWithoutTrailingComma(IReadOnlyList<int> elements)
    {
        var elementList = string.Join(", ", elements);
        return $"class Test {{ void Run() {{ var x = [{elementList}]; }} }}";
    }

    private static string BuildSourceWithTrailingComma(IReadOnlyList<int> elements)
    {
        var elementList = string.Join(", ", elements);
        return $"class Test {{ void Run() {{ var x = [{elementList},]; }} }}";
    }

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

    private static string GetPrettyPrintedOutput(CompilationUnitNode cu, string source)
    {
        return Mist.Parsing.MistPrettyPrinter.Print(cu, source);
    }

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