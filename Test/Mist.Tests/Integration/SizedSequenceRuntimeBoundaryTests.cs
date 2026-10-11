using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Integration;

public sealed class SizedSequenceRuntimeBoundaryTests
{
    [Fact]
    [Trait("Tier", "4")]
    public void ModelToEmitterToRoslynToRuntime_EnforcesErasedLengthsAtEveryNarrowingSite()
    {
        var three = ArrayOf(IntegerType, 3);
        var value = PropertyPatternEmissionTests.Reference("value", UnknownType.Instance);
        var next = new MemberAccessExpression(null!,
            PropertyPatternEmissionTests.Reference("producer", PropertyPatternEmissionTests.Type("Producer")), "Next", UnknownType.Instance);
        var holder = PropertyPatternEmissionTests.Type("Holder");
        var data = new LocalVariable("d", three);
        var positional = new TuplePattern(null!, [Bound(three, "a"), Bound(StringType, "s")], TupleOf(three, StringType), TupleOf(three, StringType), null);

        var harness = $$"""
            public sealed class Holder
            {
                public Holder(int[] data) { Data = data; }
                public int[] Data { get; }
            }
            public sealed class Producer
            {
                private readonly object value;
                public Producer(object value) { this.value = value; }
                public int Reads;
                public object Next { get { Reads++; return value; } }
            }
            public static class Probe
            {
                private static bool Sized(object value) { return {{Emit(Is(value, Bound(three, "bound")), "bound")}}; }
                private static bool Zero(object value) { return {{Emit(Is(value, Bound(ArrayOf(IntegerType, 0), "bound")), null)}}; }
                private static int Arms(object value) { return {{Emit(new SwitchExpression(null!, value,
                    [Arm(Bound(three, "a"), 3), Arm(Bound(ArrayOf(IntegerType, 5), "b"), 5), Arm(Bound(ArrayOf(IntegerType, null), "c"), 0),
                     Arm(new CatchAllPattern(null!, UnknownType.Instance, null), -1)], IntegerType), null)}}; }
                private static bool SpanTest(System.Span<int> value) { return {{Emit(Is(Local("value", SpanOf(IntegerType, null)), Bound(SpanOf(IntegerType, 3), "bound")), "bound")}}; }
                private static bool ReadOnlySpanTest(System.ReadOnlySpan<int> value) { return {{Emit(Is(Local("value", ReadOnlySpanOf(IntegerType, null)), Bound(ReadOnlySpanOf(IntegerType, 3), "bound")), "bound")}}; }
                private static int SpanArms(System.Span<int> value) { return {{Emit(new SwitchExpression(null!, Local("value", SpanOf(IntegerType, null)),
                    [Arm(Bound(SpanOf(IntegerType, 3), "a"), 3), Arm(Bound(SpanOf(IntegerType, 5), "b"), 5),
                     Arm(new CatchAllPattern(null!, SpanOf(IntegerType, null), null), 0)], IntegerType), null)}}; }
                private static bool Whole(object value) { return {{Emit(Is(value, Bound(TupleOf(three, StringType), "whole")), null)}}; }
                private static bool Positional(object value) { return {{Emit(Is(value, positional), "a")}}; }
                private static bool Nested(object value) { return {{Emit(Is(value, Bound(TupleOf(TupleOf(three, StringType), IntegerType), "whole")), null)}}; }
                private static bool Aliased(object value) { return {{Emit(Is(value, new PropertyPattern(null!, AliasOf("Three", three), [],
                    new LocalVariable("bound", three), new TypeTestPattern(null!, AliasOf("Three", three), null))), "bound")}}; }
                private static bool Contained(object value) { return {{Emit(Is(value, new PropertyPattern(null!, holder,
                    [new PropertySubpattern(null!, "Data", new TypeTestPattern(null!, three, data), ArrayOf(IntegerType, null), new NotFoundResult())],
                    null, new TypeTestPattern(null!, holder, null))), "d")}}; }
                private static int[] AsArray(Producer producer) { return {{Emit(As(next, three), null)}}; }
                private static (int[], string)? AsTuple(Producer producer) { return {{Emit(As(next, TupleOf(three, StringType)), null)}}; }
                public static int[] Run()
                {
                    var results = new System.Collections.Generic.List<int>();
                    results.Add(Sized(new int[3]) ? 1 : 0);
                    results.Add(Sized(new int[5]) ? 1 : 0);
                    results.Add(Sized(new int[0]) ? 1 : 0);
                    results.Add(Sized("text") ? 1 : 0);
                    results.Add(Sized(null) ? 1 : 0);
                    results.Add(Zero(new int[0]) ? 1 : 0);
                    results.Add(Zero(new int[3]) ? 1 : 0);
                    results.Add(Arms(new int[3]));
                    results.Add(Arms(new int[5]));
                    results.Add(Arms(new int[7]));
                    results.Add(Arms("text"));
                    results.Add(SpanTest(new int[3]) ? 1 : 0);
                    results.Add(SpanTest(new int[5]) ? 1 : 0);
                    results.Add(ReadOnlySpanTest(new int[3]) ? 1 : 0);
                    results.Add(ReadOnlySpanTest(new int[5]) ? 1 : 0);
                    results.Add(SpanArms(new int[5]));
                    results.Add(SpanArms(new int[2]));
                    results.Add(Whole((new int[3], "x")) ? 1 : 0);
                    results.Add(Whole((new int[5], "x")) ? 1 : 0);
                    results.Add(Positional((new int[3], "x")) ? 1 : 0);
                    results.Add(Positional((new int[5], "x")) ? 1 : 0);
                    results.Add(Nested(((new int[3], "x"), 1)) ? 1 : 0);
                    results.Add(Nested(((new int[5], "x"), 1)) ? 1 : 0);
                    results.Add(Aliased(new int[3]) ? 1 : 0);
                    results.Add(Aliased(new int[5]) ? 1 : 0);
                    results.Add(Contained(new Holder(new int[3])) ? 1 : 0);
                    results.Add(Contained(new Holder(new int[5])) ? 1 : 0);
                    var hit = new Producer(new int[3]);
                    results.Add(AsArray(hit)?.Length ?? -1);
                    results.Add(hit.Reads);
                    var miss = new Producer(new int[5]);
                    results.Add(AsArray(miss)?.Length ?? -1);
                    results.Add(miss.Reads);
                    var pair = new Producer((new int[3], "x"));
                    results.Add(AsTuple(pair)?.Item1.Length ?? -1);
                    results.Add(pair.Reads);
                    var shortPair = new Producer((new int[5], "x"));
                    results.Add(AsTuple(shortPair)?.Item1.Length ?? -1);
                    results.Add(AsTuple(new Producer("text")) is null ? 1 : 0);
                    results.Add(AsArray(new Producer(null)) is null ? 1 : 0);
                    results.Add(AsTuple(new Producer(null)) is null ? 1 : 0);
                    results.Add(shortPair.Reads);
                    return results.ToArray();
                }
            }
            """;

        Assert.Equal(
            [1, 0, 0, 0, 0, 1, 0, 3, 5, 0, -1, 1, 0, 1, 0, 5, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 3, 1, -1, 1, 3, 1, -1, 1, 1, 1, 1],
            Run(harness, "SizedSequenceRuntime"));
    }

    [Fact]
    [Trait("Tier", "4")]
    public void ModelToEmitterToRoslynToRuntime_KeepsErasedConstituentsAndWideTupleElementsApart()
    {
        var three = ArrayOf(IntegerType, 3);
        var five = ArrayOf(IntegerType, 5);
        var sized = new UnionType([three, five]);
        var wide = new UnionType([three, five, StringType]);
        var carriers = new[] { sized, wide }.ToDictionary(UnionCarrierNames.CarrierName);
        var held = Local("value", sized);
        var narrowed = new SwitchExpression(null!, held,
            [Arm(new TypeTestPattern(null!, three, new LocalVariable("a", three), three), 3),
             Arm(new TypeTestPattern(null!, five, new LocalVariable("b", five), five), 5)], IntegerType);
        var widened = new SwitchExpression(null!, Local("value", wide),
            [Arm(new TypeTestPattern(null!, three, new LocalVariable("a", three), three), 3),
             Arm(new TypeTestPattern(null!, five, new LocalVariable("b", five), five), 5),
             Arm(new TypeTestPattern(null!, StringType, new LocalVariable("s", StringType), StringType), -1)], IntegerType);
        var nine = TupleOf([.. Enumerable.Repeat<IMistType>(IntegerType, 7), three, StringType]);
        var fifteen = TupleOf([.. Enumerable.Repeat<IMistType>(IntegerType, 14), ArrayOf(IntegerType, 2)]);

        var harness = $$"""
            {{string.Join("\n", carriers.Select(carrier => Carrier(carrier.Key, carrier.Value, carriers)))}}
            public static class Probe
            {
                private static {{UnionCarrierNames.CarrierName(sized)}} Three(int[] three) { return {{Entering(Local("three", three), sized)}}; }
                private static {{UnionCarrierNames.CarrierName(sized)}} Five(int[] five) { return {{Entering(Local("five", five), sized)}}; }
                private static int Narrow({{UnionCarrierNames.CarrierName(sized)}} value) { return {{Emit(narrowed, null)}}; }
                private static bool IsFive({{UnionCarrierNames.CarrierName(sized)}} value) { return {{Emit(Is(held, new TypeTestPattern(null!, five, new LocalVariable("b", five), five)), null)}}; }
                private static int Widen({{UnionCarrierNames.CarrierName(sized)}} narrow) { {{UnionCarrierNames.CarrierName(wide)}} value = narrow; return {{Emit(widened, null)}}; }
                private static bool Nine(object value) { return {{Emit(Is(Local("value", UnknownType.Instance), Bound(nine, "whole")), null)}} && whole.Item9 == "x"; }
                private static bool Fifteen(object value) { return {{Emit(Is(Local("value", UnknownType.Instance), Bound(fifteen, "whole")), null)}} && whole.Item15.Length == 2; }
                public static int[] Run()
                {
                    var results = new System.Collections.Generic.List<int>();
                    results.Add(Narrow(Three(new int[3])));
                    results.Add(Narrow(Five(new int[5])));
                    results.Add(IsFive(Five(new int[5])) ? 1 : 0);
                    results.Add(IsFive(Three(new int[3])) ? 1 : 0);
                    results.Add(Widen(Three(new int[3])));
                    results.Add(Widen(Five(new int[5])));
                    results.Add(Nine((1, 2, 3, 4, 5, 6, 7, new int[3], "x")) ? 1 : 0);
                    results.Add(Nine((1, 2, 3, 4, 5, 6, 7, new int[5], "x")) ? 1 : 0);
                    results.Add(Nine((1, 2, 3, 4, 5, 6, 7, 8, 9)) ? 1 : 0);
                    results.Add(Fifteen((1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, new int[2])) ? 1 : 0);
                    results.Add(Fifteen((1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, new int[3])) ? 1 : 0);
                    return results.ToArray();
                }
            }
            """;

        Assert.Equal([3, 5, 1, 0, 3, 5, 1, 0, 0, 1, 0], Run(harness, "ErasedConstituentRuntime"));
    }

    private static string Carrier(string name, UnionType union, IReadOnlyDictionary<string, UnionType> carriers)
    {
        var writer = new CSharpWriter();
        UnionCarrierEmitter.Emit(name, union, carriers, writer);
        return writer.ToString();
    }

    private static string Entering(Expression value, IMistType slot)
    {
        var writer = new CSharpWriter();
        BodyEmitter.EmitEntering(value, slot, writer, new DiagnosticBag());
        return writer.ToString();
    }

    private static int[] Run(string harness, string name)
    {
        var compilation = CSharpCompilation.Create(
            name + "_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(harness, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;
        var context = new AssemblyLoadContext(name, isCollectible: true);

        try
        {
            var run = context.LoadFromStream(stream).GetType("Probe")!.GetMethod("Run")!;
            return Assert.IsType<int[]>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Emit(Expression expression, string? bound)
    {
        var emitted = PropertyPatternEmissionTests.Emit(expression);

        return bound is null ? emitted : $"{emitted} && {bound}.Length == 3";
    }

    private static IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(null!, scrutinee, pattern, BooleanType);
    }

    private static AsExpression As(Expression operand, IMistType target)
    {
        return new AsExpression(HandBuiltModel.Node(7), operand, target, AsConversionKind.Narrowing, null, OptionalOf(target));
    }

    private static TypeTestPattern Bound(IMistType tested, string binding)
    {
        return new TypeTestPattern(null!, tested, new LocalVariable(binding, tested));
    }

    private static SwitchArm Arm(Pattern pattern, int result)
    {
        return new SwitchArm(null!, pattern, null, PropertyPatternEmissionTests.Number(result));
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return PropertyPatternEmissionTests.Reference(name, type);
    }
}