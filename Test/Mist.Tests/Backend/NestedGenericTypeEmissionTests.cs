// Tier 3 (Component): how a generic nested type is spelled once it is carried under an instantiation of
// its enclosing generic. Asserted on the emitted text, because a derivation that spells the nested type by
// its declaring type's own parameters names a type parameter C# has no binding for there.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class NestedGenericTypeEmissionTests
{
    private const string Types = """
        public class Outer<T>
        {
            public class Inner<U>
            {
                public int Size() { return 7; }
            }

            public Inner<int> Make() { return new Inner<int>(); }
        }

        public class Swarm : Outer<string>
        {
            public Inner<int> Made() { return new Inner<int>(); }
        }
        """;

    [Theory]
    [InlineData("Probe", "Probe.Outer<string>.Inner<int> written = new Probe.Outer<string>().Make();")]
    [InlineData("Swarm", "public Probe.Outer<string>.Inner<int> Made()")]
    [InlineData("Swarm", "return new Probe.Outer<string>.Inner<int>();")]
    public void AGenericNestedTypeIsSpelledUnderItsEnclosingInstantiation(string typeName, string spelled)
    {
        Assert.Contains(spelled, EmitDeclaration(typeName));
    }

    private static string EmitDeclaration(string typeName)
    {
        var model = StandardModelProbe.Build("Outer<string>.Inner<int> written = new Outer<string>().Make();", out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}