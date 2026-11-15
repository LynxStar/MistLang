// Where `ref` on a struct survives emission. Asserted on the emitted TEXT because a ref struct and a
// plain one holding the same values compile, run and print identically, so AllFeaturesV2 cannot guard it.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class RefStructEmissionTests : ProgramModelProbe
{
    public RefStructEmissionTests()
        : base("MistRefStructEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        public ref struct RefPair
        {
            public int first;
            public int second;
        }

        public readonly ref struct ReadonlyRefPair
        {
            public int first;
        }

        public struct PlainStruct
        {
            public int kept;
        }

        public class PlainClass
        {
            public int kept;
        }

        public ref class RefClass
        {
            public int kept;
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void ARefStructKeepsItsModifier()
    {
        Assert.Contains("public ref struct RefPair", EmitDeclaration("RefPair"), StringComparison.Ordinal);
    }

    /// <summary>
    /// C# fixes this order and rejects the other, where Mist's own modifier list is free.
    /// </summary>
    [Fact]
    public void AReadonlyRefStructSpellsBothInCSharpsOrder()
    {
        Assert.Contains(
            "public readonly ref struct ReadonlyRefPair",
            EmitDeclaration("ReadonlyRefPair"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void APlainStructGainsNothing()
    {
        Assert.Contains("public struct PlainStruct", EmitDeclaration("PlainStruct"), StringComparison.Ordinal);
    }

    /// <summary>
    /// C# has no ref class, yet Mist parses one and emission reports nothing (refusing it is
    /// analysis-phase work), so the flag reaches a class here and dropping the struct guard spells a
    /// type that will not compile.
    /// </summary>
    [Fact]
    public void AClassNeverGainsIt()
    {
        Assert.Contains("public class RefClass", EmitDeclaration("RefClass"), StringComparison.Ordinal);
        Assert.DoesNotContain("ref class", EmitDeclaration("RefClass"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every struct carries the layout attribute, emitted before the declaration, so it must stay legal
    /// on a ByRefLike one or the whole type is refused.
    /// </summary>
    [Fact]
    public void TheLayoutAttributeStillApplies()
    {
        var emitted = EmitDeclaration("RefPair");

        Assert.Contains("StructLayout", emitted, StringComparison.Ordinal);
        Assert.Contains("ref struct", emitted, StringComparison.Ordinal);
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