using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// A from-end index, a list pattern and a counted slice call a count, an indexer or Slice that no expression in
// the model spells. Each rule that judges a call judges these from the recorded CountedIndexer or CountedSlice.
[Trait("Tier", "2")]
public sealed class CountedReadRuleTests
{
    [Theory]
    [InlineData("private-slice", "'Slice' is inaccessible")]
    [InlineData("private-count", "'Count' is inaccessible")]
    [InlineData("private-range-indexer-in-a-pattern", "'this[]' is inaccessible")]
    [InlineData("private-count-read-from-the-end", "'Count' is inaccessible")]
    [InlineData("private-count-read-by-a-list-pattern", "'Count' is inaccessible")]
    [InlineData("private-indexer-read-by-a-list-pattern", "'this[]' is inaccessible")]
    public void AMemberACountedReadCallsOutOfReachIsRefused(string shape, string message)
    {
        var fragment = Access(shape);

        var diagnostic = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));

        Assert.Equal("MIST_PA_INACCESSIBLE_MEMBER", diagnostic.Id.Value);
        Assert.StartsWith(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(Access(shape.Replace("private", "public", StringComparison.Ordinal)).Run(new AccessibilityAnalyzer()));
    }

    [Theory]
    [InlineData("count-with-a-private-getter", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("range-indexer-without-a-getter-in-a-pattern", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("count-with-a-private-getter-read-from-the-end", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("count-with-a-private-getter-read-by-a-list-pattern", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    public void AnAccessorACountedReadCallsMustBeReadable(string shape, string id)
    {
        var diagnostic = Assert.Single(Access(shape).Run(new AccessorAnalyzer()));

        Assert.Equal(id, diagnostic.Id.Value);
    }

    [Theory]
    [InlineData("1..3", null)]
    [InlineData("..", null)]
    [InlineData("1..^1", null)]
    [InlineData("3..1", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("^1..^3", "MIST_PA_CONSTRAINT_VIOLATED")]
    public void SlicesArgumentsAreTheOffsetsTheRangeDecodesAgainstTheCount(string range, string? id)
    {
        var h = new HandBuiltRefinements();
        var (strip, read) = Guarded(h, liftedCount: 4L);

        var findings = h.Built.Analyze(new RefinementAnalyzer(), Sliced(h, strip, read, Range(h, range)));

        if(id is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            var finding = Assert.Single(findings);
            Assert.Equal(id, finding.Id.Value);
            Assert.EndsWith("on parameter 'length'.", finding.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARangeWithNothingKnownLeavesBothArgumentsUnproven()
    {
        var h = new HandBuiltRefinements();
        var (strip, read) = Guarded(h, liftedCount: null);

        var findings = h.Built.Analyze(new RefinementAnalyzer(), Sliced(h, strip, read, HandBuiltRefinements.Local(h.Range, "window")));

        Assert.Equal(
            ["Argument 'the start of window' is not proven to satisfy the refinement constraint '>= 0' on parameter 'start'.",
             "Argument 'the length of window' is not proven to satisfy the refinement constraint '>= 0' on parameter 'length'."],
            findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(1L, "MIST_PA_CONSTRAINT_UNPROVEN")]
    public void APatternSlicesArgumentsAreItsLeadingCountAndWhatTheMatchLeaves(long atLeast, string? id)
    {
        var h = new HandBuiltRefinements();
        var (strip, read) = Guarded(h, liftedCount: null, lengthAtLeast: atLeast);
        var elements = new List<ListPatternElement>
        {
            new(new CatchAllPattern(HandBuiltModel.Node(), h.Int, null), IsSlice: false, h.Int),
            new(new CatchAllPattern(HandBuiltModel.Node(24), h.String, new LocalVariable("rest", h.String)), IsSlice: true, h.String),
            new(new CatchAllPattern(HandBuiltModel.Node(), h.Int, null), IsSlice: false, h.Int),
        };
        var list = new ListPattern(HandBuiltModel.Node(3), elements, strip) { Slice = read };

        var findings = h.Built.Analyze(new RefinementAnalyzer(), new IsPatternExpression(HandBuiltModel.Node(3), HandBuiltRefinements.Local(strip), list, h.Bool));

        Assert.Equal(id, findings.SingleOrDefault()?.Id.Value);
    }

    [Theory]
    [InlineData("pure-method-slices-through-an-impure-slice", "MIST_PA_PURE_SCOPE_CALLS_IMPURE", "call 'Slice'")]
    [InlineData("pure-method-slices-through-an-impure-count", "MIST_PA_PURE_SCOPE_CALLS_IMPURE", "read 'Count'")]
    [InlineData("pure-method-matches-a-slice-through-an-impure-slice", "MIST_PA_PURE_SCOPE_CALLS_IMPURE", "call 'Slice'")]
    [InlineData("readonly-method-slices-through-a-mutable-slice", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", "'Slice'")]
    [InlineData("readonly-method-matches-a-slice-through-a-mutable-slice", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", "'Slice'")]
    [InlineData("readonly-parameter-sliced-through-a-mutable-slice", "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE", "'Slice'")]
    public void TheSliceCallIsJudgedAsTheCallItIs(string fragment, string id, string named)
    {
        var finding = Assert.Single(new PurityFragments().Slices(fragment));

        Assert.True(finding.Id.Value == id, $"{fragment}: expected {id}, got {finding.Id.Value}: {finding.Message}");
        Assert.Contains(named, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure-method-slices-through-a-pure-slice")]
    [InlineData("readonly-method-slices-through-a-readonly-slice")]
    public void ASliceWhoseMembersKeepThePromiseIsAdmitted(string fragment)
    {
        Assert.Empty(new PurityFragments().Slices(fragment));
    }

    private static DeclarationFragments Access(string shape)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var owner = fragment.Class("Strip");
        fragment.Owner = fragment.Class("Probe");
        var hidden = DeclarationFragments.Public with { Accessibility = shape.StartsWith("private", StringComparison.Ordinal) ? Accessibility.Private : Accessibility.Public };
        var count = new Property("Count", integer, shape.Contains("count", StringComparison.Ordinal) ? hidden : DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true, Accessibility = shape.Contains("private-getter", StringComparison.Ordinal) ? Accessibility.Private : Accessibility.Public },
        };
        var receiver = new ThisExpression(HandBuiltModel.Node(), owner);

        if(shape.Contains("from-the-end", StringComparison.Ordinal) || shape.Contains("read-by-a-list-pattern", StringComparison.Ordinal))
        {
            var element = new Indexer(integer, [new Parameter("at", integer)], shape.Contains("private-indexer", StringComparison.Ordinal) ? hidden : DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
            };
            var reads = new CountedIndexer(element, integer, count);
            var last = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.FromEnd, HandBuiltModel.Literal(integer, 1L), fragment.Built.Type("Index"));
            var first = new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), integer, null), IsSlice: false, integer);
            fragment.Root = shape.Contains("from-the-end", StringComparison.Ordinal)
                ? new IndexExpression(HandBuiltModel.Node(30), receiver, last, integer) { Indexer = element, FromEnd = reads }
                : new ListPattern(HandBuiltModel.Node(30), [first], owner) { Reads = reads };
            return fragment;
        }

        if(shape.Contains("pattern", StringComparison.Ordinal))
        {
            var indexer = new Indexer(integer, [new Parameter("range", fragment.Built.Type("Range"))], shape.Contains("range-indexer", StringComparison.Ordinal) ? hidden : DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Getter = shape.Contains("without-a-getter", StringComparison.Ordinal) ? null : new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            };
            var middle = new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), integer, null), IsSlice: true, integer);
            fragment.Root = new ListPattern(HandBuiltModel.Node(30), [middle], owner) { Slice = new CountedSlice(count, indexer, null) };
            return fragment;
        }

        var slice = new Method("Slice", integer, [new Parameter("start", integer), new Parameter("length", integer)], shape.Contains("slice", StringComparison.Ordinal) ? hidden : DeclarationFragments.Public)
        {
            DeclaringType = owner,
        };
        var range = new RangeExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(integer, 0L), HandBuiltModel.Literal(integer, 1L), fragment.Built.Type("Range"));
        fragment.Root = new IndexExpression(HandBuiltModel.Node(30), receiver, range, integer) { Slice = new CountedSlice(count, null, slice) };
        return fragment;
    }

    private static (ClassType Strip, CountedSlice Read) Guarded(HandBuiltRefinements h, long? liftedCount, long lengthAtLeast = 0L)
    {
        var strip = HandBuiltModel.Class("Strip");
        var count = new Property("Count", liftedCount is null ? h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "Size") : h.Int, new MemberModifiers())
        {
            DeclaringType = strip,
            Getter = liftedCount is { } lifted
                ? new PropertyAccessor { Body = HandBuiltModel.Block(new ProgramModel.Model.Statements.ReturnStatement(h.Literal(lifted), h.Int)) }
                : new PropertyAccessor { IsAuto = true },
        };
        strip.PropertiesList.Add(count);
        var start = new Parameter("start", h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, null));
        var length = new Parameter("length", h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, lengthAtLeast, null));
        var slice = new Method("Slice", h.String, [start, length], new MemberModifiers()) { DeclaringType = strip };
        strip.MethodsList.Add(slice);
        return (strip, new CountedSlice(count, null, slice));
    }

    private static IndexExpression Sliced(HandBuiltRefinements h, ClassType strip, CountedSlice read, Expression range)
    {
        return new IndexExpression(HandBuiltModel.Node(3), HandBuiltRefinements.Local(strip, "strip"), range, h.String) { Slice = read };
    }

    private static RangeExpression Range(HandBuiltRefinements h, string spelled)
    {
        var bounds = spelled.Split("..");
        return h.Slice(Bound(h, bounds[0]), Bound(h, bounds[1]));
    }

    private static Expression? Bound(HandBuiltRefinements h, string spelled)
    {
        if(spelled.Length == 0)
        {
            return null;
        }

        return spelled.StartsWith('^')
            ? new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.FromEnd, h.Literal(long.Parse(spelled[1..], System.Globalization.CultureInfo.InvariantCulture)), h.Index)
            : h.Literal(long.Parse(spelled, System.Globalization.CultureInfo.InvariantCulture));
    }
}