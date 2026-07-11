// ambient-type-provider-v2, Property 7: Type kind projection correctness
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7**
//
// For projected CLR types from loaded assemblies, verify correct MistType subclass mapping.
// Uses [Fact] tests with known BCL types. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for each CLR type kind, <see cref="TypeProjectorV2.CreateTypeShell"/>
/// returns the correct <see cref="MistType"/> subclass with appropriate modifier flags.
/// </summary>
public sealed class TypeKindProjectionV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    [Fact]
    public void CreateTypeShell_AbstractClass_ReturnsClassTypeWithIsAbstract()
    {
        var result = projector.CreateTypeShell(typeof(Stream), "System.IO");

        _ = Assert.IsType<ClassType>(result);
        var classType = (ClassType)result!;
        Assert.True(classType.Modifiers.IsAbstract);
        Assert.False(classType.Modifiers.IsSealed);
    }

    [Fact]
    public void CreateTypeShell_SealedClass_ReturnsClassTypeWithIsSealed()
    {
        var result = projector.CreateTypeShell(typeof(string), "System");

        _ = Assert.IsType<ClassType>(result);
        var classType = (ClassType)result!;
        Assert.True(classType.Modifiers.IsSealed);
        Assert.False(classType.Modifiers.IsAbstract);
    }

    [Fact]
    public void CreateTypeShell_NonSealedNonAbstractClass_ReturnsClassTypeWithNoModifiers()
    {
        var result = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        _ = Assert.IsType<ClassType>(result);
        var classType = (ClassType)result!;
        Assert.False(classType.Modifiers.IsAbstract);
        Assert.False(classType.Modifiers.IsSealed);
    }

    [Fact]
    public void CreateTypeShell_Interface_ReturnsAbstractType()
    {
        var result = projector.CreateTypeShell(typeof(IDisposable), "System");

        _ = Assert.IsType<AbstractType>(result);
    }

    [Fact]
    public void CreateTypeShell_Struct_ReturnsStructTypeWithIsRefFalse()
    {
        var result = projector.CreateTypeShell(typeof(int), "System");

        _ = Assert.IsType<StructType>(result);
        var structType = (StructType)result!;
        Assert.False(structType.IsRef);
    }

    [Fact]
    public void CreateTypeShell_AnotherStruct_ReturnsStructTypeWithIsRefFalse()
    {
        var result = projector.CreateTypeShell(typeof(DateTime), "System");

        _ = Assert.IsType<StructType>(result);
        var structType = (StructType)result!;
        Assert.False(structType.IsRef);
    }

    [Fact]
    public void CreateTypeShell_Enum_ReturnsEnumType()
    {
        var result = projector.CreateTypeShell(typeof(DayOfWeek), "System");

        _ = Assert.IsType<EnumType>(result);
    }

    [Fact]
    public void CreateTypeShell_RefStruct_ReturnsStructTypeWithIsRefTrue()
    {
        var result = projector.CreateTypeShell(typeof(Span<>), "System");

        _ = Assert.IsType<StructType>(result);
        var structType = (StructType)result!;
        Assert.True(structType.IsRef);
    }

    [Fact]
    public void CreateTypeShell_AllProjectedTypes_HaveTypeOriginAssembly()
    {
        var types = new[]
        {
            typeof(Stream),
            typeof(string),
            typeof(List<>),
            typeof(IDisposable),
            typeof(int),
            typeof(DateTime),
            typeof(DayOfWeek),
            typeof(Span<>),
        };

        foreach(var clrType in types)
        {
            var result = projector.CreateTypeShell(clrType, "TestNamespace");

            Assert.NotNull(result);
            Assert.Equal(TypeOrigin.Assembly, result!.Origin);
        }
    }
}