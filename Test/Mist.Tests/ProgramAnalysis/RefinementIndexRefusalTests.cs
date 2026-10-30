using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinementIndexRefusalTests
{
    [Theory]
    [InlineData("constant-index-out-of-bounds", "MIST0274")]
    [InlineData("constant-index-write-out-of-bounds", "MIST0274")]
    [InlineData("enum-index-unproven", "MIST0275")]
    [InlineData("erased-array-constant-index-out-of-bounds", "MIST0274")]
    [InlineData("erased-span-constant-index-out-of-bounds", "MIST0274")]
    [InlineData("field-assigned-then-indexed-past-its-length", "MIST0274")]
    [InlineData("field-bound-lost-on-an-accessor", "MIST0275")]
    [InlineData("field-bound-lost-on-call", "MIST0275")]
    [InlineData("field-bound-lost-on-write", "MIST0275")]
    [InlineData("field-index-without-bound", "MIST0275")]
    [InlineData("field-length-lost-on-an-accessor", "MIST0275")]
    [InlineData("field-length-lost-on-call", "MIST0275")]
    [InlineData("from-end-index-out-of-bounds", "MIST0274")]
    [InlineData("from-end-index-write-out-of-bounds", "MIST0274")]
    [InlineData("from-end-offset-range-past-the-start", "MIST0275")]
    [InlineData("from-end-offset-range-reaching-the-length", "MIST0275")]
    [InlineData("index-after-length-not-one", "MIST0275")]
    [InlineData("index-bound-against-an-array-a-lambda-reassigns-unproven", "MIST0275")]
    [InlineData("index-past-a-length-guard", "MIST0275")]
    [InlineData("index-past-an-inequality-length-guard", "MIST0275")]
    [InlineData("index-without-lower-bound", "MIST0275")]
    [InlineData("index-written-by-a-lambda-unproven", "MIST0275")]
    [InlineData("inequality-array-length-lost-on-write", "MIST0275")]
    [InlineData("inequality-field-length-lost-on-call", "MIST0275")]
    [InlineData("inequality-field-length-lost-on-write", "MIST0275")]
    [InlineData("inequality-index-guard-without-lower-bound", "MIST0275")]
    [InlineData("loop-index-reaches-length", "MIST0275")]
    [InlineData("negative-constant-index", "MIST0274")]
    [InlineData("readonly-span-index-after-index-write", "MIST0274")]
    [InlineData("readonly-span-index-after-receiver-write", "MIST0275")]
    [InlineData("readonly-span-index-without-upper-bound", "MIST0275")]
    [InlineData("readonly-span-length-not-one", "MIST0275")]
    [InlineData("refined-index-not-implied", "MIST0275")]
    [InlineData("span-index-after-receiver-write", "MIST0275")]
    [InlineData("span-index-at-length-guard", "MIST0275")]
    [InlineData("span-index-past-nonempty-guard", "MIST0275")]
    [InlineData("sized-call-index-out-of-bounds", "MIST0274")]
    [InlineData("span-index-without-lower-bound", "MIST0275")]
    [InlineData("stored-index-from-end-zero-is-not-an-element", "MIST0274")]
    [InlineData("unguarded-constant-index-into-parameter", "MIST0275")]
    [InlineData("unguarded-from-end-index-into-parameter", "MIST0275")]
    [InlineData("unproven-index-write", "MIST0275")]
    [InlineData("unproven-index", "MIST0275")]
    [InlineData("unproven-symbolic-index", "MIST0275")]
    [InlineData("unproven-symbolic-upper-bound", "MIST0275")]
    [InlineData("unsized-call-index-unproven", "MIST0275")]
    public void IndexProofMustSurviveThePath(string name, string expected)
    {
        var h = new HandBuiltRefinements();
        var sequence = name.Contains("readonly-span", StringComparison.Ordinal) ? "ReadOnlySpan"
            : name.Contains("span", StringComparison.Ordinal) ? "Span" : "Array";
        var receiver = HandBuiltRefinements.Local(h.Sequence(sequence), "items");
        Expression array = receiver;
        Expression index = HandBuiltRefinements.Local(h.Int, "slot");
        var statements = new List<Statement>();
        Expression? guard = null;
        var inside = new List<Statement>();
        var sized = name is "constant-index-out-of-bounds" or "constant-index-write-out-of-bounds"
            or "erased-array-constant-index-out-of-bounds" or "erased-span-constant-index-out-of-bounds"
            or "from-end-index-out-of-bounds" or "from-end-index-write-out-of-bounds" or "negative-constant-index"
            or "unproven-index" or "unproven-index-write" or "refined-index-not-implied" or "enum-index-unproven"
            or "loop-index-reaches-length" or "stored-index-from-end-zero-is-not-an-element";
        if(sized)
        {
            statements.Add(HandBuiltRefinements.Give(receiver, h.Array(sequence == "Span" ? 4L : name == "enum-index-unproven" ? 1L : 3L, sequence, erased: true)));
        }

        if(name.StartsWith("field", StringComparison.Ordinal) || name.StartsWith("inequality-field", StringComparison.Ordinal))
        {
            var field = new Field("items", receiver.Type, default, new MemberModifiers()) { Initializer = h.Array(3L) };
            array = new FieldReferenceExpression(HandBuiltModel.Node(), field);
        }

        switch(name)
        {
            case "constant-index-out-of-bounds":
            case "constant-index-write-out-of-bounds": index = h.Literal(3L); break;
            case "erased-array-constant-index-out-of-bounds": index = h.Literal(5L); break;
            case "erased-span-constant-index-out-of-bounds": index = h.Literal(9L); break;
            case "negative-constant-index": index = h.Literal(-1L); break;
            case "from-end-index-out-of-bounds": index = h.Back(h.Literal(9L)); break;
            case "from-end-index-write-out-of-bounds": index = h.Back(h.Literal(0L)); break;
            case "stored-index-from-end-zero-is-not-an-element":
                var stored = HandBuiltRefinements.Local(h.Index, "edge"); statements.Add(HandBuiltRefinements.Give(stored, h.Back(h.Literal(0L)))); index = stored; break;
            case "sized-call-index-out-of-bounds":
            case "unsized-call-index-unproven":
                var sizedCall = name.StartsWith("sized", StringComparison.Ordinal);
                array = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Sequence("Array", sizedCall ? 3L : null), null, []);
                index = h.Literal(sizedCall ? 3L : 2L); break;
            case "unguarded-constant-index-into-parameter": index = h.Literal(0L); break;
            case "unguarded-from-end-index-into-parameter": index = h.Back(h.Literal(1L)); break;
            case "refined-index-not-implied": index = HandBuiltRefinements.Local(h.Between(h.Int, 0L, 9L, "Digit")); break;
            case "enum-index-unproven":
                index = HandBuiltRefinements.Local(HandBuiltModel.Enum("Color", h.Built.Type("Byte")), "color"); break;
            case "unproven-symbolic-upper-bound":
            case "readonly-span-index-without-upper-bound": guard = h.Binary(index, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)); break;
            case "index-without-lower-bound":
            case "span-index-without-lower-bound": guard = h.Binary(index, BinaryOperatorKind.LessThan, h.Length(array)); break;
            case "inequality-index-guard-without-lower-bound":
                guard = h.And(h.Binary(index, BinaryOperatorKind.NotEqual, h.Literal(0L)), h.Binary(index, BinaryOperatorKind.LessThan, h.Length(array))); break;
            case "span-index-at-length-guard":
                guard = h.And(h.Binary(index, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)), h.Binary(index, BinaryOperatorKind.LessThanOrEqual, h.Length(array))); break;
            case "index-after-length-not-one":
            case "readonly-span-length-not-one":
                index = h.Literal(0L); guard = h.Binary(h.Length(array), BinaryOperatorKind.NotEqual, h.Literal(1L)); break;
            case "index-past-a-length-guard":
            case "span-index-past-nonempty-guard":
                index = h.Literal(1L); guard = h.Binary(h.Length(array), BinaryOperatorKind.GreaterThan, h.Literal(0L)); break;
            case "index-past-an-inequality-length-guard":
                index = h.Literal(1L); guard = h.Binary(h.Length(array), BinaryOperatorKind.NotEqual, h.Literal(0L)); break;
            case "from-end-offset-range-past-the-start":
            case "from-end-offset-range-reaching-the-length":
                var offset = (LocalReferenceExpression)index;
                var low = name.EndsWith("start", StringComparison.Ordinal) ? 1L : 0L;
                guard = h.And(h.And(h.Binary(offset, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(low)),
                    h.Binary(offset, BinaryOperatorKind.LessThanOrEqual, h.Literal(2L))),
                    h.Binary(h.Length(array), BinaryOperatorKind.GreaterThanOrEqual, h.Literal(2L - low)));
                index = h.Back(offset); break;
            case "field-assigned-then-indexed-past-its-length":
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(array, h.Array(3L, erased: true)))); index = h.Literal(3L); break;
            case "field-bound-lost-on-write":
            case "field-bound-lost-on-call":
            case "field-bound-lost-on-an-accessor":
            case "span-index-after-receiver-write":
            case "readonly-span-index-after-index-write":
            case "index-bound-against-an-array-a-lambda-reassigns-unproven":
            case "index-written-by-a-lambda-unproven":
                guard = h.And(h.Binary(index, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)), h.Binary(index, BinaryOperatorKind.LessThan, h.Length(array)));
                if(name == "readonly-span-index-after-index-write") { inside.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(index, h.Literal(-1L)))); }
                else if(name.Contains("lambda", StringComparison.Ordinal))
                {
                    var written = name == "index-written-by-a-lambda-unproven" ? index : array;
                    var replacement = ReferenceEquals(written, index) ? h.Literal(-1L) : h.Array(0L, erased: true);
                    inside.Add(HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [], null,
                        HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(written, replacement))), false, h.Int)));
                }
                else { inside.Add(HandBuiltRefinements.Evaluate(Invalidation(h, name, array, sequence))); }

                break;
            case "field-length-lost-on-call":
            case "field-length-lost-on-an-accessor":
            case "inequality-field-length-lost-on-call":
            case "inequality-field-length-lost-on-write":
            case "inequality-array-length-lost-on-write":
            case "readonly-span-index-after-receiver-write":
                index = h.Literal(0L);
                guard = h.Binary(h.Length(array), name.StartsWith("inequality", StringComparison.Ordinal) ? BinaryOperatorKind.NotEqual : BinaryOperatorKind.GreaterThan, h.Literal(0L));
                inside.Add(HandBuiltRefinements.Evaluate(Invalidation(h, name, array, sequence))); break;
            case "loop-index-reaches-length":
                var loop = (LocalReferenceExpression)index;
                statements.Add(HandBuiltRefinements.Give(loop, h.Literal(0L)));
                statements.Add(new WhileStatement(h.Binary(loop, BinaryOperatorKind.LessThanOrEqual, h.Literal(3L)),
                    HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.At(array, loop)),
                        HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(loop, h.Binary(loop, BinaryOperatorKind.Add, h.Literal(1L), h.Int))))));
                Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Number.Value);
                return;
        }

        var access = h.At(array, index);
        Expression at = name is "constant-index-write-out-of-bounds" or "from-end-index-write-out-of-bounds" or "unproven-index-write"
            ? HandBuiltRefinements.Assign(access, h.Literal(1L)) : access;
        inside.Add(HandBuiltRefinements.Evaluate(at));
        if(guard is null) { statements.AddRange(inside); }
        else { statements.Add(HandBuiltRefinements.If(guard, [.. inside])); }

        Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Number.Value);
    }

    private static Expression Invalidation(HandBuiltRefinements h, string name, Expression array, string sequence)
    {
        if(name.Contains("accessor", StringComparison.Ordinal))
        {
            var owner = HandBuiltModel.Class("Owner");
            var reads = new Field("reads", h.Int, default, new MemberModifiers()) { DeclaringType = owner };
            var property = new Property("Shrunk", h.Int, new MemberModifiers())
            {
                Getter = new PropertyAccessor
                {
                    Body = HandBuiltModel.Block(
                        HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(new FieldReferenceExpression(HandBuiltModel.Node(), reads), h.Literal(1L))),
                        new ReturnStatement(h.Literal(1L), h.Int)),
                },
                DeclaringType = owner,
            };
            return new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner), "Shrunk", h.Int,
                member: new PropertyResult(property, owner, []));
        }

        if(name.Contains("call", StringComparison.Ordinal))
        {
            return new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, null, []);
        }

        return HandBuiltRefinements.Assign(array, HandBuiltRefinements.Local(h.Sequence(sequence), "replacement"));
    }
}