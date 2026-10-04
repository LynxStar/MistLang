// Where a type's accessibility and `sealed` survive emission. Asserted on the emitted TEXT because
// stdout cannot see either: a sealed class and an open one print alike, and a nested type collapsed
// to C#'s default only shows as a Roslyn refusal of a valid program. A sealed struct is the one
// declaration that must NOT keep its modifier: C# refuses `sealed struct` as CS0106, and a struct
// emitted as one is sealed already.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class TypeModifierEmissionTests : ProgramModelProbe
{
    public TypeModifierEmissionTests()
        : base("MistTypeModifierEmission_")
    {
    }

    protected override string Fixtures { get; } = """
        internal class Hidden
        {
        }

        class Bare
        {
        }

        public sealed class Closed
        {
        }

        public sealed struct Frozen
        {
            public int value;
        }

        internal enum Kind
        {
            One,
        }

        public class Outer
        {
            internal class Mid
            {
            }

            protected class Prot
            {
            }

            private class Priv
            {
            }

            public class Pub
            {
            }

            class Unmarked
            {
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("Hidden", "internal class Hidden")]
    [InlineData("Bare", "internal class Bare")]
    [InlineData("Closed", "public sealed class Closed")]
    [InlineData("Kind", "internal sealed record class Kind")]
    public void ATopLevelTypeSpellsItsAccessibility(string typeName, string header)
    {
        Assert.Contains(header, EmitDeclaration(typeName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("internal class Mid")]
    [InlineData("protected class Prot")]
    [InlineData("private class Priv")]
    [InlineData("public class Pub")]
    [InlineData("private class Unmarked")]
    public void ANestedTypeSpellsItsAccessibility(string header)
    {
        Assert.Contains(header, EmitDeclaration("Outer"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASealedStructDropsTheModifier()
    {
        var emitted = EmitDeclaration("Frozen");

        Assert.Contains("public struct Frozen", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("sealed", emitted, StringComparison.Ordinal);
    }

    private string EmitDeclaration(string typeName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<DeclaredType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var emitDiagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var anyEmitted = false;

        TypeEmitter.Emit(
            type, ConstraintClosureMap.Build(model, emitDiagnostics), model.VariadicUses, writer, emitDiagnostics, ref anyEmitted);

        Assert.Empty(emitDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        return writer.ToString();
    }
}