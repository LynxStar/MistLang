// ambient-type-provider-v2, Property 15: Base type resolution cache coherence
//
// **Validates: Requirements 17.1, 17.4**
//
// For projected classes with non-Object base, verify BaseType is reference-equal
// to independent ResolveType call. Uses known BCL inheritance chains. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for projected classes with a non-Object base class,
/// <see cref="TypeProjectorV2.ResolveBaseType"/> sets BaseType to the same
/// reference returned by an independent <c>resolveType</c> callback.
/// </summary>
public sealed class BaseTypeCacheCoherenceV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    [Fact]
    public void ResolveBaseType_MemoryStream_SetsBaseTypeToStreamShell()
    {
        // Arrange: create shells for both Stream and MemoryStream
        var streamShell = projector.CreateTypeShell(typeof(Stream), "System.IO");
        var memoryStreamShell = projector.CreateTypeShell(typeof(MemoryStream), "System.IO");

        Assert.NotNull(streamShell);
        Assert.NotNull(memoryStreamShell);

        // Act: resolve base type with a callback that returns the stream shell
        var success = TypeProjectorV2.ResolveBaseType(
            memoryStreamShell!, typeof(MemoryStream),
            (ns, name) => ns == "System.IO" && name == "Stream" ? streamShell : null);

        // Assert: BaseType is reference-equal to the shell returned by callback
        Assert.True(success);
        var concreteShell = (ConcreteType)memoryStreamShell!;
        Assert.True(
            ReferenceEquals(concreteShell.BaseType, streamShell),
            "BaseType should be reference-equal to the Stream shell returned by resolveType callback");
    }

    [Fact]
    public void ResolveBaseType_FileStream_SetsBaseTypeToStreamShell()
    {
        // Arrange: FileStream also inherits from Stream
        var streamShell = projector.CreateTypeShell(typeof(Stream), "System.IO");
        var fileStreamShell = projector.CreateTypeShell(typeof(FileStream), "System.IO");

        Assert.NotNull(streamShell);
        Assert.NotNull(fileStreamShell);

        // Act
        var success = TypeProjectorV2.ResolveBaseType(
            fileStreamShell!, typeof(FileStream),
            (ns, name) => ns == "System.IO" && name == "Stream" ? streamShell : null);

        // Assert
        Assert.True(success);
        var concreteShell = (ConcreteType)fileStreamShell!;
        Assert.True(
            ReferenceEquals(concreteShell.BaseType, streamShell),
            "FileStream's BaseType should be reference-equal to the Stream shell");
    }

    [Fact]
    public void ResolveBaseType_StructType_HasNullBaseType()
    {
        // Arrange: value types inherit from System.ValueType which is excluded
        var intShell = projector.CreateTypeShell(typeof(int), "System");

        Assert.NotNull(intShell);

        // Act: resolve base type (System.ValueType is excluded)
        var success = TypeProjectorV2.ResolveBaseType(
            intShell!, typeof(int),
            (_, _) => throw new InvalidOperationException("Should not be called for ValueType base"));

        // Assert: succeeds but BaseType remains null (ValueType is implicit)
        Assert.True(success);
        var concreteShell = (ConcreteType)intShell!;
        Assert.Null(concreteShell.BaseType);
    }

    [Fact]
    public void ResolveBaseType_TypeInheritingObject_HasNullBaseType()
    {
        // Arrange: Exception inherits from Object directly
        var exceptionShell = projector.CreateTypeShell(typeof(Exception), "System");

        Assert.NotNull(exceptionShell);

        // Act: resolve base type (Object is implicit, excluded)
        var success = TypeProjectorV2.ResolveBaseType(
            exceptionShell!, typeof(Exception),
            (_, _) => throw new InvalidOperationException("Should not be called for Object base"));

        // Assert: succeeds but BaseType remains null (Object is implicit)
        Assert.True(success);
        var concreteShell = (ConcreteType)exceptionShell!;
        Assert.Null(concreteShell.BaseType);
    }

    [Fact]
    public void ResolveBaseType_UnresolvableBase_ReturnsFalse()
    {
        // Arrange: MemoryStream inherits from Stream, but callback returns null (unresolvable)
        var memoryStreamShell = projector.CreateTypeShell(typeof(MemoryStream), "System.IO");

        Assert.NotNull(memoryStreamShell);

        // Act: callback returns null — simulates unresolvable base type
        var success = TypeProjectorV2.ResolveBaseType(
            memoryStreamShell!, typeof(MemoryStream),
            (_, _) => null);

        // Assert: returns false (type should be hidden)
        Assert.False(success);
    }

    [Fact]
    public void ResolveBaseType_EnumType_SkipsEnumBase()
    {
        // Arrange: enums inherit from System.Enum which is excluded
        var enumShell = projector.CreateTypeShell(typeof(DayOfWeek), "System");

        Assert.NotNull(enumShell);

        // Act: resolve base type — EnumType is not ConcreteType, so it returns true immediately
        var success = TypeProjectorV2.ResolveBaseType(
            enumShell!, typeof(DayOfWeek),
            (_, _) => throw new InvalidOperationException("Should not be called for Enum base"));

        // Assert: succeeds (enums don't have base types in this sense)
        Assert.True(success);
    }

    [Fact]
    public void ResolveBaseType_Interface_SkipsBaseResolution()
    {
        // Arrange: interfaces don't have base type resolution in the concrete sense
        var ifaceShell = projector.CreateTypeShell(typeof(IDisposable), "System");

        Assert.NotNull(ifaceShell);

        // Act: AbstractType is not ConcreteType, so ResolveBaseType returns true immediately
        var success = TypeProjectorV2.ResolveBaseType(
            ifaceShell!, typeof(IDisposable),
            (_, _) => throw new InvalidOperationException("Should not be called for interface"));

        // Assert: succeeds without calling the callback
        Assert.True(success);
    }

    [Fact]
    public void ResolveBaseType_MultipleDerivations_EachGetsCorrectCacheReference()
    {
        // Arrange: both MemoryStream and FileStream resolve to the same Stream shell
        var streamShell = projector.CreateTypeShell(typeof(Stream), "System.IO");
        var memoryStreamShell = projector.CreateTypeShell(typeof(MemoryStream), "System.IO");
        var fileStreamShell = projector.CreateTypeShell(typeof(FileStream), "System.IO");

        Assert.NotNull(streamShell);
        Assert.NotNull(memoryStreamShell);
        Assert.NotNull(fileStreamShell);

        // Act: resolve both with the same callback
        IMistType? resolveCallback(string ns, string name)
        {
            return ns == "System.IO" && name == "Stream" ? streamShell : null;
        }

        var success1 = TypeProjectorV2.ResolveBaseType(
            memoryStreamShell!, typeof(MemoryStream), resolveCallback);
        var success2 = TypeProjectorV2.ResolveBaseType(
            fileStreamShell!, typeof(FileStream), resolveCallback);

        // Assert: both succeed and both reference the same Stream shell
        Assert.True(success1);
        Assert.True(success2);

        var memConcrete = (ConcreteType)memoryStreamShell!;
        var fileConcrete = (ConcreteType)fileStreamShell!;

        Assert.True(
            ReferenceEquals(memConcrete.BaseType, streamShell),
            "MemoryStream.BaseType should be reference-equal to Stream shell");
        Assert.True(
            ReferenceEquals(fileConcrete.BaseType, streamShell),
            "FileStream.BaseType should be reference-equal to Stream shell");
        Assert.True(
            ReferenceEquals(memConcrete.BaseType, fileConcrete.BaseType),
            "Both derived types should share the same BaseType reference");
    }
}