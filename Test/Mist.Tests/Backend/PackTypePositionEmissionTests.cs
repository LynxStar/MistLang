using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: how a pack written in a type position is spelled in each specialization. Asserted on the
// emitted text, since a pack name left in a signature fails only in Roslyn.
[Trait("Tier", "3")]
public sealed class PackTypePositionEmissionTests
{
    private const string Types = """
        public class Plain<params TArgs>
        {
            public (TArgs...) Kept;

            public Plain((TArgs...) whole) { Kept = whole; }

            public (TArgs...) Whole()
            {
                (TArgs...) copy = Kept;
                return copy;
            }

            public int Take((TArgs...) whole) { return TArgs.Length; }
            public int Both(params TArgs flat, (TArgs...) whole) { return TArgs.Length; }
            public int Relay(params TArgs args) { return this.Take(args...); }
        }

        public class Indexed<Type[2]:TArgs>
        {
            public TArgs[0] First;
            public TArgs[1] Second;

            public Indexed(TArgs[0] first, TArgs[1] second) { First = first; Second = second; }

            public TArgs[1] Copy()
            {
                TArgs[1] copy = Second;
                return copy;
            }
        }

        public class Through<Type[2]:TBoth>
        {
            public Indexed<TBoth...> Inner;

            public Through(TBoth[0] first, TBoth[1] second) { Inner = new Indexed<TBoth...>(first, second); }

            public TBoth[1] Last() { return Inner.Second; }
        }
        """;

    private const string Closings = """
        var plain = new Plain<int, string>((1, "x"));
        var taken = plain.Take((2, "y"));
        var both = plain.Both(3, "z", (4, "w"));
        var indexed = new Indexed<int, string>(7, "seven");
        var second = indexed.Second;
        var last = new Through<bool, string>(true, "last").Last();
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [InlineData(2, "public (T1, T2) Kept;")]
    [InlineData(2, "public Plain_2((T1, T2) whole)")]
    [InlineData(2, "public (T1, T2) Whole()")]
    [InlineData(2, "(T1, T2) copy = this.Kept;")]
    [InlineData(2, "public int Take((T1, T2) whole)")]
    [InlineData(2, "public int Both((T1, T2) flat, (T1, T2) whole)")]
    [InlineData(2, "return this.Take(args);")]
    [InlineData(1, "public int Take(System.ValueTuple<T1> whole)")]
    [InlineData(0, "public int Take(System.ValueTuple whole)")]
    public void ATupleOverAPack_IsSpelledWithTheSpecializationsElements(int width, string line)
    {
        Assert.Contains(line, Lines(Emit("Plain", [width])));
    }

    [Theory]
    [InlineData("int taken = plain.Take((2, \"y\"));")]
    [InlineData("int both = plain.Both((3, \"z\"), (4, \"w\"));")]
    public void ATupleParameterOverAPack_IsPassedTheTupleTheCallWrote(string line)
    {
        Assert.Contains(line, Lines(Emit("Probe", widths: null)));
    }

    [Theory]
    [InlineData("Indexed", "public Indexed_2(T1 first, T2 second)")]
    [InlineData("Indexed", "public T2 Copy()")]
    [InlineData("Indexed", "T2 copy = this.Second;")]
    [InlineData("Through", "public Probe.Indexed_2<T1, T2> Inner;")]
    [InlineData("Through", "public Through_2(T1 first, T2 second)")]
    [InlineData("Through", "public T2 Last()")]
    public void AnIndexedPackElement_IsSpelledAsTheSpecializationsParameterAtThatIndex(string typeName, string line)
    {
        Assert.Contains(line, Lines(Emit(typeName, [2])));
    }

    [Theory]
    [InlineData("string second = indexed.Second;")]
    [InlineData("string last = new Probe.Through_2<bool, string>(true, \"last\").Last();")]
    public void AnIndexedPackElement_IsTheClosingsTypeAtTheUse(string line)
    {
        Assert.Contains(line, Lines(Emit("Probe", widths: null)));
    }

    private static string Emit(string typeName, IReadOnlyList<int>? widths)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);

        using(widths is null ? null : Monomorph.For(declared.TypeParametersList, widths)?.Enter())
        {
            TypeEmitter.EmitTypeBody(
                declared, ConstraintClosureMap.Build(model, diagnostics), MonomorphSet.Discover(model).Methods, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}