// Tier 3 (Component): what a conformer emits for a GENERIC abstract's default. Asserted on the emitted
// text because the failure the sample would see is Roslyn's: a default restated with the abstract's T
// in a class where no T is in scope, in its body as much as its signature -- which is the defect
// classic ships and this backend must not copy. See ProgramModelProbe for the harness.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed partial class RestatedDefaultEmissionTests : ProgramModelProbe
{
    public RestatedDefaultEmissionTests()
        : base("MistRestatedDefault_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract Holder<T>
        {
            T Item();

            T Same()
            {
                T held = Item();
                return held;
            }
        }

        public class IntHolder : Holder<int>
        {
            public int Item()
            {
                return 5;
            }
        }

        public class Wrapper<U> : Holder<U>
        {
            private U held;

            public Wrapper(U held)
            {
                this.held = held;
            }

            public U Item()
            {
                return held;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [GeneratedRegex(@"\bT\b")]
    private static partial Regex AbstractParameter();

    private string EmitType(string fullyQualifiedName)
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
    public void ADefaultIsRestatedWithTheConformerArgumentInSignatureAndBody()
    {
        var text = EmitType("Probe.IntHolder");

        Assert.Contains("public int Same()", text);
        Assert.Contains("int held = ", text);
        Assert.DoesNotMatch(AbstractParameter(), text);
    }

    [Fact]
    public void AGenericConformerRestatesUnderItsOwnParameter()
    {
        var text = EmitType("Probe.Wrapper<>");

        Assert.Contains("public U Same()", text);
        Assert.Contains("U held = ", text);
        Assert.DoesNotMatch(AbstractParameter(), text);
    }
}