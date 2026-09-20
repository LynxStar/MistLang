// Tier 3 (Component): a generic method named as a value on the experimental pipeline. The model holds the
// type arguments the target taught it, and the emitter states them, C# inferring a method group's only
// from a delegate it may not be looking at -- a carrier's parameter, a method emitted per instantiation.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class GenericMethodGroupEmissionTests
{
    private const string Types = """
        public class Api
        {
            public static T Same<T>(T value) { return value; }

            public static string Lift<K>(K? value) { return "lifted"; }

            public static int Apply(func<int, int> f, int value) { return f(value); }
        }

        public class Box
        {
            public Box() { }

            public string Show<T>(T value) { return "shown"; }
        }

        public class Keeper<K>
        {
            public K Keep(K value)
            {
                func<K, K> same = Api.Same;
                return same(value);
            }
        }
        """;

    [Theory]
    [InlineData("func<int, int> same = Api.Same;", "= Probe.Api.Same<int>;")]
    [InlineData("func<string, string> same = Api.Same;", "= Probe.Api.Same<string>;")]
    [InlineData("var applied = Api.Apply(Api.Same, 3);", "Probe.Api.Apply(Probe.Api.Same<int>, 3)")]
    [InlineData("var box = new Box(); func<bool, string> show = box.Show;", "= box.Show<bool>;")]
    [InlineData("func<int?, string> lift = Api.Lift;", "= Probe.Api.Lift__Standard_Int32;")]
    [InlineData("func<long, long> same = Api.Same<long>;", "= Probe.Api.Same<long>;")]
    [InlineData("func<string?, string> lift = Api.Lift<string>;", "= Probe.Api.Lift__Standard_String;")]
    [InlineData("var applied = Api.Apply(Api.Same<int>, 3);", "Probe.Api.Apply(Probe.Api.Same<int>, 3)")]
    public void AGenericMethodNamedAsAValue_StatesTheTypeArgumentsTheTargetTaughtIt(string statements, string expected)
    {
        Assert.Contains(expected, Emit("Probe", statements));
    }

    [Fact]
    public void AGenericMethodNamedInsideGenericCode_IsStatedAtThatCodesOwnParameter()
    {
        Assert.Contains("= Probe.Api.Same<K>;", Emit("Keeper<>", ""));
    }

    private static string Emit(string typeName, string statements)
    {
        var model = StandardModelProbe.Build(statements, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}