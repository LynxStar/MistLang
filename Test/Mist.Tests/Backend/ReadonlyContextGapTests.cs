// Tier 3 (Component): purity Requirement 4.1 read against the readonly analyzer -- a readonly member
// writes nothing External however the write is spelled: through a ref parameter or a ref local, into an
// element of an array parameter, by an object initializer's setter, a property pattern's getter, an
// extension getter, a conversion operator or an implicit clone -- while what it OWNS stays writable, a
// readonly field is assignable only through this in a constructor of its own type, and the emitter's
// oracle-less inference agrees with the analysis on a setter called on a fresh local. The ids are the
// readonly analyzer's own; a row asserts the family where no descriptor is named yet.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ReadonlyContextGapTests : ProgramModelProbe
{
    private const string ReadonlyFamily = "MIST_PA_READONLY_";
    private const string FieldAssignedFamily = "MIST_PA_FIELD_ASSIGNED";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";
    private const string SetsProperty = "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY";
    private const string WritesThroughIndex = "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX";
    private const string ReadonlyFieldAssigned = "MIST_PA_READONLY_FIELD_ASSIGNED";

    public ReadonlyContextGapTests()
        : base("MistReadonlyContextGap_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
        }

        public class Bag
        {
            public int Count;

            public pure Bag() { }
        }

        public class Cell
        {
            public int Level { get; set; }
        }

        public class Loud
        {
            public int lvl;

            public int Level
            {
                get { return lvl; }
                set { lvl = value; Globals.Counter = 1; }
            }
        }

        public class Noisy
        {
            public int Level
            {
                get { Globals.Counter = Globals.Counter + 1; return Globals.Counter; }
            }
        }

        public class Quiet
        {
            public int N;

            public pure Quiet() { }

            public owned Quiet Clone() { var c = new Quiet(); c.N = N; return c; }
        }

        public class Chatty
        {
            public static int Made;
            public int N;

            public pure Chatty() { }

            public owned Chatty Clone() { Made = Made + 1; var c = new Chatty(); c.N = N; return c; }
        }

        public class Wrapped
        {
            public int V;

            public static explicit operator Wrapped(Bag b) { Globals.Counter = 1; var w = new Wrapped(); w.V = b.Count; return w; }

            public static implicit operator int(Wrapped w) { Globals.Counter = 10; return w.V; }
        }

        public class Calm
        {
            public int V;

            public static explicit operator Calm(Bag b) { var c = new Calm(); c.V = b.Count; return c; }

            public static implicit operator int(Calm c) { return c.V; }
        }

        public class Key
        {
            public readonly int K;

            public pure Key(int k) { K = k; }
        }

        extension(Bag self)
        {
            public int Ext
            {
                get { Globals.Counter = Globals.Counter + 1; return self.Count; }
            }

            public int Plain
            {
                get { return self.Count; }
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public static readonly int Set(ref int x) { x = 5; return x; }")]
    [InlineData("public readonly int Twist() { ref int r = n; r = 7; return n; }")]
    public void AReadonlyMember_MayNotWriteThroughARef(string member)
    {
        var finding = Assert.Single(AnalyseMember(member));

        Assert.StartsWith(ReadonlyFamily, finding.Id.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayNotWriteAnElementOfAnArrayParameter()
    {
        var finding = Assert.Single(AnalyseMember("public static readonly int Fill(int[] a) { a[0] = 5; return 1; }"));

        Assert.Equal(WritesThroughIndex, finding.Id.Value);
    }

    [Fact]
    public void AReadonlyMember_MayStillFillAnArrayItAllocated()
    {
        Assert.Empty(AnalyseMember("public readonly int Build() { int[] xs = [0, 0]; xs[0] = 1; return xs[0]; }"));
    }

    [Fact]
    public void AReadonlyMember_MayNotRunASideEffectingSetterThroughAnObjectInitializer()
    {
        var finding = Assert.Single(AnalyseMember("public readonly int Build() { var l = new Loud { Level = 3 }; return l.Level; }"));

        Assert.Contains(finding.Id.Value, new[] { SetsProperty, CallsMutable });
        Assert.Contains("Level", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public readonly int Build() { var c = new Cell { Level = 3 }; return c.Level; }")]
    [InlineData("public readonly int Build() { var c = new Cell(); c.Level = 2; return c.Level; }")]
    public void AReadonlyMember_MayStillCallAnAutoSetterOnWhatItOwns(string member)
    {
        Assert.Empty(AnalyseMember(member));
    }

    [Fact]
    public void AReadonlyMember_MayNotReadASideEffectingGetterInAPropertyPattern()
    {
        var finding = Assert.Single(AnalyseMember("public readonly int M(Noisy c) { if(c is { Level: 1 }) { return 1; } return 0; }"));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Contains("Level", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayMatchAnAutoPropertyInAPattern()
    {
        Assert.Empty(AnalyseMember("public readonly int M(Cell c) { if(c is { Level: 1 }) { return 1; } return 0; }"));
    }

    [Fact]
    public void AReadonlyMember_MayNotReadASideEffectingExtensionGetter()
    {
        var finding = Assert.Single(AnalyseMember("public readonly int M(Bag b) { return b.Ext; }"));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Contains("Ext", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayReadAnExtensionGetterThatOnlyReads()
    {
        Assert.Empty(AnalyseMember("public readonly int M(Bag b) { return b.Plain; }"));
    }

    [Theory]
    [InlineData("public readonly int M(Bag b) { var w = (Wrapped)b; return 1; }")]
    [InlineData("public readonly int M(Wrapped w) { int k = w; return k; }")]
    public void AReadonlyMember_MayNotCallASideEffectingConversion(string member)
    {
        var finding = Assert.Single(AnalyseMember(member));

        Assert.Equal(CallsMutable, finding.Id.Value);
    }

    [Theory]
    [InlineData("public readonly int M(Bag b) { var c = (Calm)b; return c.V; }")]
    [InlineData("public readonly int M(Calm c) { int k = c; return k; }")]
    public void AReadonlyMember_MayCallAConversionThatWritesNothing(string member)
    {
        Assert.Empty(AnalyseMember(member));
    }

    [Fact]
    public void AReadonlyMember_MayNotCloneThroughASideEffectingClone()
    {
        var finding = Assert.Single(AnalyseMember("public readonly int Take(Chatty b) { owned var c = b; return c.N; }"));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Contains("Clone", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMember_MayCloneThroughACloneThatReadsOnlyItsSource()
    {
        Assert.Empty(AnalyseMember("public readonly int Take(Quiet b) { owned var c = b; return c.N; }"));
    }

    [Theory]
    [InlineData("public class Twin { public readonly int K; public pure Twin(int k) { K = k; } public Twin(Twin other, int v) { K = 0; other.K = v; } }")]
    [InlineData("public class Hacker { public Hacker(Key k) { k.K = 99; } }")]
    public void AReadonlyField_IsAssignableOnlyThroughThisInAConstructorOfItsType(string declaration)
    {
        var finding = Assert.Single(Analyse("", declaration));

        Assert.Equal(ReadonlyFieldAssigned, finding.Id.Value);
        Assert.Contains("'K'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyField_MayNotBePassedByRef()
    {
        var finding = Assert.Single(Analyse("", "public class Twister { public static void Set(ref int slot) { slot = 8; } public void Twist(Key k) { Set(ref k.K); } }"));

        Assert.StartsWith("MIST_PA_READONLY_FIELD", finding.Id.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Twin { public readonly int K; public Twin(int k) { K = k; } }")]
    [InlineData("public class Twin { public readonly int K; public Twin(int k) { this.K = k; } }")]
    public void AReadonlyField_IsAssignableThroughThisInItsOwnConstructor(string declaration)
    {
        Assert.Empty(Analyse("", declaration));
    }

    [Fact]
    public void AStructMethodSettingAnAutoPropertyOnAFreshLocal_KeepsCSharpReadonly()
    {
        var model = BuildModel(
            "",
            "public struct Vec { public int X; public int PeekProperty() { var c = new Cell(); c.Level = 3; return X + c.Level; } public int PeekField() { var c = new Cell(); return X + c.Level; } }",
            "",
            out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Vec"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);
        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var emitted = writer.ToString();

        Assert.Contains("public readonly int PeekField()", emitted, StringComparison.Ordinal);
        Assert.Contains("public readonly int PeekProperty()", emitted, StringComparison.Ordinal);
    }

    private List<Diagnostic> AnalyseMember(string member)
    {
        return Analyse("", "public class Host { public int n; " + member + " }");
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return
        [
            .. findings.ToImmutableArray().Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && (diagnostic.Id.Value.StartsWith(ReadonlyFamily, StringComparison.Ordinal)
                    || diagnostic.Id.Value.StartsWith(FieldAssignedFamily, StringComparison.Ordinal)
                    || diagnostic.Id.Value == StoresUnowned)),
        ];
    }
}