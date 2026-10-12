using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.FrameFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FrameEscapeSiteTests
{
    private readonly FrameFragments f = new();

    [Theory]
    [InlineData("holder-local")]
    [InlineData("holder-built-at-the-return")]
    [InlineData("holder-reassigned-from-a-heap-span-is-still-stack-bound")]
    public void AHolderOfStackMemoryIsRefusedAtTheReturn(string shape)
    {
        var st = Local("st", f.Span);
        var h = Local("h", f.Holder);
        var body = shape switch
        {
            "holder-local" => HandBuiltModel.Block(Declare(st, f.Stackalloc()), Declare(h, f.NewHolder(Ref(st))), Return(Ref(h, 37))),
            "holder-built-at-the-return" => HandBuiltModel.Block(Declare(st, f.Stackalloc()), Return(f.NewHolder(Ref(st), 37))),
            _ => HandBuiltModel.Block(
                Declare(st, f.Stackalloc()), Declare(h, f.NewHolder(Ref(st))),
                Run(Assign(Ref(h), f.NewHolder(Ref(Parameter("p", f.Span))))), Return(Ref(h, 37))),
        };

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), body), "MIST_PA_STACK_SPAN_RETURNED", "This 'Holder' refers to stackalloc memory");
    }

    [Fact]
    public void AStackSpanBoundByAPatternIsRefusedWhereTheBindingIsReturned()
    {
        var st = Local("st", f.Span);
        var three = Local("three", f.Span);
        var test = new IsPatternExpression(
            HandBuiltModel.Node(), Ref(st), new ProgramModel.Model.Patterns.TypeTestPattern(HandBuiltModel.Node(), f.Span, three), f.Model.Type("Boolean"));
        var body = HandBuiltModel.Block(Declare(st, f.Stackalloc()), new IfStatement(test, HandBuiltModel.Block(Return(Ref(three, 37))), null));

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), body), "MIST_PA_STACK_SPAN_RETURNED", "This 'Span");
    }

    [Fact]
    public void ALambdaResultOverItsOwnStackMemoryIsRefused()
    {
        var lambda = new LambdaExpression(
            HandBuiltModel.Node(), [], f.Stackalloc(37), null, false, f.Model.Interner.GetOrCreateFunctionType([], f.Span));

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), Run(lambda)), "MIST_PA_STACK_SPAN_RETURNED", "This 'Span");
    }

    [Theory]
    [InlineData("heap-declared-local", "local 'heap'")]
    [InlineData("parameter", "parameter 'p'")]
    [InlineData("field-of-this", "field 'Data' of 'this'")]
    [InlineData("field-of-a-heap-holder", "field 'Data' of local 'h'")]
    public void StackMemoryStoredWhereItOutlivesTheFrameIsRefusedAtTheValue(string shape, string destination)
    {
        var st = Local("st", f.Span);
        var heap = Local("heap", f.Span);
        var h = Local("h", f.Holder);
        Expression target = shape switch
        {
            "heap-declared-local" => Ref(heap),
            "parameter" => Ref(Parameter("p", f.Span)),
            "field-of-this" => new FieldReferenceExpression(HandBuiltModel.Node(), f.Data),
            _ => Member(Ref(h), f.Data),
        };
        var body = HandBuiltModel.Block(
            Declare(st, f.Stackalloc()),
            Declare(heap, Ref(Parameter("q", f.Span))),
            Declare(h, f.NewHolder(Ref(Parameter("r", f.Span)))),
            Run(Assign(target, Ref(st, 37))));

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), body), "MIST_PA_STACK_SPAN_STORED", $"cannot be stored in {destination}:");
    }

    [Fact]
    public void StackMemoryLentBesideAHeapHolderTheCallCouldStoreItInIsRefusedAtTheArgument()
    {
        var st = Local("st", f.Span);
        var h = Local("h", f.Holder);
        var body = HandBuiltModel.Block(
            Declare(st, f.Stackalloc()),
            Declare(h, f.NewHolder(Ref(Parameter("p", f.Span)))),
            Run(Call(f.Put, Ref(h), 0, Ref(st, 37))));

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), body), "MIST_PA_STACK_SPAN_PASSED", "passed to 'Put' alongside local 'h'");
    }

    [Theory]
    [InlineData("local", "local 'x'")]
    [InlineData("alias-of-a-local", "local 'x'")]
    [InlineData("value-parameter", "parameter 'p'")]
    [InlineData("element-of-stack-memory", "stackalloc memory")]
    public void AReferenceReturnedToFrameStorageIsRefusedAtTheReturnedValue(string shape, string origin)
    {
        var x = Local("x", f.Integer);
        var alias = Local("alias", f.Integer, isRef: true);
        var st = Local("st", f.Span);
        var body = shape switch
        {
            "local" => HandBuiltModel.Block(Declare(x, HandBuiltModel.Literal(f.Integer, 1L)), Return(Ref(x, 37), isRef: true)),
            "alias-of-a-local" => HandBuiltModel.Block(
                Declare(x, HandBuiltModel.Literal(f.Integer, 1L)), Declare(alias, Ref(x)), Return(Ref(alias, 37), isRef: true)),
            "value-parameter" => HandBuiltModel.Block(Return(Ref(Parameter("p", f.Integer), 37), isRef: true)),
            _ => HandBuiltModel.Block(Declare(st, f.Stackalloc()), Return(f.Element(Ref(st), 37), isRef: true)),
        };

        AssertSite(f.Model.Analyze(new EscapeAnalyzer(), body), "MIST_PA_REF_RETURN_ESCAPES", $"it refers to {origin},");
    }

    [Theory]
    [InlineData("alias-of-a-class-field")]
    [InlineData("alias-of-a-ref-parameter")]
    [InlineData("static-field")]
    [InlineData("element-of-a-span-parameter")]
    public void AReferenceReturnedToStorageThatOutlivesTheCallIsAdmitted(string shape)
    {
        var alias = Local("alias", f.Integer, isRef: true);
        var body = shape switch
        {
            "alias-of-a-class-field" => HandBuiltModel.Block(
                Declare(alias, Member(Ref(Parameter("box", f.Box)), f.Value)), Return(Ref(alias), isRef: true)),
            "alias-of-a-ref-parameter" => HandBuiltModel.Block(
                Declare(alias, Ref(Parameter("p", f.Integer, byRef: true))), Return(Ref(alias), isRef: true)),
            "static-field" => HandBuiltModel.Block(Return(new FieldReferenceExpression(HandBuiltModel.Node(), f.Shared), isRef: true)),
            _ => HandBuiltModel.Block(Return(f.Element(Ref(Parameter("s", f.Span))), isRef: true)),
        };

        Assert.Empty(f.Model.Analyze(new EscapeAnalyzer(), body));
    }

    [Fact]
    public void FrameBoundValuesUsedWithinTheFrameAreAdmitted()
    {
        var st = Local("st", f.Span);
        var other = Local("other", f.Span);
        var h = Local("h", f.Holder);
        var heap = Local("heap", f.Holder);
        var body = HandBuiltModel.Block(
            Declare(st, f.Stackalloc()),
            Declare(other, f.Stackalloc()),
            Declare(h, f.NewHolder(Ref(st))),
            Run(Assign(Member(Ref(h), f.Data), Ref(other))),
            Run(Call(f.Put, Ref(h), 0, Ref(other))),
            Declare(heap, f.NewHolder(Ref(Parameter("p", f.Span)))),
            Run(Call(f.Look, Ref(heap), 0, Ref(st))),
            Return(Ref(heap)));

        Assert.Empty(f.Model.Analyze(new EscapeAnalyzer(), body));
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("readonly-alias")]
    public void AReferenceThePositionRuleRefusesGetsNoSecondComplaint(string shape)
    {
        var view = new LocalVariable("view", f.Integer) { IsRef = true, IsReadonly = true };
        var x = Local("x", f.Integer);
        var body = shape == "literal"
            ? HandBuiltModel.Block(Return(HandBuiltModel.Literal(f.Integer, 5L), isRef: true))
            : HandBuiltModel.Block(Declare(x, HandBuiltModel.Literal(f.Integer, 1L)), Declare(view, Ref(x)), Return(Ref(view), isRef: true));

        Assert.Empty(f.Model.Analyze(new EscapeAnalyzer(), body));
    }

    private static void AssertSite(IReadOnlyList<Diagnostic> diagnostics, string expected, string message)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expected, diagnostic.Id.Value);
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }
}