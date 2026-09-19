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
        }

        public class Caller<K>
        {
            public int Use(K value) { return Api.Pick<K, string>(value); }
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
}