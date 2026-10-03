using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinementMemberRefusalTests
{
    [Theory]
    [InlineData("constant-unproven-against-a-symbolic-bound", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("from-end-index-unproven-for-indexer-constraint", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("from-end-index-violates-indexer-constraint", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("list-pattern-element-after-a-slice-unproven", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("list-pattern-element-violates-indexer-constraint", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("list-pattern-position-unproven-against-a-member-bound", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("member-bound-unproven-over-a-field", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("member-bound-unproven-where-the-member-may-exceed-the-value", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("member-bound-violated-through-a-lift", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("span-slice-start-past-its-length", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("span-slice-start-unproven", "MIST_PA_REFINEMENT_UNPROVEN")]
    public void MemberBoundsAndPositionsNeedASupportedProof(string name, string expected)
    {
        var h = new HandBuiltRefinements();
        if(name.StartsWith("span", StringComparison.Ordinal))
        {
            var span = HandBuiltRefinements.Local(h.Sequence("Span"), "span");
            var length = ((ProgramModel.Model.Types.ClassType)((ProgramModel.Model.Types.ConstructedType)span.Type).Definition).TypeParametersList[1];
            var bounds = HandBuiltRefinements.RefinePattern(h.Int, new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
                [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), length))]), "SpanBounds");
            var method = new Method("Slice", span.Type, [new Parameter("start", bounds), new Parameter("length", h.Int)], new MemberModifiers());
            var receiver = new MemberAccessExpression(HandBuiltModel.Node(), span, "Slice", span.Type);
            var call = new InvocationExpression(HandBuiltModel.Node(), receiver,
                [h.Literal(name.EndsWith("unproven", StringComparison.Ordinal) ? 1L : 9L), h.Literal(1L)], span.Type, method, [bounds, h.Int]);
            var body = name.EndsWith("unproven", StringComparison.Ordinal)
                ? HandBuiltModel.Block(HandBuiltRefinements.Evaluate(call))
                : HandBuiltModel.Block(HandBuiltRefinements.Give(span, h.Array(4L, "Span", erased: true)), HandBuiltRefinements.Evaluate(call));
            Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body)).Id.Value);
            return;
        }

        var owner = HandBuiltModel.Class("Ledger");
        var countField = new Field("count", h.Int, default, new MemberModifiers()) { Initializer = h.Literal(name == "list-pattern-element-violates-indexer-constraint" ? 9L : 3L) };
        var count = new Property("Count", h.Int, new MemberModifiers())
        {
            Getter = new PropertyAccessor
            {
                Body = name == "from-end-index-violates-indexer-constraint"
                    ? HandBuiltModel.Block(new ReturnStatement(h.Literal(9L), h.Int))
                    : name == "member-bound-violated-through-a-lift" ? HandBuiltModel.Block(new ReturnStatement(h.Literal(3L), h.Int))
                    : name == "member-bound-unproven-where-the-member-may-exceed-the-value"
                    ? HandBuiltModel.Block(HandBuiltRefinements.If(HandBuiltRefinements.Local(h.Bool), new ReturnStatement(h.Literal(3L), h.Int)), new ReturnStatement(h.Literal(5L), h.Int))
                    : HandBuiltModel.Block(new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), countField), h.Int)),
            },
        };
        var againstMember = name is "constant-unproven-against-a-symbolic-bound" or "member-bound-unproven-over-a-field"
            or "member-bound-unproven-where-the-member-may-exceed-the-value" or "member-bound-violated-through-a-lift"
            or "list-pattern-position-unproven-against-a-member-bound";
        Expression bound = name == "member-bound-unproven-over-a-field"
            ? new FieldReferenceExpression(HandBuiltModel.Node(), countField)
            : new PropertyReferenceExpression(HandBuiltModel.Node(), count);
        var target = HandBuiltRefinements.RefinePattern(h.Int, new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, againstMember ? bound : h.Literal(3L))]), null);
        var indexer = new Indexer(h.Int, [new Parameter("entry", target)], new MemberModifiers());
        var counted = new CountedIndexer(indexer, target, count);
        var receiverValue = HandBuiltRefinements.Local(owner, "ledger");
        Expression root;
        if(name.StartsWith("list", StringComparison.Ordinal))
        {
            var elements = new List<ListPatternElement>();
            if(name == "list-pattern-element-after-a-slice-unproven") { elements.Add(new ListPatternElement(null, true, null)); }

            if(name == "list-pattern-element-violates-indexer-constraint")
            {
                for(var i = 0; i < 8; i++) { elements.Add(new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), h.Int, null), false, h.Int)); }
            }

            elements.Add(new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), h.Int, new LocalVariable("element", h.Int)), false, h.Int));
            var list = new ListPattern(HandBuiltModel.Node(), elements, owner) { Reads = counted };
            root = new IsPatternExpression(HandBuiltModel.Node(), receiverValue, list, h.Bool);
        }
        else if(name.StartsWith("from-end", StringComparison.Ordinal))
        {
            root = new IndexExpression(HandBuiltModel.Node(), receiverValue, h.Back(h.Literal(1L)), h.Int) { Indexer = indexer, FromEnd = counted };
        }
        else
        {
            var position = name == "member-bound-violated-through-a-lift" ? 7L
                : name == "member-bound-unproven-where-the-member-may-exceed-the-value" ? 4L
                : name == "member-bound-unproven-over-a-field" ? 0L : 1L;
            root = new IndexExpression(HandBuiltModel.Node(), receiverValue, h.Literal(position), h.Int) { Indexer = indexer, ParameterType = target };
        }

        Assert.Equal(expected, Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), root)).Id.Value);
    }
}