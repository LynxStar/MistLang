// Tier 3 (Component): what a user-declared operator emits. Asserted on the emitted TEXT because the
// sample cannot see most of it -- a misspelled symbol fails Roslyn and so the sample, but a type
// whose equality warnings go unsuppressed still runs and prints the same, and the in-process compile
// reports errors only.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class OperatorEmissionTests : ProgramModelProbe
{
    public OperatorEmissionTests()
        : base("MistOperatorEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Vec2
        {
            public int X;
            public int Y;

            public Vec2(int x, int y)
            {
                X = x;
                Y = y;
            }

            public string Describe()
            {
                return "v";
            }

            public static Vec2 operator +(Vec2 a, Vec2 b)
            {
                return new Vec2(a.X + b.X, a.Y + b.Y);
            }

            public static Vec2 operator -(Vec2 v)
            {
                return new Vec2(-v.X, -v.Y);
            }

            public static bool operator ==(Vec2 a, Vec2 b)
            {
                return a.X == b.X && a.Y == b.Y;
            }

            public static bool operator !=(Vec2 a, Vec2 b)
            {
                return a.X != b.X || a.Y != b.Y;
            }

            public static bool operator !(Vec2 v)
            {
                return v.X == 0 && v.Y == 0;
            }

            public static string SumOf(Vec2 a, Vec2 b)
            {
                return (a + b).Describe();
            }

            public static string Negated(Vec2 v)
            {
                return (-v).Describe();
            }
        }

        public class Meters
        {
            public int value = 0;

            public static Meters operator +(Meters a, Meters b)
            {
                return new Meters();
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void EachOperatorIsDeclaredPublicStaticBySymbol()
    {
        var emitted = EmitDeclaration("Vec2");

        Assert.Contains("public static Probe.Vec2 operator +(Probe.Vec2 a, Probe.Vec2 b)", emitted);
        Assert.Contains("public static Probe.Vec2 operator -(Probe.Vec2 v)", emitted);
        Assert.Contains("public static bool operator ==(Probe.Vec2 a, Probe.Vec2 b)", emitted);
        Assert.Contains("public static bool operator !=(Probe.Vec2 a, Probe.Vec2 b)", emitted);
        Assert.Contains("public static bool operator !(Probe.Vec2 v)", emitted);
        Assert.DoesNotContain("op_", emitted);
    }

    [Fact]
    public void AnOperatorResultKeepsItsParenthesesAsAReceiver()
    {
        var emitted = EmitDeclaration("Vec2");

        Assert.Contains("(a + b).Describe()", emitted);
        Assert.Contains("(-v).Describe()", emitted);
    }

    [Fact]
    public void OnlyATypeDeclaringEqualitySuppressesTheObjectEqualsWarnings()
    {
        var withEquality = EmitDeclaration("Vec2");

        Assert.StartsWith("#pragma warning disable CS0660, CS0661", withEquality);
        Assert.EndsWith("#pragma warning restore CS0660, CS0661", withEquality.TrimEnd());
        Assert.DoesNotContain("#pragma", EmitDeclaration("Meters"));
    }

    private string EmitDeclaration(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}