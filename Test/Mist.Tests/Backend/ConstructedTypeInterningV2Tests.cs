// ambient-type-provider-v2, Property 11: Constructed type interning
//
// **Validates: Requirements 10.4**
//
// Verify same definition + type arguments from different resolution paths produce the same reference.
// Uses TypeInterner.GetOrCreateConstructedType directly (no process spawning).

using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: calling <see cref="TypeInterner.GetOrCreateConstructedType"/> multiple times
/// with the same definition and type arguments returns reference-equal instances.
/// </summary>
public sealed class ConstructedTypeInterningV2Tests
{
    [Fact]
    public void GetOrCreateConstructedType_SameDefinitionAndArgs_ReturnsSameReference()
    {
        var interner = new TypeInterner();
        var definition = new ClassType("List<>", "System.Collections.Generic", new Scope(null), null, null);
        var typeArg = new ClassType("Int32", "Standard", new Scope(null), null, null);

        var first = interner.GetOrCreateConstructedType(definition, new IMistType[] { typeArg });
        var second = interner.GetOrCreateConstructedType(definition, new IMistType[] { typeArg });

        Assert.True(ReferenceEquals(first, second),
            "GetOrCreateConstructedType with the same definition and args should return the same reference.");
    }

    [Fact]
    public void GetOrCreateConstructedType_SameDefinitionDifferentArgs_ReturnsDifferentReference()
    {
        var interner = new TypeInterner();
        var definition = new ClassType("List<>", "System.Collections.Generic", new Scope(null), null, null);
        var typeArg1 = new ClassType("Int32", "Standard", new Scope(null), null, null);
        var typeArg2 = new ClassType("String", "Standard", new Scope(null), null, null);

        var first = interner.GetOrCreateConstructedType(definition, new IMistType[] { typeArg1 });
        var second = interner.GetOrCreateConstructedType(definition, new IMistType[] { typeArg2 });

        Assert.False(ReferenceEquals(first, second),
            "GetOrCreateConstructedType with different args should return different references.");
    }

    [Fact]
    public void GetOrCreateConstructedType_DifferentDefinitionsSameArgs_ReturnsDifferentReference()
    {
        var interner = new TypeInterner();
        var definition1 = new ClassType("List<>", "System.Collections.Generic", new Scope(null), null, null);
        var definition2 = new ClassType("HashSet<>", "System.Collections.Generic", new Scope(null), null, null);
        var typeArg = new ClassType("Int32", "Standard", new Scope(null), null, null);

        var first = interner.GetOrCreateConstructedType(definition1, new IMistType[] { typeArg });
        var second = interner.GetOrCreateConstructedType(definition2, new IMistType[] { typeArg });

        Assert.False(ReferenceEquals(first, second),
            "GetOrCreateConstructedType with different definitions should return different references.");
    }
}