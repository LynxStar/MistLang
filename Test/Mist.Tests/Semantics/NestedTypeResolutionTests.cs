// Emission stays unprotected: NestedScoping and AssociatedTypes both fill every slot with a nested
// type, and each is blocked behind a different threshold before it can bank. No sample reaches the
// qualification forms under test -- classic refuses them -- so these cover the model instead.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class NestedTypeResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public class Outer
        {
            public class Inner
            {
                public static string Read() { return "inner"; }
            }
        }

        public class Level1
        {
            public class Level2
            {
                public class Level3
                {
                    public class Level4
                    {
                        public static string Depth() { return "deep"; }
                    }
                }
            }
        }

        public class Box<T>
        {
            public class Wrapper
            {
                public T Item;
            }
        }
        """;

    public NestedTypeResolutionTests()
        : base("MistNested_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        int[] numbers = [1, 2, 3];
        """;

    [Fact]
    public void AStaticMemberOnANestedType_ResolvesThroughQualification()
    {
        var invocation = InitializerOf<InvocationExpression>("var read = Outer.Inner.Read();");

        Assert.NotNull(invocation.Method);
        Assert.Equal("Read", invocation.Method.Name);
    }

    [Fact]
    public void QualificationChainsThroughFourLevelsOfNesting()
    {
        var invocation = InitializerOf<InvocationExpression>(
            "var deep = Level1.Level2.Level3.Level4.Depth();");

        Assert.NotNull(invocation.Method);
        Assert.Equal("Depth", invocation.Method.Name);
    }

    [Fact]
    public void ATypeParameterReachedByMemberAccess_StaysAMemberAccess()
    {
        _ = InitializerOf<MemberAccessExpression>("var length = numbers.Length;");
    }

    [Fact]
    public void ANestedTypeIsRecordedOnItsDeclaringType()
    {
        var outer = Declared("Probe.Outer");

        var nested = Assert.Single(outer.NestedTypes);
        Assert.Equal("Inner", nested.Name);
        Assert.Same(outer, nested.DeclaringType);
    }

    [Fact]
    public void ANestedTypeIsRegisteredUnderItsDottedPath()
    {
        Assert.True(BuildModel("var unused = 1;", "", "", out _)
            .TypesByFQN.ContainsKey("Probe.Level1.Level2.Level3.Level4"));
    }

    [Fact]
    public void ANestedTypeSeesTheEnclosingTypesTypeParameters()
    {
        var wrapper = Assert.IsAssignableFrom<ConcreteType>(
            Declared("Probe.Box<>").NestedTypes.Single(nested => nested.Name == "Wrapper"));

        var item = Assert.Single(wrapper.FieldsList);
        _ = Assert.IsType<TypeParameter>(item.Type);
        Assert.Equal("T", item.Type.Name);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private MemberType Declared(string fullyQualifiedName)
    {
        var model = BuildModel("var unused = 1;", "", extraPreamble: "", out _);
        return Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[fullyQualifiedName]);
    }
}