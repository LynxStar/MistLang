// Tuple deconstruction declarations: where each target's type comes from, and what is refused. Asserted
// on the model because the two Mist forms lower to ONE C# shape, so output cannot distinguish a target
// typed by the element from one C# inferred for itself -- and the difference is what makes a target over
// an unknown element narrowable. Order 102 of backlog/emitter-v2-capability-progression.yaml rests on
// this file.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class TupleDeconstructionResolutionTests : ProgramModelProbe
{
    public TupleDeconstructionResolutionTests()
        : base("MistTupleDeconstruct_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Greeter
        {
            public string Name { get; set; }
        }
        """;

    protected override string Preamble { get; } = """
        (int, string) pair = (1, "a");
        (int, unknown) opaque = (1, "a");
        Greeter g = new Greeter();
        """;

    [Fact]
    public void TheVarFormTypesEachTargetByTheElementItReads()
    {
        var targets = TargetsOf("var (n, s) = pair;");

        Assert.Equal(["n", "s"], targets.Select(target => target.Name));
        Assert.Equal(
            ["Standard.Int32", "Standard.String"],
            targets.Select(target => target.Type.FullyQualifiedName));
    }

    [Fact]
    public void TheVarFormOverAnUnknownElement_TypesThatTargetUnknown()
    {
        var targets = TargetsOf("var (n, data) = opaque;");

        Assert.Equal("unknown", targets[1].Type.Name);
        BindsCleanly("var (n, data) = opaque; if (data is string ds) { var probe = ds; }");
    }

    [Fact]
    public void TheTypedFormTypesEachTargetByWhatItDeclares()
    {
        var targets = TargetsOf("(long u, string v) = pair;");

        Assert.Equal(["u", "v"], targets.Select(target => target.Name));
        Assert.Equal("Standard.Int64", targets[0].Type.FullyQualifiedName);
    }

    /// <summary>
    /// A target-typed <c>new()</c> has no type of its own, so the initializer binds ONLY if the DECLARED
    /// targets were assembled into a tuple and pushed as its target type.
    /// </summary>
    [Fact]
    public void TheTypedFormPushesItsDeclaredTargetsOntoTheInitializer()
    {
        var statement = DeconstructionOf("(int a, Greeter b) = (1, new());");
        var literal = Assert.IsType<TupleLiteralExpression>(statement.Initializer);

        Assert.Equal("Probe.Greeter", literal.Elements[1].Type.FullyQualifiedName);
    }

    [Fact]
    public void ATargetCountOtherThanTheSourcesArityIsRefused()
    {
        var diagnostics = BuildRun("var (n, s, extra) = pair;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_DECONSTRUCTION_ARITY"));
    }

    /// <summary>
    /// The var form's repetition is <c>*</c> where its typed sibling's is a required second element, so a
    /// ONE-target deconstruction parses. Nothing can satisfy it: a tuple needs two elements, so the arity
    /// it asks for is unreachable rather than merely unmatched.
    /// </summary>
    [Fact]
    public void ASingleTargetVarFormParsesAndIsRefusedOnArity()
    {
        var diagnostics = BuildRun("var (only) = pair;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_DECONSTRUCTION_ARITY"));
    }

    [Fact]
    public void ASourceThatIsNotATupleIsRefused()
    {
        var diagnostics = BuildRun("var (n, s) = g;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_DECONSTRUCTION_SOURCE"));
    }

    [Fact]
    public void EveryTargetIsReadableAfterTheDeclaration()
    {
        BindsCleanly("var (n, s) = pair; var probe = $\"{n}{s}\";");
    }

    private TupleDeconstructionStatement DeconstructionOf(string statement)
    {
        return BodyIn(statement).Statements.OfType<TupleDeconstructionStatement>().Last();
    }

    private IReadOnlyList<LocalVariable> TargetsOf(string statement)
    {
        return DeconstructionOf(statement).Targets;
    }

    private void BindsCleanly(string statement)
    {
        Assert.Empty(BuildRun(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}