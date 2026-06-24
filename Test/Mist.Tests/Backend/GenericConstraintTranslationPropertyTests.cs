// Feature: assembly-type-provider, Property 10: Generic Constraint Translation
//
// **Validates: Requirements 4.6, 10.2, 10.3, 10.4, 10.5**

using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test for Generic Constraint Translation (Property 10).
/// For any CLR type parameter with constraints, the TypeProjector SHALL translate:
/// - <c>class</c> → HasClassConstraint
/// - <c>struct</c> → HasStructConstraint
/// - <c>new()</c> → constructor constraint (empty parameter list)
/// - base type/interface constraints → corresponding projected Mist types
/// Constraints with no Mist equivalent SHALL be omitted.
///
/// **Validates: Requirements 4.6, 10.2, 10.3, 10.4, 10.5**
/// </summary>
public sealed class GenericConstraintTranslationPropertyTests
{
    private readonly TypeProjector _projector;
    private readonly VisibilityFilter _filter;
    private readonly IStdlibTypeProvider _stdlib;

    /// <summary>
    /// All generic type definitions from the InteropFixtures assembly that have
    /// at least one type parameter (constrained or unconstrained).
    /// </summary>
    private static readonly Type[] GenericFixtureTypes = BuildGenericFixtureTypes();

    public GenericConstraintTranslationPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        _filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, _filter);
        _stdlib = FallbackStdlibTypeProvider.Instance;
    }

    private static Type[] BuildGenericFixtureTypes()
    {
        var assembly = typeof(InteropFixtures.SimpleClass).Assembly;
        var filter = new VisibilityFilter();

        return assembly.GetExportedTypes()
            .Where(t => !filter.IsTypeExcluded(t))
            .Where(t => t.IsGenericTypeDefinition)
            .ToArray();
    }

    /// <summary>
    /// Generates a random (type, type parameter index) pair from the generic fixture types.
    /// </summary>
    private static Gen<(Type Type, int ParamIndex)> GenTypeParameterSelection()
    {
        // Build all (type, paramIndex) pairs
        var pairs = GenericFixtureTypes
            .SelectMany(t => t.GetGenericArguments()
                .Select((_, idx) => (Type: t, ParamIndex: idx)))
            .ToArray();

        return Gen.Choose(0, pairs.Length - 1)
            .Select(idx => pairs[idx]);
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for (var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ── Property 10: Generic Constraint Translation ──────────────────────
    // For any CLR type parameter with constraints, verify translation rules:
    // class → HasClassConstraint, struct → HasStructConstraint,
    // new() → constructor constraint, base/interface → projected Mist type.

    [Fact]
    public void P10_ClassConstraint_TranslatesToHasClassConstraint()
    {
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var attributes = clrParam.GenericParameterAttributes;
            var hasClrClassConstraint =
                (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            if (hasClrClassConstraint)
            {
                Assert.NotNull(projectedParam.Constraints);
                Assert.True(
                    projectedParam.Constraints!.HasClassConstraint,
                    $"CLR type '{clrType.Name}' param '{clrParam.Name}' has 'class' constraint " +
                    $"but projected type parameter does not have HasClassConstraint=true");
            }
            else
            {
                // If no class constraint on CLR side, projected should not have it either
                if (projectedParam.Constraints is not null)
                {
                    Assert.False(
                        projectedParam.Constraints.HasClassConstraint,
                        $"CLR type '{clrType.Name}' param '{clrParam.Name}' does NOT have 'class' constraint " +
                        $"but projected type parameter has HasClassConstraint=true");
                }
            }
        });
    }

    [Fact]
    public void P10_StructConstraint_TranslatesToHasStructConstraint()
    {
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var attributes = clrParam.GenericParameterAttributes;
            var hasClrStructConstraint =
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            if (hasClrStructConstraint)
            {
                Assert.NotNull(projectedParam.Constraints);
                Assert.True(
                    projectedParam.Constraints!.HasStructConstraint,
                    $"CLR type '{clrType.Name}' param '{clrParam.Name}' has 'struct' constraint " +
                    $"but projected type parameter does not have HasStructConstraint=true");
            }
            else
            {
                if (projectedParam.Constraints is not null)
                {
                    Assert.False(
                        projectedParam.Constraints.HasStructConstraint,
                        $"CLR type '{clrType.Name}' param '{clrParam.Name}' does NOT have 'struct' constraint " +
                        $"but projected type parameter has HasStructConstraint=true");
                }
            }
        });
    }

    [Fact]
    public void P10_NewConstraint_TranslatesToConstructorConstraint()
    {
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var attributes = clrParam.GenericParameterAttributes;
            var hasClrNewConstraint =
                (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;
            var hasClrStructConstraint =
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            // new() constraint is only projected when struct constraint is NOT present
            // (struct implicitly has new(), so it's redundant)
            if (hasClrNewConstraint && !hasClrStructConstraint)
            {
                Assert.NotNull(projectedParam.Constraints);
                Assert.True(
                    projectedParam.Constraints!.ConstructorConstraints.Count > 0,
                    $"CLR type '{clrType.Name}' param '{clrParam.Name}' has 'new()' constraint " +
                    $"but projected type parameter has no constructor constraints");
                // The new() constraint should be an empty parameter list
                Assert.Empty(projectedParam.Constraints.ConstructorConstraints[0]);
            }
            else if (hasClrStructConstraint)
            {
                // struct constraint: new() is implicit, should NOT be projected separately
                if (projectedParam.Constraints is not null)
                {
                    Assert.True(
                        projectedParam.Constraints.ConstructorConstraints.Count == 0,
                        $"CLR type '{clrType.Name}' param '{clrParam.Name}' has 'struct' constraint " +
                        $"(implicit new()) but projected type parameter has explicit constructor constraints");
                }
            }
        });
    }

    [Fact]
    public void P10_AbstractConstraint_TranslatesToProjectedMistType()
    {
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var clrConstraints = clrParam.GetGenericParameterConstraints();

            // Get interface constraints from CLR (excluding ValueType which is implicit for struct)
            var clrInterfaceConstraints = clrConstraints
                .Where(c => c.IsInterface)
                .ToArray();

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            if (clrInterfaceConstraints.Length > 0)
            {
                Assert.NotNull(projectedParam.Constraints);
                Assert.Equal(
                    clrInterfaceConstraints.Length,
                    projectedParam.Constraints!.AbstractConstraints.Count);

                // Each CLR interface constraint should have a corresponding projected Mist type
                for (var i = 0; i < clrInterfaceConstraints.Length; i++)
                {
                    var projectedConstraint = projectedParam.Constraints.AbstractConstraints[i];
                    Assert.NotNull(projectedConstraint);
                    // The projected constraint should be an interface type or constructed type
                    Assert.True(
                        projectedConstraint is AbstractType or ConstructedType,
                        $"CLR type '{clrType.Name}' param '{clrParam.Name}' has interface constraint " +
                        $"'{clrInterfaceConstraints[i].Name}' but projected constraint is " +
                        $"{projectedConstraint.GetType().Name} instead of AbstractType/ConstructedType");
                }
            }
            else
            {
                // No interface constraints on CLR side
                if (projectedParam.Constraints is not null)
                {
                    Assert.Empty(projectedParam.Constraints.AbstractConstraints);
                }
            }
        });
    }

    [Fact]
    public void P10_BaseClassConstraint_TranslatesToProjectedMistType()
    {
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var clrConstraints = clrParam.GetGenericParameterConstraints();

            // Get base class constraints (non-interface, non-ValueType)
            var clrBaseClassConstraints = clrConstraints
                .Where(c => !c.IsInterface && c != typeof(ValueType))
                .ToArray();

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            if (clrBaseClassConstraints.Length > 0)
            {
                Assert.NotNull(projectedParam.Constraints);
                Assert.NotNull(projectedParam.Constraints!.BaseClassConstraint);
                // The projected base class constraint should be a class type
                Assert.True(
                    projectedParam.Constraints.BaseClassConstraint is ClassType or ConstructedType,
                    $"CLR type '{clrType.Name}' param '{clrParam.Name}' has base class constraint " +
                    $"'{clrBaseClassConstraints[0].Name}' but projected constraint is " +
                    $"{projectedParam.Constraints.BaseClassConstraint?.GetType().Name}");
            }
            else
            {
                if (projectedParam.Constraints is not null)
                {
                    Assert.Null(projectedParam.Constraints.BaseClassConstraint);
                }
            }
        });
    }

    [Fact]
    public void P10_UnconstrainedTypeParameter_HasNullConstraints()
    {
        // Verify that type parameters without any constraints project to null Constraints
        RunProperty(GenTypeParameterSelection(), selection =>
        {
            var (clrType, paramIndex) = selection;
            var clrParam = clrType.GetGenericArguments()[paramIndex];
            var attributes = clrParam.GenericParameterAttributes;
            var clrConstraints = clrParam.GetGenericParameterConstraints();

            var hasAnyConstraint =
                (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 ||
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 ||
                (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 ||
                clrConstraints.Any(c => c != typeof(ValueType));

            var (projectedType, _) = _projector.ProjectType(clrType, _stdlib);
            var typeParams = GetTypeParameters(projectedType);
            var projectedParam = Assert.IsType<TypeParameterType>(typeParams[paramIndex]);

            if (!hasAnyConstraint)
            {
                Assert.Null(
                    projectedParam.Constraints);
            }
            else
            {
                Assert.NotNull(projectedParam.Constraints);
            }
        });
    }

    /// <summary>
    /// Extracts type parameters from a projected Mist type regardless of its kind.
    /// </summary>
    private static IReadOnlyList<IType> GetTypeParameters(IType projectedType)
    {
        return projectedType switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            AbstractType it => it.TypeParameters,
            _ => throw new InvalidOperationException(
                $"Unexpected projected type kind: {projectedType.GetType().Name}")
        };
    }
}
