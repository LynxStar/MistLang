using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test for Type Kind Projection Correctness (Property 2).
/// For any public CLR type that is not excluded by the visibility filter,
/// the TypeProjector SHALL produce a Mist type whose kind matches the CLR
/// type's classification: classes → ClassType, structs → StructType,
/// interfaces → AbstractType, enums → EnumType.
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
/// </summary>
public sealed class TypeKindProjectionPropertyTests
{
    private readonly TypeProjector _projector;
    private readonly VisibilityFilter _filter;
    private readonly IStdlibTypeProvider _stdlib;

    /// <summary>
    /// All public, non-excluded types exported from the InteropFixtures assembly.
    /// </summary>
    private static readonly Type[] FixtureTypes = BuildFixtureTypes();

    public TypeKindProjectionPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        _filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, _filter);
        _stdlib = FallbackStdlibTypeProvider.Instance;
    }

    private static Type[] BuildFixtureTypes()
    {
        var assembly = typeof(InteropFixtures.SimpleClass).Assembly;
        var filter = new VisibilityFilter();

        return assembly.GetExportedTypes()
            .Where(t => !filter.IsTypeExcluded(t))
            .ToArray();
    }

    /// <summary>
    /// Generates a random index into the FixtureTypes array, selecting a random
    /// non-excluded public type from the fixture assembly.
    /// </summary>
    private static Gen<Type> GenFixtureType()
    {
        return Gen.Choose(0, FixtureTypes.Length - 1)
            .Select(idx => FixtureTypes[idx]);
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Determines the expected Mist type kind for a CLR type based on its classification.
    /// </summary>
    private static Type ExpectedMistTypeKind(Type clrType)
    {
        if(clrType.IsEnum)
        {
            return typeof(EnumType);
        }

        if(clrType.IsInterface)
        {
            return typeof(AbstractType);
        }

        if(clrType.IsValueType)
        {
            return typeof(StructType);
        }

        return typeof(ClassType);
    }

    // ── Property 2: Type Kind Projection Correctness ─────────────────
    // For any public CLR type not excluded by the visibility filter,
    // the projected Mist type's kind matches the CLR classification.

    [Fact]
    public void P2_TypeKindProjection_MatchesCLRClassification()
    {
        RunProperty(GenFixtureType(), clrType =>
        {
            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);

            var expectedKind = ExpectedMistTypeKind(clrType);
            var actualKind = projectedType.GetType();

            Assert.True(
                actualKind == expectedKind,
                $"CLR type '{clrType.FullName}' (IsEnum={clrType.IsEnum}, IsInterface={clrType.IsInterface}, " +
                $"IsValueType={clrType.IsValueType}) should project to {expectedKind.Name} " +
                $"but got {actualKind.Name}");
        });
    }
}