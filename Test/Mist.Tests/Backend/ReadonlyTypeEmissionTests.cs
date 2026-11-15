// Where `readonly` survives emission and where it cannot. Asserted on the emitted TEXT: the model carries
// one flag for every position while C# accepts it in only some, and a readonly field and a writable one
// holding the same value print identically, so neither the model nor AllFeaturesV2 can guard it.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ReadonlyTypeEmissionTests : ProgramModelProbe
{
    public ReadonlyTypeEmissionTests()
        : base("MistReadonlyEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public readonly class ReadonlyClass
        {
            public int kept;

            public string Describe()
            {
                return "ro-class";
            }
        }

        public readonly struct ReadonlyStruct
        {
            public int kept;

            public string Describe()
            {
                return "ro-struct";
            }
        }

        public class PlainClass
        {
            public int kept;
        }

        public struct PlainStruct
        {
            public int kept;
        }

        public class TakesReadonly
        {
            public string Show(readonly int locked)
            {
                return "shown";
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AReadonlyStructKeepsTheKeyword()
    {
        Assert.Contains("readonly struct ReadonlyStruct", EmitDeclaration("ReadonlyStruct"));
    }

    /// <summary>
    /// C# has no <c>readonly class</c>.
    /// </summary>
    [Fact]
    public void AReadonlyClassDropsTheKeyword()
    {
        var emitted = EmitDeclaration("ReadonlyClass");

        Assert.Contains("class ReadonlyClass", emitted);
        Assert.DoesNotContain("readonly class", emitted);
    }

    /// <summary>
    /// With the class keyword gone, the fields are the only place a readonly class's discipline reaches
    /// the output.
    /// </summary>
    [Fact]
    public void BothReadonlyKindsPromoteTheirFields()
    {
        Assert.Contains("public readonly int kept;", EmitDeclaration("ReadonlyClass"));
        Assert.Contains("public readonly int kept;", EmitDeclaration("ReadonlyStruct"));
    }

    [Fact]
    public void NeitherPlainKindPromotesItsFields()
    {
        Assert.DoesNotContain("readonly", EmitDeclaration("PlainClass"));
        Assert.DoesNotContain("readonly int kept", EmitDeclaration("PlainStruct"));
    }

    /// <summary>
    /// C# refuses a readonly method on a class (CS0106), and a readonly TYPE already makes its members
    /// readonly.
    /// </summary>
    [Fact]
    public void NeitherReadonlyKindPutsTheModifierOnItsMethod()
    {
        Assert.DoesNotContain("readonly string Describe", EmitDeclaration("ReadonlyClass"));
        Assert.DoesNotContain("readonly string Describe", EmitDeclaration("ReadonlyStruct"));
    }

    /// <summary>
    /// C# has no readonly parameter. Both halves are asserted because erasure alone is equally consistent
    /// with the model never recording it.
    /// </summary>
    [Fact]
    public void AReadonlyParameterIsModelledAndThenErased()
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.TakesReadonly"]);
        var parameter = Assert.Single(type.MethodsList.Single(m => m.Name == "Show").ParametersList);

        Assert.True(parameter.Modifier.IsReadonly);

        var emitted = EmitDeclaration("TakesReadonly");
        Assert.Contains("int locked", emitted);
        Assert.DoesNotContain("readonly int locked", emitted);
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