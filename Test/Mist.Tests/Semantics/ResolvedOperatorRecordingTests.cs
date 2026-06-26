using System.Collections.Immutable;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="SemanticModel.RecordResolvedOperator"/> and
/// <see cref="SemanticModel.GetResolvedOperator"/>. The type checker records
/// the operator selected by overload resolution; the emitter later retrieves
/// it to pick the correct emission path.
/// </summary>
public class ResolvedOperatorRecordingTests
{
    private static Storm.Runtime.Token MakeToken(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    /// <summary>
    /// Builds a minimal <see cref="SemanticModel"/> with an empty global scope
    /// and symbol table. The model carries no real compilation, but it is a
    /// valid target for the operator-recording methods under test.
    /// </summary>
    private static SemanticModel MakeEmptyModel()
    {
        var sourceText = new SourceText("test.mist", string.Empty);
        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));
        var globalScope = new Scope(parent: null);
        var symbolTable = new SymbolTable();

        return new SemanticModel(
            compilation,
            units: new List<CompilationUnit> { unit },
            asts: ImmutableArray<Storm.Runtime.SyntaxNode>.Empty,
            globalScope: globalScope,
            symbolTable: symbolTable);
    }

    private static Storm.Runtime.SyntaxNode MakeSyntaxNode(string tokenText = "1")
    {
        // A leaf AST node is sufficient — we only use it as a dictionary key.
        return new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(tokenText),
        };
    }

    private static ISymbol MakeOperatorSymbol(string name = "operator+")
    {
        var scope = new Scope(parent: null);
        return new Symbol(
            name: name,
            kind: SymbolKind.Method,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: null);
    }

    [Fact]
    public void RecordResolvedOperator_ThenGetResolvedOperator_RoundTripsTheSymbol()
    {
        var model = MakeEmptyModel();
        var node = MakeSyntaxNode();
        var op = MakeOperatorSymbol();

        model.RecordResolvedOperator(node, op);

        var retrieved = model.GetResolvedOperator(node);
        Assert.Same(op, retrieved);
    }

    [Fact]
    public void GetResolvedOperator_UnrecordedNode_ReturnsNull()
    {
        var model = MakeEmptyModel();
        var node = MakeSyntaxNode();

        Assert.Null(model.GetResolvedOperator(node));
    }

    [Fact]
    public void GetResolvedOperator_DistinctNodes_DoNotLeak()
    {
        // Recording an operator for one node must not cause an unrelated node
        // to be treated as recorded. This guards against accidental global
        // state or key collisions.
        var model = MakeEmptyModel();
        var recordedNode = MakeSyntaxNode("1");
        var otherNode = MakeSyntaxNode("2");
        var op = MakeOperatorSymbol();

        model.RecordResolvedOperator(recordedNode, op);

        Assert.Same(op, model.GetResolvedOperator(recordedNode));
        Assert.Null(model.GetResolvedOperator(otherNode));
    }

    [Fact]
    public void RecordResolvedOperator_SameNodeTwice_OverwritesWithLatest()
    {
        // If the type checker re-records (e.g., during a re-analysis pass),
        // the latest symbol wins. Dictionary-indexer semantics already give us
        // this behavior; this test pins it as part of the contract.
        var model = MakeEmptyModel();
        var node = MakeSyntaxNode();
        var first = MakeOperatorSymbol("operator+");
        var second = MakeOperatorSymbol("operator-");

        model.RecordResolvedOperator(node, first);
        model.RecordResolvedOperator(node, second);

        Assert.Same(second, model.GetResolvedOperator(node));
    }
}