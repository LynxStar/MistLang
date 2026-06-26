// Feature: assembly-type-provider, Property 13: IEnumerable Interface Mapping
//
// **Validates: Requirements 18.1, 18.2, 18.3**
//
// For any CLR type implementing System.Collections.Generic.IEnumerable<T>,
// the Assembly_Type_Provider SHALL project it as implementing
// Standard.IEnumerable<TElement> where TElement is the projected Mist type for T.
// The Enumerator associated type slot SHALL be synthesized from the
// GetEnumerator() return type.

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for IEnumerable interface mapping (Property 13).
/// Verifies that CLR types implementing <c>IEnumerable&lt;T&gt;</c> are projected
/// with <c>Standard.IEnumerable&lt;TElement&gt;</c> in their interface list and
/// have an <c>Enumerator</c> associated type slot synthesized.
///
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class IEnumerableMappingPropertyTests
{
    private readonly TypeProjector projector;
    private readonly VisibilityFilter filter;
    private readonly IStdlibTypeProvider stdlib;

    /// <summary>
    /// All CLR types that implement <c>IEnumerable&lt;T&gt;</c> and are suitable
    /// for testing. Includes the fixture assembly's CustomCollection and well-known
    /// .NET collection types.
    /// </summary>
    private static readonly Type[] EnumerableTypes = BuildEnumerableTypes();

    public IEnumerableMappingPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
        stdlib = new EnumerableTestStdlibTypeProvider();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test Stdlib Provider
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A custom <see cref="IStdlibTypeProvider"/> that extends the fallback provider
    /// with <c>IEnumerator</c> and <c>IEnumerable</c> types needed for enumerator
    /// slot synthesis.
    /// </summary>
    private sealed class EnumerableTestStdlibTypeProvider : IStdlibTypeProvider
    {
        private static readonly AbstractType IEnumeratorType =
            new("IEnumerator", new Scope(null), "Standard");

        private static readonly AbstractType IEnumerableType =
            new("IEnumerable", new Scope(null), "Standard");

        private static readonly IStdlibTypeProvider Fallback = FallbackStdlibTypeProvider.Instance;

        public IType LookupStandardType(string simpleName)
        {
            return simpleName switch
            {
                "IEnumerator" => IEnumeratorType,
                "IEnumerable" => IEnumerableType,
                _ => Fallback.LookupStandardType(simpleName),
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type Pool
    // ═══════════════════════════════════════════════════════════════════════

    private static Type[] BuildEnumerableTypes()
    {
        var filter = new VisibilityFilter();
        var candidates = new List<Type>
        {
            // Fixture assembly type
            typeof(CustomCollection),
            // Well-known .NET collection types implementing IEnumerable<T>
            typeof(List<int>),
            typeof(List<string>),
            typeof(HashSet<int>),
            typeof(LinkedList<double>),
            typeof(Queue<int>),
            typeof(Stack<string>),
            typeof(SortedSet<int>),
        };

        return candidates
            .Where(t => !filter.IsTypeExcluded(t))
            .Where(HasGenericIEnumerableImplementation)
            .ToArray();
    }

    private static bool HasGenericIEnumerableImplementation(Type type)
    {
        return type.GetInterfaces().Any(iface =>
            iface.IsConstructedGenericType &&
            iface.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IEnumerable`1");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random index into the EnumerableTypes array, selecting a random
    /// CLR type that implements <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    private static Gen<Type> GenEnumerableType()
    {
        return Gen.Choose(0, EnumerableTypes.Length - 1)
            .Select(idx => EnumerableTypes[idx]);
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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13a: IEnumerable<T> projects as Standard.IEnumerable<TElement>
    //
    // For any CLR type implementing IEnumerable<T>, the projected Mist type's
    // Interfaces list SHALL contain a ConstructedType whose GenericDefinition
    // is Standard.IEnumerable with the correct element type argument.
    //
    // **Validates: Requirements 18.1, 18.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P13a_IEnumerableType_ProjectsStandardIEnumerableInterface()
    {
        RunProperty(GenEnumerableType(), clrType =>
        {
            var (projectedType, _) = projector.ProjectType(clrType, stdlib);

            // Get the interfaces list from the projected type
            var interfaces = projectedType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                AbstractType it => it.Abstracts,
                _ => Array.Empty<IType>(),
            };

            // Find the IEnumerable<T> implementation on the CLR type
            var enumerableImpls = clrType.GetInterfaces()
                .Where(iface => iface.IsConstructedGenericType &&
                    iface.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IEnumerable`1")
                .ToList();

            Assert.True(enumerableImpls.Count > 0,
                $"CLR type '{clrType.FullName}' should implement IEnumerable<T>");

            // Verify that the projected interfaces contain Standard.IEnumerable<TElement>
            // The intrinsic map should substitute IEnumerable → Standard.IEnumerable
            var hasStandardIEnumerable = interfaces.Any(iface =>
            {
                if(iface is ConstructedType ct)
                {
                    // The generic definition should be Standard.IEnumerable
                    var def = ct.GenericDefinition;
                    return def.DisplayName == "IEnumerable" &&
                           def is AbstractType { ContainingNamespace: "Standard" };
                }

                return false;
            });

            Assert.True(hasStandardIEnumerable,
                $"CLR type '{clrType.FullName}' should project with Standard.IEnumerable<TElement> " +
                $"in its interfaces list. Actual interfaces: [{string.Join(", ", interfaces.Select(i => i.DisplayName))}]");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13b: Non-generic IEnumerable is NOT projected
    //
    // For any CLR type implementing IEnumerable<T>, the non-generic
    // System.Collections.IEnumerable interface SHALL NOT appear in the
    // projected type's interface list.
    //
    // **Validates: Requirement 18.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P13b_NonGenericIEnumerable_IsNotProjected()
    {
        RunProperty(GenEnumerableType(), clrType =>
        {
            var (projectedType, _) = projector.ProjectType(clrType, stdlib);

            var interfaces = projectedType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                AbstractType it => it.Abstracts,
                _ => Array.Empty<IType>(),
            };

            // The non-generic IEnumerable (System.Collections.IEnumerable) should NOT be present
            var hasNonGenericIEnumerable = interfaces.Any(iface =>
            {
                if(iface is AbstractType { DisplayName: "IEnumerable", ContainingNamespace: "System.Collections" })
                {
                    return true;
                }

                return false;
            });

            Assert.False(hasNonGenericIEnumerable,
                $"CLR type '{clrType.FullName}' should NOT project the non-generic " +
                $"System.Collections.IEnumerable interface. " +
                $"Actual interfaces: [{string.Join(", ", interfaces.Select(i => i.DisplayName))}]");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13c: Enumerator associated type slot is synthesized
    //
    // For any CLR type implementing IEnumerable<T>, the projected type's
    // member scope SHALL contain an "Enumerator" symbol of kind TypeAlias
    // with an AssociatedTypeSlotType.
    //
    // **Validates: Requirements 18.2, 18.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P13c_EnumeratorSlot_IsSynthesized()
    {
        RunProperty(GenEnumerableType(), clrType =>
        {
            var (projectedType, _) = projector.ProjectType(clrType, stdlib);

            // Get the member scope
            var memberScope = projectedType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                AbstractType it => it.MemberScope,
                _ => null,
            };

            Assert.NotNull(memberScope);

            // Look for the Enumerator slot symbol
            var enumeratorSymbol = memberScope!.Members
                .FirstOrDefault(m => m.Name == "Enumerator" && m.Kind == SymbolKind.TypeAlias);

            Assert.True(enumeratorSymbol is not null,
                $"CLR type '{clrType.FullName}' should have an 'Enumerator' associated type slot " +
                $"synthesized in its member scope. Members: [{string.Join(", ", memberScope.Members.Select(m => $"{m.Name}:{m.Kind}"))}]");

            // Verify the slot type is AssociatedTypeSlotType
            _ = Assert.IsType<AssociatedTypeSlotType>(enumeratorSymbol.DeclaredType);

            // Verify the slot has SymbolOrigin.Backend
            Assert.Equal(SymbolOrigin.Backend, enumeratorSymbol.Origin);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13d: Enumerator slot has correct IEnumerator constraint
    //
    // The synthesized Enumerator slot SHALL have a constraint requiring
    // IEnumerator<TElement> implementation.
    //
    // **Validates: Requirement 18.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P13d_EnumeratorSlot_HasIEnumeratorConstraint()
    {
        RunProperty(GenEnumerableType(), clrType =>
        {
            var (projectedType, _) = projector.ProjectType(clrType, stdlib);

            var memberScope = projectedType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                AbstractType it => it.MemberScope,
                _ => null,
            };

            Assert.NotNull(memberScope);

            var enumeratorSymbol = memberScope!.Members
                .FirstOrDefault(m => m.Name == "Enumerator" && m.Kind == SymbolKind.TypeAlias);

            Assert.NotNull(enumeratorSymbol);

            var slotType = Assert.IsType<AssociatedTypeSlotType>(enumeratorSymbol!.DeclaredType);

            // The slot's constraints should include IEnumerator<TElement>
            Assert.NotNull(slotType.Constraints);
            Assert.NotEmpty(slotType.Constraints.AbstractConstraints);

            // Verify the constraint is a ConstructedType with Standard.IEnumerator as definition
            var iEnumeratorConstraint = slotType.Constraints.AbstractConstraints
                .OfType<ConstructedType>()
                .FirstOrDefault(ct =>
                    ct.GenericDefinition.DisplayName == "IEnumerator" &&
                    ct.GenericDefinition is AbstractType { ContainingNamespace: "Standard" });

            Assert.True(iEnumeratorConstraint is not null,
                $"Enumerator slot for '{clrType.FullName}' should have an IEnumerator<TElement> " +
                $"constraint. Actual constraints: [{string.Join(", ", slotType.Constraints.AbstractConstraints.Select(c => c.DisplayName))}]");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13e: Concrete enumerator type is stored in slot
    //
    // For CLR types with a concrete GetEnumerator() return type (not
    // IEnumerator<T>), the Enumerator slot's BaseClassConstraint SHALL
    // store the projected concrete enumerator type.
    //
    // **Validates: Requirement 18.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P13e_ConcreteEnumerator_StoredInSlotConstraint()
    {
        // CustomCollection has a concrete Enumerator struct returned by GetEnumerator()
        var clrType = typeof(CustomCollection);
        var (projectedType, _) = projector.ProjectType(clrType, stdlib);

        var memberScope = projectedType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        Assert.NotNull(memberScope);

        var enumeratorSymbol = memberScope!.Members
            .FirstOrDefault(m => m.Name == "Enumerator" && m.Kind == SymbolKind.TypeAlias);

        Assert.NotNull(enumeratorSymbol);

        var slotType = Assert.IsType<AssociatedTypeSlotType>(enumeratorSymbol!.DeclaredType);

        // CustomCollection.GetEnumerator() returns CustomCollection.Enumerator (a concrete struct)
        // The BaseClassConstraint should store the projected concrete type
        Assert.NotNull(slotType.Constraints.BaseClassConstraint);

        // The concrete enumerator should be a StructType (since CustomCollection.Enumerator is a struct)
        var concreteEnumerator = slotType.Constraints.BaseClassConstraint;
        _ = Assert.IsType<StructType>(concreteEnumerator);
    }
}