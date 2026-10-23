using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class MonomorphNamespaceEmissionTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void SameNamedFamiliesInTwoNamespaces_AreDeclaredAndNamedEachInItsOwn()
    {
        var alpha = Family("Alpha");
        var beta = Family("Beta");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.True(MonomorphEmitter.Emit(
            [(alpha, [2]), (beta, [2])], ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics));

        var emitted = writer.ToString().ReplaceLineEndings("\n");
        Assert.Contains("namespace Alpha\n{\n    public class Bundle_2<T1, T2>\n", emitted, StringComparison.Ordinal);
        Assert.Contains("namespace Beta\n{\n    public class Bundle_2<T1, T2>\n", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("\npublic class", "\n" + emitted, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());

        var arguments = new LiftedType(built.Type("Type"), new TypePackValue([built.Type("Int32"), built.Type("String")]));
        Assert.StartsWith("Alpha.Bundle_2<", TypeNameEmitter.Emit(new ConstructedType(alpha, [arguments])), StringComparison.Ordinal);
        Assert.StartsWith("Beta.Bundle_2<", TypeNameEmitter.Emit(new ConstructedType(beta, [arguments])), StringComparison.Ordinal);
    }

    private static ClassType Family(string namespaceName)
    {
        var family = new ClassType("Bundle", namespaceName, new Scope(null), null, null);
        family.TypeParametersList.Add(new TypeParameter("Ts", 0, family.Scope, family) { PackKind = TypePackKind.Unbracketed });
        return family;
    }
}