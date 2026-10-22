using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: the default a specialization gives storage typed by a pack element, and what a default
// constraint on a pack lowers to. Asserted on the emitted text: a zero left unspelled (`this.First = ;`)
// and a `where` naming the pack fail only in Roslyn.
[Trait("Tier", "3")]
public sealed class PackElementDefaultEmissionTests
{
    private const string Marker = "Mist.Emission.CSharp.__DefaultInit";

    private const string Types = """
        public abstract INamed { string Name(); }
        public class Cat : INamed { public string Name() { return "cat"; } }
        public class Widget { public int Size = 3; }

        public struct Pair<Type[2]:TArgs>
        {
            public TArgs[0] First;
            public TArgs[1] Second;
        }

        public class Crate<Type[2]:TArgs>
        {
            public TArgs[0] First;
            public TArgs[1] Kept { get; set; }
        }

        public class Blocked<Type[2]:TArgs>
        {
            public TArgs[0] First;
            public int Count;

            public Blocked(int count) { Count = count; }

            default
            {
                Count = 5
            }
        }

        public struct Holder<Type[2]:TBoth>
        {
            public Pair<TBoth...> Inner;
            public TBoth[1] Own;
        }

        public struct Flat : Pair<int, string> { public int Extra; }
        public class Derived : Crate<bool, string> { public int Extra; }

        public class Values<Type[2]:TArgs> where TArgs : struct
        {
            public TArgs[0] First;
            public TArgs[1]? Maybe;
        }

        public class Calls
        {
            public static T[1] Zero<Type[2]:T>() where T : default { return default(T[1]); }
            public static int Zeroed<params T>(params T args) where T : default { return T.Length; }
            public static int Defaulted<params T>(params T args) where T : default { return Zeroed(args...); }
            public static string Both<Type[2]:T>(T[0] a, T[1] b) where T : INamed { return a.Name(); }
        }
        """;

    private const string Closings = """
        var pair = default(Pair<int, string>);
        var keeper = default(Holder<Widget, string>);
        var blocked = default(Blocked<string, int>);
        var values = default(Values<int, bool>);
        var zero = Calls.Zero<[int, string]>();
        var defaulted = Calls.Defaulted(1, "x");
        var both = Calls.Both<[Cat, Cat]>(new Cat(), new Cat());
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build(Closings, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [InlineData("Pair", $"public Pair_2(System.Func<T1> __default_T1, System.Func<T2> __default_T2) : this(default({Marker}), __default_T1, __default_T2)")]
    [InlineData("Pair", $"private Pair_2({Marker} marker, System.Func<T1> __default_T1, System.Func<T2> __default_T2)")]
    [InlineData("Pair", "this.First = __default_T1();")]
    [InlineData("Pair", "this.Second = __default_T2();")]
    [InlineData("Pair", "public static Pair_2<T1, T2> __CreateDefault(System.Func<T1> __default_T1, System.Func<T2> __default_T2)")]
    [InlineData("Crate", "this.Kept = __default_T2();")]
    [InlineData("Blocked", $"protected Blocked_2({Marker} marker, System.Func<T1> __default_T1)")]
    [InlineData("Blocked", "public static Blocked_2<T1, T2> __CreateDefault(System.Func<T1> __default_T1)")]
    [InlineData("Holder", "this.Inner = Probe.Pair_2<T1, T2>.__CreateDefault(__default_T1, __default_T2);")]
    [InlineData("Holder", "this.Own = __default_T2();")]
    [InlineData("Values", $"protected Values_2({Marker} marker, System.Func<T1> __default_T1)")]
    [InlineData("Values", "this.Maybe = null;")]
    public void StorageTypedByAPackElement_IsZeroedThroughAWitnessOfThatElement(string typeName, string line)
    {
        Assert.Contains(line, Lines(Emit(typeName, [2])));
    }

    [Theory]
    [InlineData("Flat", "this.First = default(int);")]
    [InlineData("Flat", "this.Second = \"\";")]
    [InlineData("Derived", "public Derived() : base(default(Mist.Emission.CSharp.__DefaultInit), () => default(bool), () => \"\")")]
    public void ATypeOverAClosedVariadicBase_FillsEachElementsZeroFromItsBaseList(string typeName, string line)
    {
        Assert.Contains(line, Lines(Emit(typeName, widths: null)));
    }

    [Theory]
    [InlineData("Probe.Pair_2<int, string> pair = Probe.Pair_2<int, string>.__CreateDefault(() => default(int), () => \"\");")]
    [InlineData("Probe.Holder_2<Probe.Widget, string> keeper = Probe.Holder_2<Probe.Widget, string>.__CreateDefault(() => Probe.Widget.__CreateDefault(), () => \"\");")]
    [InlineData("Probe.Blocked_2<string, int> blocked = Probe.Blocked_2<string, int>.__CreateDefault(() => \"\");")]
    [InlineData("Probe.Values_2<int, bool> values = Probe.Values_2<int, bool>.__CreateDefault(() => default(int));")]
    [InlineData("string zero = Probe.Calls.Zero_2<int, string>(() => default(int), () => \"\");")]
    [InlineData("int defaulted = Probe.Calls.Defaulted_2<int, string>(() => default(int), () => \"\", (1, \"x\"));")]
    public void AUse_PassesTheDefaultOfTheTypeItsClosingPutAtEachElement(string line)
    {
        Assert.Contains(line, Lines(Emit("Probe", widths: null)));
    }

    [Theory]
    [InlineData("public static T2 Zero_2<T1, T2>(System.Func<T1> __default_T1, System.Func<T2> __default_T2)", "return __default_T2();")]
    [InlineData(
        "public static int Defaulted_2<T1, T2>(System.Func<T1> __default_T1, System.Func<T2> __default_T2, (T1, T2) args)",
        "return Zeroed_2<T1, T2>(() => __default_T1(), () => __default_T2(), args);")]
    public void ADefaultConstraintOnAPack_IsWitnessedPerElement(string declaration, string body)
    {
        var lines = Lines(Emit("Calls", widths: null));
        var declared = lines.IndexOf(declaration);

        Assert.True(declared >= 0, declaration);
        Assert.Equal(body, lines[declared + 2]);
    }

    [Fact]
    public void AConstraintOnAMethodsPack_IsWrittenOnEachParameterOfItsSpecialization()
    {
        var lines = Lines(Emit("Calls", widths: null));
        var declared = lines.IndexOf("public static string Both_2<T1, T2>(T1 a, T2 b)");

        Assert.True(declared >= 0);
        Assert.Equal(["where T1 : Probe.INamed", "where T2 : Probe.INamed"], lines.Skip(declared + 1).Take(2));
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