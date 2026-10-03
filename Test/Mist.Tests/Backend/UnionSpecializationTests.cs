// Tier 3 (Component): which generic declarations the backend emits once per instantiation because they
// write a union over their own type parameters, at which instantiations, and what each instance spells.
// A C# generic fills its parameters in by position, while Mist sorts a union and merges duplicates after
// substitution, so no declaration written once agrees with every instantiation; see UnionSpecialization.
// Asserted on the registry and on emitted text, because a correct instance and a lucky one print alike.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class UnionSpecializationTests : ProgramModelProbe
{
    public UnionSpecializationTests()
        : base("MistUnionSpecialization_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Cell<V>
        {
            public V Value;

            public Cell(V value) { this.Value = value; }
        }

        public class Pair<T, R>
        {
            public T | R Held;

            public Cell<T | R> Boxed;

            public Pair(T | R start)
            {
                this.Held = start;
                this.Boxed = new Cell<T | R>(start);
            }

            public Pair<R, T> Flip() { return new Pair<R, T>(this.Held); }
        }

        public class Slot<K>
        {
            public K? Item;

            public Slot() { }
        }

        public class ValueSlot<K> where K : struct
        {
            public K? Item;

            public ValueSlot() { }
        }

        public class RefSlot<K> where K : class
        {
            public K? Item;

            public RefSlot() { }
        }

        public class Unused<K>
        {
            public K? Item;

            public Unused() { }
        }

        public class Api
        {
            public static int Pick<T, R>(T | R value) { return 1; }

            public static int UseSlot<T>(Slot<T> slot) { return 2; }

            public static int Maybe<Type[2]:T>(T[0]? first) { return 3; }
        }

        public class Duo<Type[2]:Ts>
        {
            public Ts... Item;

            public Ts[1]? Second;

            public Duo(params Ts values) { Item... = values...; }

            public int Width() { return Ts.Length; }
        }

        public class Either<Type[2]:Ts>
        {
            public Ts[0] | string Held;

            public Either(Ts[0] first) { this.Held = first; }
        }

        public class Nest<Type[2]:Ts>
        {
            public Slot<Ts[1]> Held;

            public Nest() { this.Held = new Slot<Ts[1]>(); }
        }

        public class Forward<Type[2]:Ts>
        {
            public Duo<Ts...> Inner;

            public Forward(params Ts values) { this.Inner = new Duo<Ts...>(values...); }
        }

        public class ValueDuo<Type[2]:Ts> where Ts : struct
        {
            public Ts[1]? Second;

            public ValueDuo() { }
        }

        public class Spread<params Ts>
        {
            public Ts... Item;

            public Spread(params Ts values) { Item... = values...; }
        }

        public class ClosedDuo : Duo<int, string>
        {
            public ClosedDuo() : base(1, "c") { }

            public Duo<int, string> Again() { return new Duo<int, string>(2, "a"); }
        }

        public class Caller<K>
        {
            public int Use(K value) { return Api.Pick<K, string>(value); }
        }

        public class Lifted<K>
        {
            public int Take(K? key) { return 1; }

            public int Take(int? number) { return 2; }
        }

        public class Derived<K> : Lifted<K>
        {
            public int Both(K? key) { return this.Take(key); }
        }

        public class Via<K>
        {
            public int Go(Derived<K> derived) { return derived.Take(3); }
        }

        public abstract ITaker<K>
        {
            int Take(K? key);
        }

        public class Filler<K> : ITaker<K>
        {
            public int Take(K? key) { return 1; }

            public int Take(int? number) { return 2; }
        }

        public class Chooser
        {
            public int Go()
            {
                var lifted = new Lifted<int>();
                func<int?, int> take = lifted.Take;
                return take(3);
            }
        }

        public class Valued<K>
        {
            public int Go(Lifted<K> lifted, K? key)
            {
                func<K?, int> take = lifted.Take;
                return take(key);
            }
        }
        """;

    protected override string Preamble { get; } = """
        var first = new Pair<int, string>(1);
        var merged = new Pair<int, int>(2);
        var slot = new Slot<int>();
        var named = new Slot<string>();
        var values = new ValueSlot<int>();
        var refs = new RefSlot<string>();
        var picked = Api.Pick<bool, int>(true);
        var used = Api.UseSlot(slot);
        var caller = new Caller<long>();
        var lifted = new Lifted<int>();
        var liftedWords = new Lifted<string>();
        var derived = new Derived<int>();
        var via = new Via<int>();
        var filler = new Filler<int>();
        var valued = new Valued<int>();
        var duo = new Duo<int, string>(1, "a");
        var again = new Duo<int, string>(2, "b");
        var flipped = new Duo<string, int>("c", 3);
        var either = new Either<int, bool>(4);
        var nest = new Nest<string, int>();
        var forward = new Forward<bool, char>(true, 'q');
        var valueDuo = new ValueDuo<int, long>();
        var spread = new Spread<int, int>(1, 2);
        var maybe = Api.Maybe<[int, string]>(5);
        """;

    [Theory]
    [InlineData("Pair", true)]
    [InlineData("Slot", true)]
    [InlineData("Unused", true)]
    [InlineData("Caller", true)]
    [InlineData("Cell", false)]
    [InlineData("ValueSlot", false)]
    [InlineData("RefSlot", false)]
    public void ATypeIsSpecializedExactlyWhenItWritesAUnionNoCSharpGenericCanSpell(string type, bool expected)
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        Assert.Equal(expected, UnionSpecialization.Specializes(Definition(model, type)));
    }

    [Fact]
    public void EveryInstantiationTheProgramReachesIsAnInstance_IncludingThroughSubstitution()
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        Assert.Equal(
            ["Pair<int, int>", "Pair<int, string>", "Pair<string, int>"],
            UnionSpecialization.InstancesOf(Definition(model, "Pair")).Select(TypeDisplay.Mist).Order());
    }

    [Fact]
    public void AnUnusedDeclarationHasNoInstanceToEmit()
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        Assert.Empty(UnionSpecialization.InstancesOf(Definition(model, "Unused")));
    }

    [Theory]
    [InlineData("Pair<int, string>", "public Union_Int32_String Held;")]
    [InlineData("Pair<string, int>", "public Union_Int32_String Held;")]
    [InlineData("Pair<int, int>", "public int Held;")]
    [InlineData("Pair<int, int>", "public Probe.Cell<int> Boxed;")]
    [InlineData("Slot<int>", "public int? Item;")]
    [InlineData("Slot<string>", "public string Item;")]
    public void AnInstanceSpellsItsUnionsExactlyAsClosedCodeDoes(string instance, string expected)
    {
        Assert.Contains(expected, EmitInstance(instance), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Lifted<int>", "Take__o0(int? key)")]
    [InlineData("Lifted<int>", "Take__o1(int? number)")]
    [InlineData("Lifted<string>", "Take(string key)")]
    [InlineData("Lifted<string>", "Take(int? number)")]
    public void OverloadsAnInstanceMakesIdenticalAreNamedApart_AndOnlyThere(string instance, string expected)
    {
        Assert.Contains(expected, EmitInstance(instance), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Derived<int>", "this.Take__o0(key)")]
    [InlineData("Via<int>", "derived.Take__o1(3)")]
    public void ACallThroughADerivedReceiverIsNamedAtTheBaseConstructionItReaches(string instance, string expected)
    {
        Assert.Contains(expected, EmitInstance(instance), StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodGroupOfACoincidingPairIsNamedForTheOverloadMistChose_WhereverItWasChosen()
    {
        Assert.Contains("lifted.Take__o1;", EmitType(Model(), "Chooser"), StringComparison.Ordinal);
        Assert.Contains("lifted.Take__o0;", EmitInstance("Valued<int>"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Filler<int>", "public int Take(int? key)")]
    [InlineData("Filler<int>", "public int Take__o1(int? number)")]
    public void TheOverloadAnsweringAnAbstractSlotKeepsItsName_AndOnlyItsSiblingIsRenamed(string instance, string expected)
    {
        Assert.Contains(expected, EmitInstance(instance), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IntKeep", "private static extern ref int? __Probe_KeepInt32_item(Probe.Keep__Standard_Int32 target);")]
    [InlineData("IntHeld", "private static extern ref int? __In_Probe_HeldInt32_item(Probe.Held__Standard_Int32 __target);")]
    [InlineData("IntHeld", "private static extern Probe.Held__Standard_Int32 __Out_Probe_HeldInt32_new(int? start, int tag);")]
    public void ANewtypeReachesASpecializedSourceAsTheClosedTypeItIs_NotThroughAHolder(string clone, string expected)
    {
        var model = StandardModelProbe.Build(
            "var keep = (IntKeep)new Keep<int>(1); var held = (IntHeld)new Held<int>(1); Held<int> back = (Held<int>)held;",
            out var diagnostics,
            extraTypes: """
                public readonly class Keep<K>
                {
                    private K? item;

                    public Keep(K? start) { this.item = start; }
                }

                public type IntKeep = new Keep<int>;

                public class Held<K> : IClonable<Held<K>>
                {
                    private K? item;

                    public Held(K? start) { this.item = start; }

                    private Held(K? start, int tag) { this.item = start; }

                    public owned Held<K> Clone() { return new Held<K>(this.item, 1); }
                }

                public type IntHeld = new Held<int>;
                """);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Contains(expected, EmitType(model, clone), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Kept", "private static extern ref string __Probe_KeptString_item(Probe.Kept__Standard_String target);")]
    [InlineData("Kept", "private static extern ref string __Probe_KeepString_item(Probe.Keep__Standard_String target);")]
    [InlineData("Helded", "private static extern ref string __In_Probe_HeldString_item(Probe.Held__Standard_String __target);")]
    [InlineData("Helded", "private static extern Probe.Held__Standard_String __Out_Probe_HeldString_new(string start, int tag);")]
    [InlineData("Helded", "private static extern int __In_Probe_HeldString_get_Tag(Probe.Held__Standard_String __target);")]
    [InlineData("Helded", "private static extern void __Out_Probe_HeldString_set_Tag(Probe.Held__Standard_String __target, int value);")]
    [InlineData("Helded", "UnsafeAccessorKind.Method, Name = \"Take__o0\")]")]
    public void AGenericNewtypeEmittedPerInstance_ReachesItsSourceAtThatInstance(string clone, string expected)
    {
        var model = StandardModelProbe.Build(
            "var kept = (Kept<string>)new Keep<string>(\"k\"); var helded = (Helded<string>)new Held<string>(\"h\"); var back = (Held<string>)helded;",
            out var diagnostics,
            extraTypes: """
                public readonly class Keep<K>
                {
                    private K? item;

                    public Keep(K? start) { this.item = start; }
                }

                public type Kept<X> = new Keep<X>;

                public class Held<K> : IClonable<Held<K>>
                {
                    private K? item;
                    private int spare;

                    private int Tag
                    {
                        get { return spare; }
                        set { spare = value; }
                    }

                    public Held(K? start) { this.item = start; }

                    private Held(K? start, int tag) { this.item = start; }

                    private int Take(K? value) { return 1; }

                    private int Take(string? value) { return 2; }

                    public owned Held<K> Clone()
                    {
                        var copy = new Held<K>(this.item, 1);
                        copy.Tag = Tag + Take(this.item);
                        return copy;
                    }
                }

                public type Helded<Y> = new Held<Y>;
                """);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var emitDiagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, emitDiagnostics);
        var instance = Assert.Single(UnionSpecialization.InstancesOf(Definition(model, clone)));
        var writer = new CSharpWriter();

        using(TypeEmitter.Specializing(instance))
        {
            TypeEmitter.EmitTypeBody(
                (MemberType)instance.Definition, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);
        }

        var emitted = writer.ToString();

        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("static class", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceIsDeclaredUnderItsOwnNameWithNoTypeParameters()
    {
        Assert.Contains(
            "public class Pair__Standard_String__Standard_Int32",
            EmitInstance("Pair<string, int>"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pick", "bool, int")]
    [InlineData("UseSlot", "int")]
    [InlineData("Pick", "long, string")]
    public void AGenericMethodIsSpecializedPerCall_IncludingOneReachedThroughAGenericCaller(
        string method, string arguments)
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        var declared = ((ClassType)model.TypesByFQN["Probe.Api"]).MethodsList.Single(m => m.Name == method);

        Assert.Contains(
            arguments,
            UnionSpecialization.InstancesOf(declared).Select(instance => string.Join(", ", instance.Select(TypeDisplay.Mist))));
    }

    [Fact]
    public void AnInstantiationNeedingALargerOneOfItself_IsReportedRatherThanEmittedForever()
    {
        var model = BuildModel(
            statement: "var grown = new Grow<int>(1);",
            extraTypes: """
                public class Grow<T>
                {
                    public T | string Held;

                    public Grow(T | string held) { this.Held = held; }

                    public Grow<Cell<T>> Next() { return new Grow<Cell<T>>("n"); }
                }
                """,
            extraPreamble: "",
            out _);

        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        _ = Assert.Single(diagnostics.ToImmutableArray(), d => d.Id.Value == "MIST_BE_UNBOUNDED_SPECIALIZATION");
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Duo", true)]
    [InlineData("Either", true)]
    [InlineData("Nest", true)]
    [InlineData("Forward", true)]
    [InlineData("ValueDuo", false)]
    [InlineData("Spread", false)]
    public void APackDeclarationIsSpecializedExactlyWhenItWritesAUnionOverAnElement_OrNamesOneThatDoes(string type, bool expected)
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        Assert.Equal(expected, UnionSpecialization.Specializes(Definition(model, type)));
    }

    [Fact]
    [Trait("Tier", "3")]
    public void EachClosedPackIsOneInstance_RepeatsMergedAndOnesReachedThroughSubstitutionIncluded()
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        Assert.Equal(
            ["Duo<[Boolean, Char]>", "Duo<[Int32, String]>", "Duo<[String, Int32]>"],
            UnionSpecialization.InstancesOf(Definition(model, "Duo")).Select(TypeDisplay.Mist).Order());
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Duo<[Int32, String]>", "public class Duo__P2__Standard_Int32__Standard_String")]
    [InlineData("Duo<[Int32, String]>", "public string Second;")]
    [InlineData("Duo<[String, Int32]>", "public int? Second;")]
    [InlineData("Duo<[Int32, String]>", "public (int, string) Item;")]
    [InlineData("Duo<[Int32, String]>", "public Duo__P2__Standard_Int32__Standard_String((int, string) values)")]
    [InlineData("Duo<[Int32, String]>", "public int Item1 { get => Item.Item1; set => Item.Item1 = value; }")]
    [InlineData("Duo<[String, Int32]>", "public int Item2 { get => Item.Item2; set => Item.Item2 = value; }")]
    [InlineData("Duo<[Int32, String]>", "return 2;")]
    [InlineData("Either<[Int32, Boolean]>", "public Union_Int32_String Held;")]
    [InlineData("Nest<[String, Int32]>", "public Probe.Slot__Standard_Int32 Held;")]
    [InlineData("Forward<[Boolean, Char]>", "public Probe.Duo__P2__Standard_Boolean__Standard_Char Inner;")]
    public void AnInstanceOfAPackDeclaration_SpellsEachElementAsClosedCodeDoes(string instance, string expected)
    {
        Assert.Contains(expected, Lines(EmitInstance(instance)));
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("public class ClosedDuo : Probe.Duo__P2__Standard_Int32__Standard_String")]
    [InlineData("public ClosedDuo() : base((1, \"c\"))")]
    [InlineData("public Probe.Duo__P2__Standard_Int32__Standard_String Again()")]
    public void AClosedReferenceToASpecializedPackDeclaration_NamesTheInstanceRatherThanAnArity(string expected)
    {
        Assert.Contains(expected, Lines(EmitType(Model(), "ClosedDuo")));
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AVariadicMethodWritingAUnionOverItsElement_IsSpecializedPerCall()
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        var declared = ((ClassType)model.TypesByFQN["Probe.Api"]).MethodsList.Single(m => m.Name == "Maybe");

        Assert.Equal("[Int32, String]", TypeDisplay.Mist(Assert.Single(Assert.Single(UnionSpecialization.InstancesOf(declared)))));
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ASpecializedPackDeclarationIsWrittenWithItsFile_AndOnlyThatOne()
    {
        var model = Model();
        using var specialization = UnionSpecialization.Enter(model, new DiagnosticBag());

        var written = model.SelfAssembly!.CompilationUnits.SelectMany(CSharpEmitterV2.WrittenDeclarations)
            .Select(type => MistType.StripArity(type.Name)).ToList();

        Assert.Contains("Duo", written);
        Assert.DoesNotContain("Spread", written);
        Assert.DoesNotContain("ValueDuo", written);
    }

    private ProgramModelClass Model()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return model;
    }

    private static MemberType Definition(ProgramModelClass model, string name)
    {
        return (MemberType)model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == $"Probe.{name}").Value;
    }

    private string EmitInstance(string display)
    {
        var model = Model();
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        var name = display[..display.IndexOf('<')];
        var instance = UnionSpecialization.InstancesOf(Definition(model, name)).Single(i => TypeDisplay.Mist(i) == display);
        var writer = new CSharpWriter();

        using(TypeEmitter.Specializing(instance))
        {
            TypeEmitter.EmitTypeBody(
                (MemberType)instance.Definition, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }

    private static string EmitType(ProgramModelClass model, string name)
    {
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        var writer = new CSharpWriter();
        TypeEmitter.EmitTypeBody(
            Definition(model, name), ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}