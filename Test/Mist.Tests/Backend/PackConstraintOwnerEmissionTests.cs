using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

// Tier 3: a whole-pack clause naming its pack, emitted through each kind of owner a pack has -- a type
// specialized per width and a method specialized per call -- beside a clause naming an ordinary sibling,
// which already emitted and is the control. Asserted on the emitted declaration, since a clause naming
// the pack fails only in Roslyn.
[Trait("Tier", "3")]
public sealed class PackConstraintOwnerEmissionTests : ProgramModelProbe
{
    public PackConstraintOwnerEmissionTests()
        : base("MistPackConstraintOwner_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IRank<T> { int Rank(); }
        public abstract IKeyed<K> { K Key(); }

        public class Score : IRank<Score>, IKeyed<int>
        {
            public int Rank() { return 1; }
            public int Key() { return 2; }
        }

        public class Row<params Ts> where Ts : IRank<Ts> { }

        public class Keys<K, params Vs> where Vs : IKeyed<K> { }

        public class Util
        {
            public static int Count<params Ts>(params Ts args) where Ts : IRank<Ts> { return 0; }
        }
        """;

    protected override string Preamble { get; } = """
        var row = new Row<Score, Score>();
        var one = new Row<Score>();
        var keys = new Keys<int, Score>();
        var counted = Util.Count(new Score(), new Score());
        """;

    [Theory]
    [InlineData("Row", 2, "public class Row_2<T1, T2>", "where T1 : Probe.IRank<T1>", "where T2 : Probe.IRank<T2>")]
    [InlineData("Row", 1, "public class Row_1<T1>", "where T1 : Probe.IRank<T1>", null)]
    [InlineData("Keys", 1, "public class Keys_1<K, T1>", "where T1 : Probe.IKeyed<K>", null)]
    [InlineData("Util", null, "public static int Count_2<T1, T2>((T1, T2) args)", "where T1 : Probe.IRank<T1>", "where T2 : Probe.IRank<T2>")]
    public void EachElementOfAnOwnersPack_IsConstrainedOverItself(
        string owner, int? width, string declaration, string first, string? second)
    {
        var lines = Lines(Emit(owner, width));
        var declared = lines.IndexOf(declaration);

        Assert.True(declared >= 0, declaration);
        Assert.Equal(first, lines[declared + 1]);
        Assert.Equal(second ?? "{", lines[declared + 2]);
    }

    private string Emit(string owner, int? width)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var declared = Definition(model, owner);

        using(width is { } fixedWidth ? Monomorph.For(declared.TypeParametersList, [fixedWidth])?.Enter() : null)
        {
            TypeEmitter.EmitTypeBody(
                declared, ConstraintClosureMap.Build(model, diagnostics), MonomorphSet.Discover(model).Methods, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }

    private static MemberType Definition(ProgramModelClass model, string name)
    {
        return (MemberType)model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == $"Probe.{name}").Value;
    }

    private static List<string> Lines(string emitted)
    {
        return [.. emitted.Split('\n').Select(line => line.Trim())];
    }
}