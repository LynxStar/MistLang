using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: a union over a pack element is spelled per instantiation, so the declaration owning the pack
// is emitted once per instantiation -- a type, or a type's method -- and refused only where the pack
// belongs to a declaration the backend cannot emit that way, an extension block's method. An optional
// over an element whose pack is constrained to one kind is spelled per arity and is neither; an alias is
// erased and emits no union of its own.
[Trait("Tier", "3")]
public sealed class PackElementUnionRefusalTests : ProgramModelProbe
{
    public PackElementUnionRefusalTests()
        : base("MistPackElementUnion_")
    {
    }

    protected override string Fixtures { get; } = "public class Box<T> { public T? Value; public Box() { } }";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[1]? Maybe; public Wrap() { } }", "Wrap", null)]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[0] | string Either; public Wrap(TArgs[0] e) { Either = e; } }", "Wrap", null)]
    [InlineData("public class Wrap<Type[2]:TArgs> { public Box<TArgs[1]> Held; public Wrap() { Held = new Box<TArgs[1]>(); } }", "Wrap", null)]
    [InlineData("public class Wrap { public static int Use<Type[2]:T>(T[0]? maybe) { return 0; } }", "Wrap", "Use")]
    public void AUnionOverAPackElement_SpecializesTheDeclarationOwningThePack(string declaration, string type, string? method)
    {
        var (model, refusals) = Specialize(declaration, out var specialization);

        using(specialization)
        {
            var owner = (MemberType)model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == $"Probe.{type}").Value;

            Assert.Empty(refusals);
            Assert.True(method is null
                ? UnionSpecialization.Specializes(owner)
                : UnionSpecialization.Specializes(owner.MethodsList.Single(candidate => candidate.Name == method)));
        }
    }

    [Theory]
    [InlineData("public class Wrap<Type[2]:TArgs> where TArgs : struct { public TArgs[1]? Maybe; public Wrap() { } }")]
    [InlineData("public class Wrap<Type[2]:TArgs> where TArgs : class { public TArgs[1]? Maybe; public Wrap() { } }")]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[1] Plain; public Wrap(TArgs[1] p) { Plain = p; } }")]
    public void AnElementWhoseSpellingIsFixedPerArity_IsNeitherRefusedNorSpecialized(string declaration)
    {
        var (model, refusals) = Specialize(declaration, out var specialization);

        using(specialization)
        {
            Assert.Empty(refusals);
            Assert.False(UnionSpecialization.Specializes(model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == "Probe.Wrap").Value));
        }
    }

    [Fact]
    public void AnAliasOverAPackElement_IsNotRefused()
    {
        var (_, refusals) = Specialize("public type Maybe<Type[1]:T> = T[0]?;", out var specialization);

        using(specialization)
        {
            Assert.Empty(refusals);
        }
    }

    [Fact]
    public void AnExtensionMethodsPack_IsRefusedAtTheMethodNamingTheElement()
    {
        var (_, refusals) = Specialize(
            "public class Holder { } extension(Holder self) { public int Use<Type[2]:T>(T[0]? maybe) { return 0; } }",
            out var specialization);

        using(specialization)
        {
            var refusal = Assert.Single(refusals);

            Assert.Contains("it names 'T[0]'", refusal.Message, StringComparison.Ordinal);
            Assert.NotEmpty(refusal.CompilationUnitPath);
        }
    }

    private (ProgramModel.Model.ProgramModel Model, List<Diagnostic> Refusals) Specialize(string declaration, out IDisposable specialization)
    {
        var model = BuildModel("", declaration, "", out var built);
        var diagnostics = new DiagnosticBag();

        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        specialization = UnionSpecialization.Enter(model, diagnostics);

        return (model, [.. diagnostics.ToImmutableArray().Where(d => d.Id.Value == "MIST_BE_UNION_OVER_PACK_ELEMENT")]);
    }
}