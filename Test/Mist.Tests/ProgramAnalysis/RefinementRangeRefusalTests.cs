using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinementRangeRefusalTests
{
    [Theory]
    [InlineData("copied-invalid-slice-has-one-refusal", "MIST0274")]
    [InlineData("custom-slice-name-does-not-prove-length", "MIST0275")]
    [InlineData("index-after-a-copied-symbolic-range-bound-is-written", "MIST0274")]
    [InlineData("index-after-a-range-bound-is-reassigned-before-reconstruction", "MIST0275")]
    [InlineData("index-after-equal-length-different-range-joins", "MIST0275")]
    [InlineData("index-after-equivalent-stored-range-is-reassigned", "MIST0275")]
    [InlineData("index-after-equivalent-stored-range-is-written-by-a-lambda", "MIST0275")]
    [InlineData("index-after-range-from-end-marker-disagreement", "MIST0275")]
    [InlineData("index-after-repeated-unsupported-range-bounds", "MIST0275")]
    [InlineData("index-after-same-named-range-bounds-from-distinct-scopes", "MIST0275")]
    [InlineData("index-after-stored-index-marker-disagreement", "MIST0275")]
    [InlineData("index-past-a-range-the-paths-disagree-on", "MIST0275")]
    [InlineData("index-past-a-stored-range", "MIST0274")]
    [InlineData("index-past-equivalent-stored-range-join", "MIST0274")]
    [InlineData("negative-from-end-slice-offset-with-unknown-receiver", "MIST0274")]
    [InlineData("negative-slice-start", "MIST0274")]
    [InlineData("range-index-conversion-does-not-capture-its-operand", "MIST0275")]
    [InlineData("range-index-member-does-not-lift-a-getter-body", "MIST0275")]
    [InlineData("range-member-does-not-read-a-field-initializer", "MIST0275")]
    [InlineData("reversed-from-end-slice-endpoints", "MIST0274")]
    [InlineData("reversed-slice-endpoints", "MIST0274")]
    [InlineData("slice-constant-index-out-of-bounds", "MIST0274")]
    [InlineData("slice-end-beyond-receiver", "MIST0274")]
    [InlineData("slice-from-end-beyond-receiver", "MIST0274")]
    [InlineData("slice-past-decoded-string-length", "MIST0274")]
    [InlineData("stored-index-owned-field-ref-alias-is-unproven", "MIST0275")]
    [InlineData("stored-index-owned-this-field-ref-alias-is-unproven", "MIST0275")]
    [InlineData("user-comparison-does-not-prove-an-index-conversion-offset", "MIST0275")]
    public void CapturedRangeFactsMustProveTheSlice(string name, string expected)
    {
        var h = new HandBuiltRefinements();
        var numbers = HandBuiltRefinements.Local(h.Sequence(), "numbers");
        var window = HandBuiltRefinements.Local(h.Range, "window");
        var slice = HandBuiltRefinements.Local(h.Sequence(), "slice");
        var flag = HandBuiltRefinements.Local(h.Bool, "flag");
        var statements = new List<Statement>();
        var size = name.StartsWith("slice-", StringComparison.Ordinal) || name.StartsWith("reversed", StringComparison.Ordinal)
            || name is "negative-slice-start" or "copied-invalid-slice-has-one-refusal" ? 3L : 5L;
        statements.Add(HandBuiltRefinements.Give(numbers, h.Array(size, erased: true)));
        Expression receiver = numbers;
        Expression range = h.Slice(h.Literal(0L), h.Literal(3L));
        var access = 0L;
        var directlyRefused = name is "negative-slice-start" or "negative-from-end-slice-offset-with-unknown-receiver"
            or "reversed-from-end-slice-endpoints" or "reversed-slice-endpoints" or "slice-end-beyond-receiver"
            or "slice-from-end-beyond-receiver" or "slice-past-decoded-string-length" or "copied-invalid-slice-has-one-refusal";
        switch(name)
        {
            case "negative-slice-start": range = h.Slice(h.Literal(-1L), h.Literal(2L)); break;
            case "negative-from-end-slice-offset-with-unknown-receiver":
                statements.Clear(); range = h.Slice(h.Back(h.Literal(-1L)), h.Back(h.Literal(0L))); break;
            case "reversed-from-end-slice-endpoints": range = h.Slice(h.Back(h.Literal(0L)), h.Back(h.Literal(1L))); break;
            case "reversed-slice-endpoints": range = h.Slice(h.Literal(2L), h.Literal(1L)); break;
            case "slice-end-beyond-receiver":
            case "copied-invalid-slice-has-one-refusal": range = h.Slice(h.Literal(0L), h.Literal(4L)); break;
            case "slice-from-end-beyond-receiver": range = h.Slice(h.Back(h.Literal(4L)), null); break;
            case "slice-past-decoded-string-length": receiver = h.Literal("a\\nb"); range = h.Slice(h.Literal(0L), h.Literal(4L)); break;
            case "slice-constant-index-out-of-bounds":
                range = h.Slice(h.Literal(1L), h.Literal(3L)); access = 2; break;
            case "index-past-a-stored-range": range = h.Slice(h.Literal(0L), h.Literal(2L)); access = 2; break;
            case "index-past-equivalent-stored-range-join":
                statements.Add(HandBuiltRefinements.Give(window, range));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), h.Literal(3L))))));
                range = window; access = 3; break;
            case "index-after-a-copied-symbolic-range-bound-is-written":
                var end = HandBuiltRefinements.Local(h.Int, "end");
                var original = HandBuiltRefinements.Local(h.Range, "original");
                statements.Add(HandBuiltRefinements.Give(end, h.Literal(3L)));
                statements.Add(HandBuiltRefinements.Give(original, h.Slice(h.Literal(0L), end)));
                statements.Add(HandBuiltRefinements.Give(window, original));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, original))));
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(end, h.Literal(5L))));
                range = window; access = 3; break;
            case "index-after-a-range-bound-is-reassigned-before-reconstruction":
                var bound = HandBuiltRefinements.Local(h.Int, "end");
                statements.Add(HandBuiltRefinements.Give(bound, h.Literal(3L)));
                statements.Add(HandBuiltRefinements.Give(window, h.Slice(h.Literal(0L), bound)));
                statements.Add(HandBuiltRefinements.If(flag,
                    HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(bound, h.Literal(1L))),
                    HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), bound)))));
                range = window; break;
            case "index-after-same-named-range-bounds-from-distinct-scopes":
                var thenEnd = HandBuiltRefinements.Local(h.Int, "end");
                var elseEnd = HandBuiltRefinements.Local(h.Int, "end");
                statements.Add(HandBuiltRefinements.Give(window, h.Slice(null, null)));
                statements.Add(new IfStatement(flag,
                    HandBuiltModel.Block(HandBuiltRefinements.Give(thenEnd, h.Literal(3L)), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), thenEnd)))),
                    HandBuiltModel.Block(HandBuiltRefinements.Give(elseEnd, h.Literal(1L)), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), elseEnd))))));
                range = window; break;
            case "index-after-repeated-unsupported-range-bounds":
                range = h.Slice(h.Literal(0L), Unsupported(h));
                statements.Add(HandBuiltRefinements.Give(window, range));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), Unsupported(h))))));
                range = window; break;
            case "index-after-equivalent-stored-range-is-reassigned":
            case "index-after-equivalent-stored-range-is-written-by-a-lambda":
                statements.Add(HandBuiltRefinements.Give(window, range));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), h.Literal(3L))))));
                if(name.EndsWith("lambda", StringComparison.Ordinal))
                {
                    statements.Add(HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [], null,
                        HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), h.Literal(1L))))), false, h.Int)));
                }
                else { statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, HandBuiltRefinements.Local(h.Range, "replacement")))); }

                range = window; access = 2; break;
            case "index-after-equal-length-different-range-joins":
            case "index-after-range-from-end-marker-disagreement":
            case "index-past-a-range-the-paths-disagree-on":
                var first = name == "index-past-a-range-the-paths-disagree-on" ? h.Slice(h.Literal(0L), h.Literal(1L)) : range;
                var second = name == "index-after-equal-length-different-range-joins" ? h.Slice(h.Literal(1L), h.Literal(4L))
                    : name == "index-after-range-from-end-marker-disagreement" ? h.Slice(h.Literal(0L), h.Back(h.Literal(3L)))
                    : h.Slice(h.Literal(0L), h.Literal(3L));
                statements.Add(HandBuiltRefinements.Give(window, first));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, second))));
                range = window; access = name == "index-past-a-range-the-paths-disagree-on" ? 1 : 0; break;
            case "index-after-stored-index-marker-disagreement":
                var edge = HandBuiltRefinements.Local(h.Index, "edge");
                statements.Add(HandBuiltRefinements.Give(edge, h.Back(h.Literal(1L))));
                statements.Add(HandBuiltRefinements.Give(window, h.Slice(h.Literal(0L), edge)));
                statements.Add(HandBuiltRefinements.If(flag, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(window, h.Slice(h.Literal(0L), h.Literal(4L))))));
                range = window; break;
            case "range-member-does-not-read-a-field-initializer":
            case "range-index-member-does-not-lift-a-getter-body":
                var holder = HandBuiltModel.Class("Holder");
                var held = HandBuiltRefinements.Local(holder, "holder");
                Expression member;
                if(name == "range-member-does-not-read-a-field-initializer")
                {
                    var field = new Field("end", h.Int, default, new MemberModifiers()) { Initializer = h.Literal(3L) };
                    member = new MemberAccessExpression(HandBuiltModel.Node(), held, "end", h.Int, member: new FieldResult(field, holder, []));
                }
                else
                {
                    var property = new Property("End", h.Index, new MemberModifiers()) { Getter = new PropertyAccessor { Body = HandBuiltModel.Block(new ReturnStatement(h.Back(h.Literal(1L)), h.Index)) } };
                    member = new MemberAccessExpression(HandBuiltModel.Node(), held, "End", h.Index, member: new PropertyResult(property, holder, []));
                }

                range = h.Slice(h.Literal(0L), member); break;
            case "range-index-conversion-does-not-capture-its-operand":
            case "user-comparison-does-not-prove-an-index-conversion-offset":
                var counterType = HandBuiltModel.Class("Counter");
                var counter = HandBuiltRefinements.Local(counterType, "counter");
                var conversion = HandBuiltModel.Conversion(counterType, counterType, h.Index);
                var converted = new ImplicitConversionExpression(HandBuiltModel.Node(), counter, h.Index, [new ConversionFunction(conversion, counterType, h.Index)]);
                var captured = HandBuiltRefinements.Local(h.Index, "edge");
                statements.Add(HandBuiltRefinements.Give(captured, converted));
                range = h.Slice(h.Literal(0L), captured); access = 2;
                if(name.StartsWith("user", StringComparison.Ordinal))
                {
                    var comparison = new BinaryExpression(HandBuiltModel.Node(), counter, BinaryOperatorKind.Equal, h.Literal(3L), h.Bool)
                    { Operator = new Operator(OperatorKind.EqualEqual, h.Bool, [new Parameter("counter", counterType), new Parameter("value", h.Int)], new MemberModifiers()) };
                    statements.Add(HandBuiltRefinements.If(comparison,
                        HandBuiltRefinements.Give(slice, new IndexExpression(HandBuiltModel.Node(), receiver, range, h.Sequence()) { ParameterType = h.Range }),
                        HandBuiltRefinements.Evaluate(h.At(slice, h.Literal(access)))));
                    Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Number.Value); return;
                }

                break;
            case "stored-index-owned-field-ref-alias-is-unproven":
            case "stored-index-owned-this-field-ref-alias-is-unproven":
                var owner = HandBuiltModel.Class("Owner");
                var storedField = new Field("edge", h.Index, default, new MemberModifiers());
                Expression owned = name.Contains("this", StringComparison.Ordinal)
                    ? new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner), "edge", h.Index, member: new FieldResult(storedField, owner, []))
                    : new FieldReferenceExpression(HandBuiltModel.Node(), storedField);
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(owned, h.Back(h.Literal(1L)))));
                var alias = new LocalVariable("alias", h.Index) { IsRef = true };
                statements.Add(new LocalDeclarationStatement(alias, owned));
                statements.Add(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(alias), h.Back(h.Literal(5L)))));
                range = h.Slice(h.Literal(0L), owned); break;
            case "custom-slice-name-does-not-prove-length":
                var custom = HandBuiltModel.Class("Custom");
                var customMethod = new Method("Slice", h.Sequence(), [new Parameter("start", h.Int), new Parameter("count", h.Int)], new MemberModifiers())
                { Body = HandBuiltModel.Block(new ReturnStatement(h.Array(1L), h.Sequence())) };
                var customCall = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(custom), "Slice", h.Sequence()),
                    [h.Literal(0L), h.Literal(3L)], h.Sequence(), customMethod, [h.Int, h.Int]);
                statements.Add(HandBuiltRefinements.Give(slice, customCall));
                statements.Add(HandBuiltRefinements.Evaluate(h.At(slice, h.Literal(2L))));
                Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Number.Value); return;
        }

        var sliced = new IndexExpression(HandBuiltModel.Node(), receiver, range, h.Sequence()) { ParameterType = h.Range };
        statements.Add(HandBuiltRefinements.Give(slice, sliced));
        if(name == "copied-invalid-slice-has-one-refusal")
        {
            var firstCopy = HandBuiltRefinements.Local(h.Sequence(), "first");
            var secondCopy = HandBuiltRefinements.Local(h.Sequence(), "second");
            statements.Add(HandBuiltRefinements.Give(firstCopy, slice));
            statements.Add(HandBuiltRefinements.Give(secondCopy, firstCopy));
            statements.Add(HandBuiltRefinements.Evaluate(h.At(secondCopy, h.Literal(0L))));
        }
        else if(!directlyRefused) { statements.Add(HandBuiltRefinements.Evaluate(h.At(slice, h.Literal(access)))); }

        Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. statements]))).Number.Value);
    }

    private static InvocationExpression Unsupported(HandBuiltRefinements h)
    {
        var method = new Method("End", h.Int, [], new MemberModifiers()) { Body = HandBuiltModel.Block(new ReturnStatement(h.Literal(3L), h.Int)) };
        return new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, method, []);
    }
}