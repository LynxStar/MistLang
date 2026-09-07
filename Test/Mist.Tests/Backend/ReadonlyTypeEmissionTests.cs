// Where `readonly` survives emission and where it cannot. Asserted on the emitted TEXT rather than on
// the model, because the model carries one flag for every position and the whole capability is that the
// flag lands differently in each -- C# has no `readonly class`, refuses a writable instance field on a
// readonly struct, and refuses `readonly` on a class method. None of that is observable from program
// output either: a readonly field and a writable one holding the same value print identically, so
// AllFeaturesV2 cannot guard any of it. Order 134 of
// backlog/emitter-v2-capability-progression.yaml rests on this file.

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
    /// C# has no <c>readonly class</c>, so the keyword cannot survive on the declaration. Asserted as
    /// an absence because emitting it would be rejected by Roslyn against a generated file.
    /// </summary>
    [Fact]
    public void AReadonlyClassDropsTheKeyword()
    {
        var emitted = EmitDeclaration("ReadonlyClass");

        Assert.Contains("class ReadonlyClass", emitted);
        Assert.DoesNotContain("readonly class", emitted);
    }

    /// <summary>
    /// The one thing that carries a readonly class's discipline into the output. With the keyword gone
    /// the fields are the only place left for it, so a readonly class whose fields emit writable has
    /// had its readonly-ness erased entirely rather than relocated.
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
    /// A readonly method is a struct-only concept in C# (CS0106 on a class), and a readonly TYPE makes
    /// its members readonly without either declaring it, so neither declaration needs the modifier on
    /// its method and the class one must not have it.
    /// </summary>
    [Fact]
    public void NeitherReadonlyKindPutsTheModifierOnItsMethod()
    {
        Assert.DoesNotContain("readonly string Describe", EmitDeclaration("ReadonlyClass"));
        Assert.DoesNotContain("readonly string Describe", EmitDeclaration("ReadonlyStruct"));
    }

    /// <summary>
    /// The one position where readonly is carried by the model and cannot be carried by the output: C#
    /// has no readonly parameter, so erasure is the only faithful emission. Both halves are asserted
    /// together because erasure alone is equally consistent with the model never having recorded it.
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