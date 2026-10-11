using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class LocalBindingContractEmissionTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void HiddenNamesAvoidLocalsParametersAndLocalFunctions()
    {
        var first = Discard(f.Int32);
        var second = Discard(f.Int32);
        var function = new Method("__discard_2", f.Void, [], default) { Body = HandBuiltModel.Block() };
        var body = HandBuiltModel.Block(
            Declare(new LocalVariable("__discard_1", f.Int32), HandBuiltModel.Literal(f.Int32, 1L)),
            Declare(first, HandBuiltModel.Literal(f.Int32, 2L)),
            new LocalFunctionStatement(function, []),
            Declare(second, HandBuiltModel.Literal(f.Int32, 3L)));

        using var names = LocalNames.Enter(body, ["__discard_3"]);

        Assert.Equal("__discard_4", LocalNames.Of(first));
        Assert.Equal("__discard_5", LocalNames.Of(second));
        Assert.Equal("__discard_2", LocalNames.Of(function));
    }

    [Fact]
    public void OrdinaryAndLoopDiscardsEmitOneInitializerInSourceOrderUnderDistinctNames()
    {
        var record = new Method("Record", f.Int32, [new Parameter("id", f.Int32)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = f.Sink };
        var first = Discard(f.Int32);
        var second = Discard(f.Int32);
        var counted = Discard(f.Int32);
        var iteration = Discard(f.Int32);
        var inner = Discard(f.Int32);
        var body = HandBuiltModel.Block(
            Declare(first, f.Call(record, HandBuiltModel.Literal(f.Int32, 1L))),
            Declare(second, f.Call(record, HandBuiltModel.Literal(f.Int32, 2L))),
            new ForStatement(Declare(counted, f.Call(record, HandBuiltModel.Literal(f.Int32, 3L))),
                HandBuiltModel.Literal(f.Boolean, false), null, HandBuiltModel.Block()),
            new ForEachStatement(iteration, Array(4),
                HandBuiltModel.Block(Declare(inner, f.Call(record, HandBuiltModel.Literal(f.Int32, 5L))))));

        Assert.Equal(
            """
            int __discard_1 = Sink.Record(1);
            _ = __discard_1;
            int __discard_2 = Sink.Record(2);
            _ = __discard_2;
            for (int __discard_3 = Sink.Record(3); __discard_3 is var _ && (false); )
            {
            }
            foreach (int __discard_4 in new int[] { 4 })
            {
                int __discard_5 = Sink.Record(5);
                _ = __discard_5;
            }
            """.ReplaceLineEndings("\n"),
            Emit(body));
    }

    [Fact]
    public void HiddenDiscardsRetainTheExistingCleanupForEachSemanticLocal()
    {
        var first = Discard(f.Token);
        var second = Discard(f.Token);

        Assert.Equal(
            """
            Token __discard_1 = new Token(1);
            _ = __discard_1;
            try
            {
                Token __discard_2 = new Token(2);
                _ = __discard_2;
                try
                {
                }
                finally
                {
                    __discard_2.Drop();
                }
            }
            finally
            {
                __discard_1.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emit(HandBuiltModel.Block(Declare(first, f.New(1)), Declare(second, f.New(2)))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantDiscardsAreReadWithoutReevaluatingTheirInitializers(bool conditionless)
    {
        var ordinary = Declare(Discard(f.Int32), HandBuiltModel.Literal(f.Int32, 3L));
        var counted = Declare(Discard(f.Int32), HandBuiltModel.Literal(f.Int32, 0L));
        var loop = new ForStatement(counted,
            conditionless ? null : HandBuiltModel.Literal(f.Boolean, false), null, HandBuiltModel.Block());
        var condition = conditionless ? "__discard_2 is var _" : "__discard_2 is var _ && (false)";

        Assert.Equal($"int __discard_1 = 3;\n_ = __discard_1;\n"
            + $"for (int __discard_2 = 0; {condition}; )\n{{\n}}",
            Emit(HandBuiltModel.Block(ordinary, loop)));
    }

    [Fact]
    public void ForeachBodyShadowAndItsCaptureUseNamesDistinctFromTheIteratorAndCollection()
    {
        var collection = new LocalVariable("x", Array(1).Type);
        var iteration = new LocalVariable("x", f.Int32) { IsReadonly = true };
        var shadow = new LocalVariable("x", f.Int32);
        var read = new Method("Read", f.Int32, [], default)
        {
            Body = HandBuiltModel.Block(Return(Ref(shadow))),
        };
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), read),
            [], f.Int32, read, []);
        var body = HandBuiltModel.Block(
            Declare(collection, Array(1)),
            new ForEachStatement(iteration, Ref(collection), HandBuiltModel.Block(
                Declare(shadow, HandBuiltModel.Literal(f.Int32, 2L)),
                new LocalFunctionStatement(read, []),
                Declare(new LocalVariable("captured", f.Int32), call))));

        Assert.Equal(
            """
            int[] x = new int[] { 1 };
            foreach (int x_1 in x)
            {
                int x_2 = 2;
                int Read()
                {
                    return x_2;
                }
                int captured = Read();
            }
            """.ReplaceLineEndings("\n"),
            Emit(body));
    }

    [Fact]
    public void SiblingForeachIteratorsKeepTheirIndependentSourceNames()
    {
        var first = new LocalVariable("item", f.Int32) { IsReadonly = true };
        var second = new LocalVariable("item", f.Int32) { IsReadonly = true };
        var body = HandBuiltModel.Block(
            new ForEachStatement(first, Array(1), HandBuiltModel.Block()),
            new ForEachStatement(second, Array(2), HandBuiltModel.Block()));

        using var names = LocalNames.Enter(body, []);

        Assert.Equal("item", LocalNames.Of(first));
        Assert.Equal("item", LocalNames.Of(second));
    }

    [Fact]
    public void TupleAndPatternDiscardsKeepTheirEstablishedUnderscoreSpelling()
    {
        var tupleDiscard = new LocalVariable("_", f.Int32);
        var kept = new LocalVariable("kept", f.Int32);
        var tupleType = f.Model.Interner.GetOrCreateTupleType([f.Int32, f.Int32]);
        var tuple = new TupleLiteralExpression(HandBuiltModel.Node(),
            [HandBuiltModel.Literal(f.Int32, 1L), HandBuiltModel.Literal(f.Int32, 2L)], [null, null], tupleType);
        var pattern = new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(f.Int32, 3L),
            new CatchAllPattern(HandBuiltModel.Node(), f.Int32, null), f.Boolean);
        var body = HandBuiltModel.Block(
            Declare(Discard(f.Int32), HandBuiltModel.Literal(f.Int32, 0L)),
            new TupleDeconstructionStatement([tupleDiscard, kept], tuple),
            Declare(new LocalVariable("matched", f.Boolean), pattern));

        Assert.Equal(
            """
            int __discard_1 = 0;
            _ = __discard_1;
            (int _, int kept) = (1, 2);
            bool matched = 3 is var _;
            """.ReplaceLineEndings("\n"),
            Emit(body));
    }

    private static LocalVariable Discard(IMistType type)
    {
        return new LocalVariable("_", type) { IsDiscard = true };
    }

    private ArrayLiteralExpression Array(int value)
    {
        var type = f.Model.Interner.GetOrCreateConstructedType(f.Model.Type("Array<,>"), [f.Int32]);
        return new ArrayLiteralExpression(HandBuiltModel.Node(), f.Int32, [HandBuiltModel.Literal(f.Int32, (long)value)], type);
    }

    private static string Emit(Block body)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitBlock(body, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().ReplaceLineEndings("\n").Trim();
    }
}