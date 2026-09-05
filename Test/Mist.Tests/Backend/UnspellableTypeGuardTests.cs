using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Emitter;
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

        public class Generic<T>
        {
            public int length;
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

    [Fact]
    public void RefinedFieldType_IsRefused()
    {
        AssertSingle(GuardOn("Person"), "MIST_BE_REFINED_TYPE");
    }

    /// <summary>
    /// A return type and a parameter are separate positions, so a method using the refinement in
    /// both is refused twice. Emitting either would break the file on its own.
    /// </summary>
    [Fact]
    public void RefinedReturnAndParameter_AreBothRefused()
    {
        var diagnostics = GuardOn("Greeter");

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("MIST_BE_REFINED_TYPE", d.Id.Value));
    }

    [Fact]
    public void DependentTypeParameter_IsRefused()
    {
        AssertSingle(GuardOn("Sized"), "MIST_BE_DEPENDENT_TYPE_PARAMETER");
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
}