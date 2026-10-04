using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// The backend's refusal of type shapes C# cannot spell. Asserted against the guard rather than
/// against emitted source because the point is that emission never happens: before this existed,
/// both shapes emitted a file that did not parse and produced no Mist diagnostic at all, so a test
/// reading the output could only have asserted the broken spelling.
/// </summary>
public sealed class UnspellableTypeGuardTests : ProgramModelProbe
{
    public UnspellableTypeGuardTests()
        : base("MistUnspellable_")
    {
    }

    protected override string Fixtures { get; } = """
        type Age = int >= 0;

        public class Person
        {
            public Age age;
        }

        public class Greeter
        {
            public Age Describe(Age given)
            {
                return given;
            }
        }

        public class Plain
        {
            public int age;
        }

        public class Sized<Count:int>
        {
            public int length;
        }

        public class Packed<params TArgs>
        {
            public int length;
        }

        public class Generic<T>
        {
            public int length;
        }

        public class Unioned<Type[2]:TArgs>
        {
            public TArgs... Item;
            public TArgs[1]? Maybe;

            public Unioned(params TArgs values) { Item... = values...; }

            public TArgs[0] First() { return Item1; }
        }
        """;

    protected override string Preamble { get; } = "";

    private ImmutableArray<Diagnostic> GuardOn(string simpleName)
    {
        var model = BuildModel("", "", "", out _);

        var type = model.TypesByFQN
            .Where(entry => entry.Key.StartsWith("Probe.", StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .OfType<MemberType>()
            .Single(candidate => MistType.StripArity(candidate.Name) == simpleName);

        var diagnostics = new DiagnosticBag();
        UnspellableTypeGuard.Report(type, diagnostics);
        return diagnostics.ToImmutableArray();
    }

    private static void AssertSingle(ImmutableArray<Diagnostic> diagnostics, string expectedId)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedId, diagnostic.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// A refinement in a signature is spelled as the type it refines. The predicate constrains which values may reach the slot, which is a question
    /// settled before the value gets there rather than a different runtime representation -- so
    /// erasing it in a FIELD, a parameter or a return type loses nothing. Where it may not be
    /// erased is a test, and that refusal lives with the test; see
    /// <see cref="RefinementEmissionTests"/>.
    /// </summary>
    [Theory]
    [InlineData("Person")]
    [InlineData("Greeter")]
    public void RefinedSignature_IsNoLongerRefused(string type)
    {
        Assert.Empty(GuardOn(type));
    }

    /// <summary>
    /// A dependent parameter is spelled as a type parameter constrained to a witness interface carrying the value.
    /// </summary>
    [Fact]
    public void DependentTypeParameter_IsNoLongerRefused()
    {
        Assert.Empty(GuardOn("Sized"));
    }

    /// <summary>
    /// A PACK keeps the refusal, and this is what stops the narrowing above from having quietly
    /// un-refused both. Its domain is Type[], so carrying one means a witness per type LIST, which
    /// this guard cannot spell as a C# type.
    /// </summary>
    [Fact]
    public void TypePack_IsStillRefused()
    {
        AssertSingle(GuardOn("Packed"), "MIST_BE_DEPENDENT_TYPE_PARAMETER");
    }

    /// <summary>
    /// The two negative cases that keep the guard from being a blanket refusal of generics or of
    /// integer-typed members: an ordinary type parameter and an ordinary field both pass.
    /// </summary>
    [Fact]
    public void PlainTypeParameter_IsNotRefused()
    {
        Assert.Empty(GuardOn("Generic"));
    }

    [Fact]
    public void UnrefinedField_IsNotRefused()
    {
        Assert.Empty(GuardOn("Plain"));
    }

    /// <summary>
    /// A pack the instance being emitted closes needs no width: an instantiation fixes its elements, so
    /// its spread field, its element-typed members and its parameters all have a spelling there.
    /// </summary>
    [Fact]
    [Trait("Tier", "3")]
    public void APackClosedByTheInstanceBeingEmitted_IsNotRefused()
    {
        var model = BuildModel("var unioned = new Unioned<int, string>(1, \"a\");", "", "", out _);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model, diagnostics);
        var type = (MemberType)model.TypesByFQN.Single(entry => MistType.StripArity(entry.Key) == "Probe.Unioned").Value;

        using(TypeEmitter.Specializing(Assert.Single(UnionSpecialization.InstancesOf(type))))
        {
            UnspellableTypeGuard.Report(type, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }
}