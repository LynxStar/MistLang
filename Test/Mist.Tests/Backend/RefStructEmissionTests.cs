// Where `ref` on a struct survives emission. Asserted on the emitted TEXT because nothing downstream
// can see it: a ref struct and a plain one holding the same values compile, run and print
// identically, so AllFeaturesV2 cannot guard the modifier at all -- only its absence from a stack-
// only guarantee would ever show, and not as output.

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
    /// C# has no ref class, and Mist PARSES one -- classic reports MIST2001 for it while v2 declines
    /// to emit and stays silent, refusing a program being analysis-phase work. So the flag really does
    /// reach a class here, and dropping the struct guard really does spell a type that will not
    /// compile. That is what this pins; the diagnostic is listed on the capability.
    /// </summary>
    [Fact]
    public void AClassNeverGainsIt()
    {
        Assert.Contains("public class RefClass", EmitDeclaration("RefClass"), StringComparison.Ordinal);
        Assert.DoesNotContain("ref class", EmitDeclaration("RefClass"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The layout attribute every struct carries is legal on a ByRefLike one, which is worth pinning
    /// because it is emitted before the declaration and would refuse the whole type if it were not.
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