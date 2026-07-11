// ambient-type-provider-v2, Property 9: Generic arity correctness
//
// **Validates: Requirements 10.1, 10.2, 10.5**
//
// For projected generic definitions, verify TypeParametersList.Count matches CLR generic parameter count.
// Verify arity-bearing name registration format.
// Uses [Fact] tests with known BCL types. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for projected CLR generic definitions, <see cref="TypeProjectorV2"/>
/// produces the correct arity-bearing name and populates TypeParametersList with the
/// matching number of type parameters.
/// </summary>
public sealed class GenericArityCorrectnessV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    /// <summary>
    /// A permissive resolve callback that returns a stub type for any requested resolution.
    /// </summary>
    private static IMistType? Resolve(string ns, string name)
    {
        return new ClassType(name, ns, new Scope(null), null, null);
    }

    [Fact]
    public void CreateTypeShell_ListT_HasArityBearingName()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        Assert.Equal("List<>", shell!.Name);
    }

    [Fact]
    public void ProjectTypeParameters_ListT_HasOneTypeParameter()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        _ = TypeProjectorV2.ProjectTypeParameters(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        _ = Assert.Single(memberShell.TypeParametersList);
    }

    [Fact]
    public void CreateTypeShell_DictionaryTKeyTValue_HasArityBearingName()
    {
        var shell = projector.CreateTypeShell(typeof(Dictionary<,>), "System.Collections.Generic");

        Assert.Equal("Dictionary<,>", shell!.Name);
    }

    [Fact]
    public void ProjectTypeParameters_DictionaryTKeyTValue_HasTwoTypeParameters()
    {
        var shell = projector.CreateTypeShell(typeof(Dictionary<,>), "System.Collections.Generic");

        _ = TypeProjectorV2.ProjectTypeParameters(shell!, typeof(Dictionary<,>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Equal(2, memberShell.TypeParametersList.Count);
    }

    [Fact]
    public void ProjectTypeParameters_FuncT_HasOneTypeParameter()
    {
        var shell = projector.CreateTypeShell(typeof(Func<>), "System");

        _ = TypeProjectorV2.ProjectTypeParameters(shell!, typeof(Func<>), Resolve);

        var memberShell = (MemberType)shell!;
        _ = Assert.Single(memberShell.TypeParametersList);
    }

    [Fact]
    public void CreateTypeShell_String_HasSimpleName()
    {
        var shell = projector.CreateTypeShell(typeof(string), "System");

        Assert.Equal("String", shell!.Name);
    }

    [Fact]
    public void ProjectTypeParameters_String_HasZeroTypeParameters()
    {
        var shell = projector.CreateTypeShell(typeof(string), "System");

        _ = TypeProjectorV2.ProjectTypeParameters(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Empty(memberShell.TypeParametersList);
    }

    [Fact]
    public void ProjectMethods_ListT_GenericMethodHasCorrectTypeParameterCount()
    {
        // List<T>.ConvertAll<TOutput> is a generic method with 1 type parameter
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        _ = TypeProjectorV2.ProjectTypeParameters(shell!, typeof(List<>), Resolve);
        projector.ProjectMethods(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        var convertAll = memberShell.MethodsList.FirstOrDefault(m => m.Name == "ConvertAll");
        Assert.NotNull(convertAll);
        _ = Assert.Single(convertAll.TypeParametersList);
    }
}