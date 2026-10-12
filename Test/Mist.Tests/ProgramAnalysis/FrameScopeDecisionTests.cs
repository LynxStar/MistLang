using System.Collections.Immutable;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FrameScopeDecisionTests
{
    private readonly FrameFragments f = new();
    private readonly LocalVariable stack;
    private readonly LocalVariable heap;
    private readonly ImmutableDictionary<LocalVariable, LocalFrame> locals;

    public FrameScopeDecisionTests()
    {
        stack = FrameFragments.Local("st", f.Span);
        heap = FrameFragments.Local("heap", f.Span);
        locals = FrameLifetimes.Nothing.SetItem(stack, new LocalFrame(FrameScopes.StackMemory, null));
    }

    [Theory]
    [InlineData("stackalloc")]
    [InlineData("stack-local")]
    [InlineData("holder-of-stack")]
    [InlineData("field-of-stack-holder")]
    [InlineData("slice-of-stack")]
    [InlineData("switch-with-a-stack-arm")]
    public void AValueBuiltFromStackMemoryRefersToIt(string shape)
    {
        var slice = new Method("Slice", f.Span, [new Parameter("start", f.Integer)], default);
        Expression value = shape switch
        {
            "stackalloc" => f.Stackalloc(),
            "stack-local" => FrameFragments.Ref(stack),
            "holder-of-stack" => f.NewHolder(FrameFragments.Ref(stack)),
            "field-of-stack-holder" => FrameFragments.Member(f.NewHolder(FrameFragments.Ref(stack)), f.Data),
            "slice-of-stack" => FrameFragments.Call(slice, FrameFragments.Ref(stack), 0, HandBuiltModel.Literal(f.Integer, 1L)),
            _ => new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(f.Integer), [
                new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), f.Integer, null), null, FrameFragments.Ref(heap)),
                new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), f.Integer, null), null, FrameFragments.Ref(stack)),
            ], f.Span),
        };

        Assert.Equal(FrameScopes.StackMemory, FrameScopes.Value(value, locals));
    }

    [Theory]
    [InlineData("heap-local")]
    [InlineData("parameter")]
    [InlineData("holder-of-parameter")]
    [InlineData("length-of-stack")]
    public void AValueNotTiedToTheFrameRefersToNothingInIt(string shape)
    {
        var length = new Method("Count", f.Integer, [], default);
        Expression value = shape switch
        {
            "heap-local" => FrameFragments.Ref(heap),
            "parameter" => FrameFragments.Ref(FrameFragments.Parameter("p", f.Span)),
            "holder-of-parameter" => f.NewHolder(FrameFragments.Ref(FrameFragments.Parameter("p", f.Span))),
            _ => FrameFragments.Call(length, FrameFragments.Ref(stack), 0),
        };

        Assert.Null(FrameScopes.Value(value, locals));
    }

    [Fact]
    public void ARefStructBuiltOverALentLocalRefersToThatLocal()
    {
        var pin = new ProgramModel.Model.Types.StructType("Pin", "Probe", new ProgramModel.Model.Scopes.Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { IsRef = true },
        };
        var parameter = FrameFragments.Parameter("x", f.Integer, byRef: true).Parameter!;
        var construct = new Constructor("Pin", pin, [parameter], default);
        var lent = new NewExpression(HandBuiltModel.Node(), pin, [FrameFragments.Ref(FrameFragments.Local("n", f.Integer))], construct, [], [f.Integer]);

        Assert.Equal("local 'n'", FrameScopes.Value(lent, locals));
    }

    [Theory]
    [InlineData("local", "local 'x'")]
    [InlineData("value-parameter", "parameter 'p'")]
    [InlineData("struct-local-field", "local 's'")]
    [InlineData("struct-this-field", "the receiver 'this' of struct 'Point'")]
    [InlineData("stack-element", FrameScopes.StackMemory)]
    [InlineData("ref-call-over-a-local", "local 'n'")]
    public void AReferenceToFrameStorageNamesIt(string shape, string expected)
    {
        var pick = f.RefReturning("Pick", FrameFragments.Parameter("a", f.Integer, byRef: true).Parameter!);
        Expression reference = shape switch
        {
            "local" => FrameFragments.Ref(FrameFragments.Local("x", f.Integer)),
            "value-parameter" => FrameFragments.Ref(FrameFragments.Parameter("p", f.Integer)),
            "struct-local-field" => FrameFragments.Member(FrameFragments.Ref(FrameFragments.Local("s", f.Point)), f.Point.FieldsList[0]),
            "struct-this-field" => new FieldReferenceExpression(HandBuiltModel.Node(), f.Point.FieldsList[0]),
            "stack-element" => f.Element(FrameFragments.Ref(stack)),
            _ => FrameFragments.Call(pick, null, 0, FrameFragments.Ref(FrameFragments.Local("n", f.Integer))),
        };

        Assert.Equal(expected, FrameScopes.Referent(reference, locals));
    }

    [Theory]
    [InlineData("ref-parameter")]
    [InlineData("class-field")]
    [InlineData("static-field")]
    [InlineData("array-element")]
    [InlineData("span-parameter-element")]
    [InlineData("ref-call-over-a-ref-parameter")]
    public void AReferenceToStorageThatOutlivesTheCallNamesNothing(string shape)
    {
        var pick = f.RefReturning("Pick", FrameFragments.Parameter("a", f.Integer, byRef: true).Parameter!);
        Expression reference = shape switch
        {
            "ref-parameter" => FrameFragments.Ref(FrameFragments.Parameter("p", f.Integer, byRef: true)),
            "class-field" => FrameFragments.Member(FrameFragments.Ref(FrameFragments.Local("b", f.Box)), f.Value),
            "static-field" => new FieldReferenceExpression(HandBuiltModel.Node(), f.Shared),
            "array-element" => new IndexExpression(HandBuiltModel.Node(), FrameFragments.Ref(FrameFragments.Local("a", f.Array)), HandBuiltModel.Literal(f.Integer, 0L), f.Integer),
            "span-parameter-element" => f.Element(FrameFragments.Ref(FrameFragments.Parameter("p", f.Span))),
            _ => FrameFragments.Call(pick, null, 0, FrameFragments.Ref(FrameFragments.Parameter("q", f.Integer, byRef: true))),
        };

        Assert.Null(FrameScopes.Referent(reference, locals));
    }

    [Theory]
    [InlineData("heap-local", "local 'heap'")]
    [InlineData("parameter", "parameter 'p'")]
    [InlineData("field-of-this", "field 'Data' of 'this'")]
    [InlineData("field-of-a-heap-holder", "field 'Data' of local 'h'")]
    public void StorageThatOutlivesTheFrameIsADestination(string shape, string expected)
    {
        Expression target = shape switch
        {
            "heap-local" => FrameFragments.Ref(heap),
            "parameter" => FrameFragments.Ref(FrameFragments.Parameter("p", f.Span)),
            "field-of-this" => new FieldReferenceExpression(HandBuiltModel.Node(), f.Data),
            _ => FrameFragments.Member(FrameFragments.Ref(FrameFragments.Local("h", f.Holder)), f.Data),
        };

        Assert.Equal(expected, FrameScopes.Destination(target, locals)?.Where);
    }

    [Fact]
    public void StorageDeclaredFromTheFrameIsNotADestination()
    {
        var holder = FrameFragments.Local("h", f.Holder);
        var frames = locals.SetItem(holder, new LocalFrame(FrameScopes.StackMemory, null));

        Assert.Null(FrameScopes.Destination(FrameFragments.Ref(stack), frames));
        Assert.Null(FrameScopes.Destination(FrameFragments.Member(FrameFragments.Ref(holder), f.Data), frames));
    }

    [Fact]
    public void ADeclarationFixesWhatItsLocalAndAliasReferTo()
    {
        var lattice = new FrameLifetimes(new Dictionary<Pattern, Expression>());
        var span = FrameFragments.Local("s", f.Span);
        var alias = FrameFragments.Local("e", f.Integer, isRef: true);

        var state = lattice.Transfer(FrameLifetimes.Nothing, Declared(span, f.Stackalloc()));
        state = lattice.Transfer(state, Declared(alias, f.Element(FrameFragments.Ref(span))));
        state = lattice.Transfer(state, Declared(heap, FrameFragments.Ref(FrameFragments.Parameter("p", f.Span))));

        Assert.Equal(new LocalFrame(FrameScopes.StackMemory, null), state[span]);
        Assert.Equal(FrameScopes.StackMemory, state[alias].Referent);
        Assert.False(state.ContainsKey(heap));
    }

    [Fact]
    public void APatternBindingTakesItsScrutineesFrame()
    {
        var three = FrameFragments.Local("three", f.Span);
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), f.Span, three);
        var lattice = new FrameLifetimes(new Dictionary<Pattern, Expression>(ReferenceEqualityComparer.Instance) { [pattern] = FrameFragments.Ref(stack) });

        var state = lattice.Transfer(locals, new FlowStep(FlowStepKind.Bind, pattern, three));

        Assert.Equal(FrameScopes.StackMemory, state[three].Value);
    }

    [Fact]
    public void AJoinKeepsTheNarrowerFact()
    {
        var lattice = new FrameLifetimes(new Dictionary<Pattern, Expression>());
        var other = FrameLifetimes.Nothing.SetItem(stack, new LocalFrame(null, "local 'x'"));

        var joined = lattice.Join(other, locals);

        Assert.Equal(new LocalFrame(FrameScopes.StackMemory, "local 'x'"), joined[stack]);
    }

    private static FlowStep Declared(LocalVariable local, Expression initializer)
    {
        return new FlowStep(FlowStepKind.Declare, FrameFragments.Declare(local, initializer), local);
    }
}