using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Contracts(string name)
    {
        return name switch
        {
            "pure-slot-answered-by-a-method-reading-a-mutable-static" => ShapeSlot(Public, Returning(StaticRead(counter))),
            "pure-slot-answered-by-a-readonly-method-reading-a-mutable-static" => ShapeSlot(Readonly, Returning(StaticRead(counter))),
            "local-slot-answered-by-a-method-writing-a-static" => SinkSlot(Public, (_, n) => HandBuiltModel.Block(Set(StaticRead(counter), Ref(n)))),
            "pure-slot-answered-by-an-unmarked-method-returning-a-literal" => ShapeSlot(Public, Returning(Number(4))),
            "pure-slot-answered-by-a-pure-method-reading-a-readonly-static" => ShapeSlot(Pure, Returning(StaticRead(limit))),
            "local-slot-answered-by-a-method-writing-its-own-field" => SinkSlot(
                Public, (count, n) => HandBuiltModel.Block(Set(Own(count), Arithmetic(Own(count), BinaryOperatorKind.Add, Ref(n))))),
            "local-slot-answered-by-a-pure-method-with-an-empty-body" => SinkSlot(Pure, (_, _) => HandBuiltModel.Block()),
            "plain-function-in-an-object-tested-for-a-pure-function-type" => TestedAgainstAPureFunctionType(),
            "plain-function-in-an-object-converted-as-a-pure-function-type" => ConvertedAsAPureFunctionType(),
            "plain-function-in-an-object-cast-to-a-pure-function-type" => CastToAPureFunctionType(),
            "pure-function-value-tested-for-its-own-type" => TestedForItsOwnType(),
            _ => null,
        };
    }

    private IReadOnlyList<Diagnostic> ShapeSlot(MemberModifiers answering, Block body)
    {
        var shape = Contract("Shape");
        _ = Method(shape, "Area", integer, Pure, null);
        var square = Implementer("Square", shape, declared: true);
        _ = Method(square, "Area", integer, answering, body);
        return model.Analyze(new ConformanceAnalyzer(), types: [shape, square]);
    }

    private IReadOnlyList<Diagnostic> SinkSlot(MemberModifiers answering, Func<Field, LocalVariable, Block> body)
    {
        var sink = Contract("Sink");
        _ = Method(sink, "Add", unit, Local, null, Input("n", integer));
        var bucket = Implementer("Bucket", sink, declared: true);
        var count = Field(bucket, "Count", integer);
        var n = Input("n", integer);
        _ = Method(bucket, "Add", unit, answering, body(count, n), n);
        return model.Analyze(new ConformanceAnalyzer(), types: [sink, bucket]);
    }

    private IReadOnlyList<Diagnostic> TestedAgainstAPureFunctionType()
    {
        var o = Variable("o", model.Type("Object"));
        var reader = Function(pure: true, integer);
        var test = new IsPatternExpression(Node(), Ref(o), new TypeTestPattern(Node(37), reader, Variable("r", reader)), boolean);
        return model.Analyze(new PatternAnalyzer(), test);
    }

    private IReadOnlyList<Diagnostic> ConvertedAsAPureFunctionType()
    {
        var o = Variable("o", model.Type("Object"));
        var reader = Function(pure: true, integer);
        var converted = new AsExpression(Node(37), Ref(o), reader, AsConversionKind.Narrowing, null, reader);
        return model.Analyze(new CastAnalyzer(), converted);
    }

    private IReadOnlyList<Diagnostic> CastToAPureFunctionType()
    {
        var o = Variable("o", model.Type("Object"));
        var reader = Function(pure: true, integer);
        var cast = new CastExpression(Node(37), Ref(o), reader, [], reader);
        return model.Analyze(new CastAnalyzer(), cast);
    }

    private IReadOnlyList<Diagnostic> TestedForItsOwnType()
    {
        var reader = Function(pure: true, integer);
        var p = Variable("p", reader);
        var test = new IsPatternExpression(Node(), Ref(p), new TypeTestPattern(Node(37), reader, Variable("q", reader)), boolean);
        return model.Analyze(new PatternAnalyzer(), test);
    }
}