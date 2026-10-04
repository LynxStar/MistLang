// Tier 3 (Component): where an inferred readonly lands in the emitted text. Only a plain struct's
// instance members can carry C#'s `readonly`, and there Roslyn checks it and it spares a defensive
// copy -- none of which program output shows, so AllFeaturesV2 cannot guard it.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ReadonlyInferenceEmissionTests : ProgramModelProbe
{
    public ReadonlyInferenceEmissionTests()
        : base("MistReadonlyInference_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Counter
        {
            public int count;

            public Counter(int start)
            {
                count = start;
            }

            public int Current
            {
                get
                {
                    return count;
                }
            }

            public int Tracked
            {
                get
                {
                    return count;
                }
                set
                {
                    count = value;
                }
            }

            public int Twice(int x)
            {
                return x * 2;
            }

            public int Doubled()
            {
                return Twice(count);
            }

            public bool Even(int n)
            {
                if(n == 0)
                {
                    return true;
                }

                return Odd(n - 1);
            }

            public bool Odd(int n)
            {
                if(n == 0)
                {
                    return false;
                }

                return Even(n - 1);
            }

            public void Bump()
            {
                count = count + 1;
            }

            public void BumpTwice()
            {
                Bump();
                Bump();
            }

            public static int Zero()
            {
                return 0;
            }
        }

        public readonly struct Frozen
        {
            public int v;

            public int Get()
            {
                return v;
            }
        }

        public class Plain
        {
            public int v;

            public int Get()
            {
                return v;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AGetterOverAFieldIsReadonlyWhereverCSharpSpellsIt()
    {
        var emitted = EmitDeclaration("Counter");

        Assert.Contains("public readonly int Current", emitted);
        Assert.Contains("readonly get", emitted);
        Assert.DoesNotContain("readonly set", emitted);
    }

    [Fact]
    public void AMethodCallingAnInferredReadonlyMethodIsReadonly()
    {
        var emitted = EmitDeclaration("Counter");

        Assert.Contains("public readonly int Twice(int x)", emitted);
        Assert.Contains("public readonly int Doubled()", emitted);
    }

    [Fact]
    public void TwoMethodsCallingEachOtherSettleReadonly()
    {
        var emitted = EmitDeclaration("Counter");

        Assert.Contains("public readonly bool Even(int n)", emitted);
        Assert.Contains("public readonly bool Odd(int n)", emitted);
    }

    [Fact]
    public void AMethodThatWritesAFieldOrCallsOneThatDoesEmitsNone()
    {
        var emitted = EmitDeclaration("Counter");

        Assert.Contains("public void Bump()", emitted);
        Assert.Contains("public void BumpTwice()", emitted);
    }

    [Fact]
    public void AStaticMethodNeverCarriesIt()
    {
        Assert.Contains("public static int Zero()", EmitDeclaration("Counter"));
    }

    [Fact]
    public void NeitherAReadonlyStructNorAClassPutsItOnAMember()
    {
        Assert.DoesNotContain("readonly int Get", EmitDeclaration("Frozen"));
        Assert.DoesNotContain("readonly", EmitDeclaration("Plain"));
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