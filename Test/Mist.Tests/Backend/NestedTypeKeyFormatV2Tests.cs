// ambient-type-provider-v2, Property 14: Nested type key format
//
// **Validates: Requirements 16.1, 16.2, 16.3, 16.4**
//
// For projected nested types, verify correct dot-separated key with parent arity notation.
// Uses [Fact] tests with known BCL types. No process spawning.

using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for projected nested types, <see cref="TypeProjectorV2.BuildNestedTypeKey"/>
/// produces the correct dot-separated key format with parent arity notation.
/// </summary>
public sealed class NestedTypeKeyFormatV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    [Fact]
    public void BuildNestedTypeKey_ListEnumerator_CorrectFormat()
    {
        var parentShell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        var key = TypeProjectorV2.BuildNestedTypeKey(parentShell!, "Enumerator");

        Assert.Equal("List<>.Enumerator", key);
    }

    [Fact]
    public void BuildNestedTypeKey_DictionaryEnumerator_CorrectFormat()
    {
        var parentShell = projector.CreateTypeShell(typeof(Dictionary<,>), "System.Collections.Generic");

        var key = TypeProjectorV2.BuildNestedTypeKey(parentShell!, "Enumerator");

        Assert.Equal("Dictionary<,>.Enumerator", key);
    }

    [Fact]
    public void BuildNestedTypeKey_DictionaryKeyCollection_CorrectFormat()
    {
        var parentShell = projector.CreateTypeShell(typeof(Dictionary<,>), "System.Collections.Generic");

        var key = TypeProjectorV2.BuildNestedTypeKey(parentShell!, "KeyCollection");

        Assert.Equal("Dictionary<,>.KeyCollection", key);
    }

    [Fact]
    public void BuildNestedTypeKey_NonGenericParent_NonGenericNested_PlainDotSeparated()
    {
        var parentShell = projector.CreateTypeShell(typeof(Environment), "System");

        var key = TypeProjectorV2.BuildNestedTypeKey(parentShell!, "SpecialFolder");

        Assert.Equal("Environment.SpecialFolder", key);
    }

    [Fact]
    public void BuildNestedTypeKey_GenericParent_GenericNested_BothArityNotated()
    {
        var parentShell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        var key = TypeProjectorV2.BuildNestedTypeKey(parentShell!, "Inner<>");

        Assert.Equal("List<>.Inner<>", key);
    }
}