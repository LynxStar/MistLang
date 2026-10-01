// Tier 3 (Component): the backend's defaultability predicate and the constructor it lowers a default
// block into, over a real ProgramModel. No Roslyn and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DefaultSynthesisTests : ProgramModelProbe
{
    public DefaultSynthesisTests() : base("mist-default-synthesis-")
    {
    }

    protected override string Fixtures => "";

    protected override string Preamble => "";

    [Fact]
    public void DefaultBlock_MakesATypeWithAnArgumentTakingConstructorDefaultable()
    {
        Assert.True(IsDefaultable(
            """
            public class Config
            {
                public int Retries;

                public Config(int retries) { this.Retries = retries; }

                default { Retries = 1 }
            }
            """,
            "Config"));
    }

    [Fact]
    public void ArgumentTakingConstructorAlone_LeavesATypeNonDefaultable()
    {
        Assert.False(IsDefaultable(
            """
            public class Connection
            {
                public int Timeout;

                public Connection(int timeout) { this.Timeout = timeout; }
            }
            """,
            "Connection"));
    }

    [Fact]
    public void FieldHoldingADefaultableType_IsAnswerEnough()
    {
        Assert.True(IsDefaultable(
            """
            public class Inner { public int Value; }

            public class Outer { public int Count; public Inner Child; }
            """,
            "Outer"));
    }

    [Fact]
    public void FieldHoldingItsOwnType_LeavesTheTypeNonDefaultable()
    {
        Assert.False(IsDefaultable(
            "public class Node { public int Value; public Node Next; }",
            "Node"));
    }

    [Fact]
    public void OptionalField_DefaultsToAbsence()
    {
        Assert.Contains("this.MaybeValue = null;", Emit(
            "public class Holder { public int? MaybeValue; }",
            "Holder"), StringComparison.Ordinal);
    }

    [Fact]
    public void TypeBindingEntry_BuildsTheBoundTypeAndAppliesTheNestedOverrides()
    {
        var emitted = Emit(
            """
            public abstract Shape { string Describe(); }

            public class Circle : Shape
            {
                public int Radius;
                public string Describe() { return "circle"; }
            }

            public class Scene
            {
                public Shape Primary;

                default { Primary : Circle { Radius = 7 } }
            }
            """,
            "Scene");

        Assert.Contains("var __default_Primary = Probe.Circle.__CreateDefault();", emitted, StringComparison.Ordinal);
        Assert.Contains("__default_Primary.Radius = 7;", emitted, StringComparison.Ordinal);
        Assert.Contains("this.Primary = __default_Primary;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredConstructor_RoutesDefaultingThroughTheSentinelRatherThanAPublicParameterless()
    {
        var emitted = Emit(
            """
            public class Config
            {
                public int Retries;

                public Config(int retries) { this.Retries = retries; }

                default { Retries = 1 }
            }
            """,
            "Config");

        Assert.Contains("protected Config(", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("public Config()", emitted, StringComparison.Ordinal);
    }

    private bool IsDefaultable(string types, string typeName)
    {
        return DefaultSynthesis.IsDefaultable(TypeNamed(types, typeName));
    }

    private string Emit(string types, string typeName)
    {
        var writer = new CSharpWriter();
        var anyEmitted = false;

        DefaultEmitter.Emit(TypeNamed(types, typeName), writer, new DiagnosticBag(), ref anyEmitted);

        return writer.ToString();
    }

    private ConcreteType TypeNamed(string types, string typeName)
    {
        var model = Build($"namespace Probe;\n\n{types}\n", out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN[$"Probe.{typeName}"]);
    }
}