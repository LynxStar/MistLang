using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
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

        var compilation = CSharpCompilation.Create(
            "SizedSequenceRuntime_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(harness, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(d => d.ToString())));
        stream.Position = 0;
        var context = new AssemblyLoadContext("SizedSequenceRuntime", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetType("Probe")!.GetMethod("Run")!;
            var results = Assert.IsType<int[]>(run.Invoke(null, null));

            Assert.Equal(
                [1, 0, 0, 0, 0, 1, 0, 3, 5, 0, -1, 1, 0, 1, 0, 5, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 3, 1, -1, 1, 3, 1, -1, 1, 1, 1, 1],
                results);
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