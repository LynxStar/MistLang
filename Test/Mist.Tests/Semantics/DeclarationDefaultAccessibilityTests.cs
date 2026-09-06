using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// What a declaration that names no accessibility resolves to. Asserted on the model rather than on
/// emitted C# because a defaulted `private` and a written `private` produce the same output, so
/// reading the emitted text can only show the answer, never that the default is what supplied it.
/// <para>
/// No banked sample reaches this: all 45 spell every constructor `public`. samples/Attributes is the
/// only source in the tree that leaves one bare, which is why the divergence survived 45 green
/// samples.
/// </para>
/// </summary>
public sealed class DeclarationDefaultAccessibilityTests : ProgramModelProbe
{
    public DeclarationDefaultAccessibilityTests()
        : base("MistDefaultAccess_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Bare
        {
            int slot;

            Bare(int slot)
            {
                this.slot = slot;
            }

            int Read()
            {
                return slot;
            }
        }

        public class Spelled
        {
            private Spelled()
            {
            }
        }

        public struct BareStruct
        {
            int slot;

            BareStruct(int slot)
            {
                this.slot = slot;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    private ConcreteType TypeNamed(string name)
    {
        var model = Build($$"""
            namespace Probe;

            {{Fixtures}}
            """, out _);

        return Assert.IsType<ConcreteType>(model.TypesByFQN[$"Probe.{name}"], exactMatch: false);
    }

    [Fact]
    public void ConstructorWithNoAccessModifier_IsPublic()
    {
        var constructor = Assert.Single(TypeNamed("Bare").ConstructorsList);

        Assert.Equal(Accessibility.Public, constructor.Modifiers.Accessibility);
    }

    [Fact]
    public void StructConstructorWithNoAccessModifier_IsPublic()
    {
        var constructor = Assert.Single(TypeNamed("BareStruct").ConstructorsList);

        Assert.Equal(Accessibility.Public, constructor.Modifiers.Accessibility);
    }

    [Fact]
    public void ConstructorSpelledPrivate_StaysPrivate()
    {
        var constructor = Assert.Single(TypeNamed("Spelled").ConstructorsList);

        Assert.Equal(Accessibility.Private, constructor.Modifiers.Accessibility);
    }

    [Fact]
    public void MethodWithNoAccessModifier_StaysPrivate()
    {
        var method = Assert.Single(TypeNamed("Bare").MethodsList, candidate => candidate.Name == "Read");

        Assert.Equal(Accessibility.Private, method.Modifiers.Accessibility);
    }

    [Fact]
    public void FieldWithNoAccessModifier_StaysPrivate()
    {
        var field = Assert.Single(TypeNamed("Bare").FieldsList);

        Assert.Equal(Accessibility.Private, field.Modifiers.Accessibility);
    }
}