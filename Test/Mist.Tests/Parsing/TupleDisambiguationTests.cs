// Feature: value-tuples — Unit tests for parser disambiguation
// **Validates: Requirements 7.1, 7.3, 7.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class TupleDisambiguationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static VariableDeclarationStatementNode GetFirstVarDecl(string source)
    {
        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        return stmt.VariableDeclaration;
    }

    [Fact]
    public void ParenthesizedExpressionNotTuple_SingleInt_ParsesAsParenthesized()
    {
        const string source = """
            class Test {
              void Run() {
                var x = (42);
              }
            }
            """;

        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(
            GetFirstVarDecl(source));

        var primary = UnwrapToPrimary(varDecl.Initializer);
        _ = Assert.IsType<ParenthesizedExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void SingleTypeNotTupleType_PrimitiveField_ParsesAsPrimitiveTypeRef()
    {
        const string source = """
            class Test {
              int value = 0;
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var fieldMember = Assert.IsType<FieldMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);

        _ = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(fieldMember.Field.Type.First);
    }

    [Fact]
    public void VarDeconstructionDisambiguation_VarTuple_ParsesAsVarDeconstruction()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b) = (1, 2);
              }
            }
            """;

        var varDecl = GetFirstVarDecl(source);
        _ = Assert.IsType<VarTupleDeconstructionVariableDeclarationStatementNode>(varDecl);
    }

    [Fact]
    public void VarDeconstructionDisambiguation_VarTuple_HasCorrectNames()
    {
        const string source = """
            class Test {
              void Run() {
                var (a, b) = (1, 2);
              }
            }
            """;

        var varDecl = GetFirstVarDecl(source);
        var deconstruction = Assert.IsType<VarTupleDeconstructionVariableDeclarationStatementNode>(varDecl);

        Assert.Equal("a", deconstruction.FirstName.Lexeme);
        _ = Assert.Single(deconstruction.RestNames);
        Assert.Equal("b", deconstruction.RestNames[0].Lexeme);
    }

    [Fact]
    public void VarDeconstructionDisambiguation_VarSingle_ParsesAsInferredDeclaration()
    {
        // `var` followed by an identifier rather than `(` is an inferred declaration.
        const string source = """
            class Test {
              void Run() {
                var t = (1, 2);
              }
            }
            """;

        var varDecl = GetFirstVarDecl(source);
        var inferred = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(varDecl);
        Assert.Equal("t", inferred.Name.Lexeme);

        var primary = UnwrapToPrimary(inferred.Initializer);
        _ = Assert.IsType<TupleLiteralExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void TupleLiteral_TwoElements_ParsesAsTupleLiteral()
    {
        const string source = """
            class Test {
              void Run() {
                var t = (1, 2);
              }
            }
            """;

        var varDecl = GetFirstVarDecl(source);
        var inferred = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(varDecl);
        var primary = UnwrapToPrimary(inferred.Initializer);
        var tupleLit = Assert.IsType<TupleLiteralExpressionPrimaryNode>(primary);

        // Should have First + Rest (arity 2), no additional elements.
        _ = Assert.IsType<UnnamedTupleLiteralElementTupleLiteralElementNode>(tupleLit.First);
        _ = Assert.IsType<UnnamedTupleLiteralElementTupleLiteralElementNode>(tupleLit.Rest);
        Assert.Empty(tupleLit.TupleLiteralElements);
    }

    [Fact]
    public void TupleTypeRef_TwoElements_ParsesAsTupleTypeRef()
    {
        const string source = """
            class Test {
              (int, bool) GetTuple() { return (1, true); }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);

        _ = Assert.IsType<TupleSingleTypeRefSingleTypeRefNode>(methodMember.Method.ReturnType.First);
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
            var childCount = 0;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                childCount++;
                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
                }

                next ??= child;
            }

            if(childCount > 1 || next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }
}