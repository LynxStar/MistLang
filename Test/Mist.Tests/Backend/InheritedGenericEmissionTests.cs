// Tier 3 (Component): what a struct absorbing a GENERIC base emits for the members it inherits.
// Asserted on the emitted text because the sample cannot see the shape that matters: a base's T
// spelled inside a struct where no T is in scope fails Roslyn, and one spelled as the derived
// struct's own parameter or argument compiles -- the difference is the whole capability. See
// ProgramModelProbe for the harness.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed partial class InheritedGenericEmissionTests : ProgramModelProbe
{
    public InheritedGenericEmissionTests()
        : base("MistInheritedGenericEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Container<T>
        {
            public T Value;

            public Container(T value)
            {
                Value = value;
            }

            public T Get()
            {
                return Value;
            }

            public Container<T> With(T replacement)
            {
                return new Container<T>(replacement);
            }
        }

        public struct IntBox : Container<int>
        {
            public int Extra;

            public IntBox(int value, int extra) : base(value)
            {
                Extra = extra;
            }
        }

        public struct Renamed<U> : Container<U>
        {
            public int Tag;

            public Renamed(U value, int tag) : base(value)
            {
                Tag = tag;
            }
        }

        public struct Deep : Renamed<string>
        {
            public Deep(string value) : base(value, 0)
            {
            }
        }

        public struct Held<T>
        {
            public T Value { get; }

            public Held(T value)
            {
                this.Value = value;
            }
        }

        public struct IntHeld : Held<int>
        {
            public int Extra;

            public IntHeld(int value) : base(value)
            {
                Extra = value;
            }
        }

        public struct RenamedHeld<U> : Held<U>
        {
            public RenamedHeld(U value) : base(value)
            {
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [GeneratedRegex(@"\bT\b")]
    private static partial Regex BaseParameter();

    private string EmitStruct(string fullyQualifiedName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var type = Assert.IsAssignableFrom<DeclaredType>(model.TypesByFQN[fullyQualifiedName]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics, ref anyEmitted);

        return writer.ToString();
    }

    [Fact]
    public void AClosedBaseSpellsItsArgumentInEveryInheritedMember()
    {
        var text = EmitStruct("Probe.IntBox");

        Assert.Contains("int Get()", text);
        Assert.Contains("Container<int> With(int replacement)", text);
        Assert.Contains("__Container_ctor(int value)", text);
        Assert.Contains("new Probe.Container<int>(replacement)", text);
        Assert.DoesNotMatch(BaseParameter(), text);
    }

    [Fact]
    public void ARenamedBaseSpellsTheDerivedParameter()
    {
        var text = EmitStruct("Probe.Renamed<>");

        Assert.Contains("U Get()", text);
        Assert.Contains("Container<U> With(U replacement)", text);
        Assert.Contains("__Container_ctor(U value)", text);
        Assert.DoesNotMatch(BaseParameter(), text);
    }

    [Fact]
    public void ATwoLevelChainComposesBothClauses()
    {
        var text = EmitStruct("Probe.Deep");

        Assert.Contains("string Get()", text);
        Assert.Contains("__Container_ctor(string value)", text);
        Assert.Contains("__Renamed_ctor(string value, int tag)", text);
        Assert.DoesNotMatch(BaseParameter(), text);
        Assert.DoesNotMatch(new Regex(@"\bU\b"), text);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Probe.IntHeld", "private IntHeld(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)")]
    [InlineData("Probe.RenamedHeld<>", "private RenamedHeld(U p0, Mist.Emission.CSharp.__StructInit marker)")]
    public void AnInheritedAutoPropertyIsCarriedByTheAncestorFactoryAsTheBaseClauseClosesIt(string type, string factory)
    {
        var text = EmitStruct(type);

        Assert.Contains(factory, text.Split('\n').Select(line => line.Trim()));
        Assert.DoesNotMatch(BaseParameter(), text);
    }
}