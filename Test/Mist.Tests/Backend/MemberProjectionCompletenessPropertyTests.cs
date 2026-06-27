using System.Collections.Immutable;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.InteropFixtures;
using PropertyInfo = System.Reflection.PropertyInfo;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for member projection completeness (Property 6).
/// For random public members passing the visibility filter, verifies that the
/// TypeProjector produces corresponding Mist symbols with correct parameter/return types.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9**
/// </summary>
public sealed class MemberProjectionCompletenessPropertyTests
{
    private readonly TypeProjector projector;
    private readonly VisibilityFilter filter;
    private readonly IStdlibTypeProvider stdlib;

    /// <summary>
    /// All fixture types that contain projectable public members.
    /// </summary>
    private static readonly Type[] FixtureTypes =
    [
        typeof(SimpleClass),
        typeof(SimpleStruct),
        typeof(ISimpleInterface),
        typeof(HasIndexer),
        typeof(HasOptionalParams),
        typeof(HasOverloads),
        typeof(GenericClass<>),
        typeof(MultiConstraint<>),
        typeof(HasNullableMembers),
        typeof(HasInParam),
        typeof(CustomCollection),
    ];

    /// <summary>
    /// Pre-computed list of (Type, MemberInfo) pairs for all public members
    /// that pass the visibility filter across all fixture types.
    /// </summary>
    private static readonly (Type DeclaringFixture, MemberInfo Member)[] VisibleMembers = BuildVisibleMembers();

    public MemberProjectionCompletenessPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
        stdlib = FallbackStdlibTypeProvider.Instance;
    }

    private static (Type, MemberInfo)[] BuildVisibleMembers()
    {
        var filter = new VisibilityFilter();
        var result = new List<(Type, MemberInfo)>();

        foreach(var type in FixtureTypes)
        {
            if(filter.IsTypeExcluded(type))
            {
                continue;
            }

            // Constructors
            foreach(var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                if(!filter.IsMemberExcluded(ctor))
                {
                    result.Add((type, ctor));
                }
            }

            // Methods (excluding special names like property accessors and operators)
            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if(method.IsSpecialName)
                {
                    continue;
                }

                if(!filter.IsMemberExcluded(method))
                {
                    result.Add((type, method));
                }
            }

            // Properties (non-indexer)
            foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if(prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if(!filter.IsMemberExcluded(prop))
                {
                    result.Add((type, prop));
                }
            }

            // Fields (skip enum backing fields)
            if(!type.IsEnum)
            {
                foreach(var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if(!filter.IsMemberExcluded(field))
                    {
                        result.Add((type, field));
                    }
                }
            }

            // Indexers
            foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if(prop.GetIndexParameters().Length == 0)
                {
                    continue;
                }

                if(!filter.IsMemberExcluded(prop))
                {
                    result.Add((type, prop));
                }
            }
        }

        return result.ToArray();
    }

    // ─── Helpers ─────────────────────────────────────────────────────

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

    // ─── Generator ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a random index into the VisibleMembers array, selecting a random
    /// public member that passes the visibility filter.
    /// </summary>
    private static Gen<int> GenVisibleMemberIndex()
    {
        return Gen.Choose(0, VisibleMembers.Length - 1);
    }

    // ─── Property Test ───────────────────────────────────────────────

    /// <summary>
    /// Property 6: Member Projection Completeness
    ///
    /// For any public member (method, property, field, constructor, or indexer) on a CLR type
    /// that passes the visibility filter, the TypeProjector SHALL produce a corresponding Mist
    /// symbol with parameter types and return type translated to Mist types.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9**
    /// </summary>
    [Fact]
    public void Property6_VisibleMembers_HaveCorrespondingMistSymbols()
    {
        RunProperty(GenVisibleMemberIndex(), memberIndex =>
        {
            var (declaringType, member) = VisibleMembers[memberIndex];

            // Project the type and its members
            var (projectedType, _) = projector.ProjectType(declaringType, stdlib);
            var projectedMembers = projector.ProjectMembers(declaringType, projectedType, stdlib);

            switch(member)
            {
                case ConstructorInfo:
                    AssertConstructorProjected(projectedMembers, (ConstructorInfo)member);
                    break;

                case MethodInfo methodInfo:
                    AssertMethodProjected(projectedMembers, methodInfo);
                    break;

                case PropertyInfo propInfo when propInfo.GetIndexParameters().Length > 0:
                    AssertIndexerProjected(projectedMembers);
                    break;

                case PropertyInfo propInfo:
                    AssertPropertyProjected(projectedMembers, propInfo);
                    break;

                case FieldInfo fieldInfo:
                    AssertFieldProjected(projectedMembers, fieldInfo);
                    break;
            }
        });
    }

    /// <summary>
    /// Property 6b: Projected members have correct SymbolKind for their CLR member category.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9**
    /// </summary>
    [Fact]
    public void Property6b_ProjectedMembers_HaveCorrectSymbolKind()
    {
        RunProperty(GenVisibleMemberIndex(), memberIndex =>
        {
            var (declaringType, member) = VisibleMembers[memberIndex];

            var (projectedType, _) = projector.ProjectType(declaringType, stdlib);
            var projectedMembers = projector.ProjectMembers(declaringType, projectedType, stdlib);

            var expectedKind = member switch
            {
                ConstructorInfo => SymbolKind.Constructor,
                MethodInfo => SymbolKind.Method,
                PropertyInfo p when p.GetIndexParameters().Length > 0 => SymbolKind.Indexer,
                PropertyInfo => SymbolKind.Property,
                FieldInfo => SymbolKind.Field,
                _ => throw new InvalidOperationException($"Unexpected member type: {member.GetType()}")
            };

            var matchingSymbols = projectedMembers.Where(s => s.Kind == expectedKind).ToList();
            Assert.NotEmpty(matchingSymbols);
        });
    }

    /// <summary>
    /// Property 6c: All projected member symbols have Backend origin.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9**
    /// </summary>
    [Fact]
    public void Property6c_ProjectedMembers_HaveBackendOrigin()
    {
        RunProperty(GenVisibleMemberIndex(), memberIndex =>
        {
            var (declaringType, member) = VisibleMembers[memberIndex];

            var (projectedType, _) = projector.ProjectType(declaringType, stdlib);
            var projectedMembers = projector.ProjectMembers(declaringType, projectedType, stdlib);

            Assert.All(projectedMembers, m => Assert.Equal(SymbolOrigin.Backend, m.Origin));
        });
    }

    /// <summary>
    /// Property 6d: Projected method symbols have non-null DeclaredType (return type).
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Fact]
    public void Property6d_ProjectedMethods_HaveReturnType()
    {
        RunProperty(GenVisibleMemberIndex(), memberIndex =>
        {
            var (declaringType, member) = VisibleMembers[memberIndex];

            if(member is not MethodInfo)
            {
                return; // Only check methods
            }

            var (projectedType, _) = projector.ProjectType(declaringType, stdlib);
            var projectedMembers = projector.ProjectMembers(declaringType, projectedType, stdlib);

            var methodName = ((MethodInfo)member).Name;
            var matchingMethods = projectedMembers
                .Where(s => s.Kind == SymbolKind.Method && s.Name == methodName)
                .ToList();

            Assert.NotEmpty(matchingMethods);
            Assert.All(matchingMethods, m => Assert.NotNull(m.DeclaredType));
        });
    }

    /// <summary>
    /// Property 6e: Projected method symbols have correct parameter count.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.5**
    /// </summary>
    [Fact]
    public void Property6e_ProjectedMethods_HaveCorrectParameterCount()
    {
        RunProperty(GenVisibleMemberIndex(), memberIndex =>
        {
            var (declaringType, member) = VisibleMembers[memberIndex];

            if(member is not MethodInfo methodInfo)
            {
                return; // Only check methods
            }

            var (projectedType, _) = projector.ProjectType(declaringType, stdlib);
            var projectedMembers = projector.ProjectMembers(declaringType, projectedType, stdlib);

            var expectedParamCount = methodInfo.GetParameters().Length;

            // Find the matching method by name and parameter count
            var matchingMethods = projectedMembers
                .Where(s => s.Kind == SymbolKind.Method && s.Name == methodInfo.Name)
                .OfType<Symbol>()
                .Where(s => s.Parameters.Count == expectedParamCount)
                .ToList();

            Assert.NotEmpty(matchingMethods);
        });
    }

    // ─── Assertion Helpers ───────────────────────────────────────────

    private static void AssertConstructorProjected(
        ImmutableArray<ISymbol> projectedMembers,
        ConstructorInfo ctor)
    {
        var constructors = projectedMembers
            .Where(s => s.Kind == SymbolKind.Constructor)
            .OfType<Symbol>()
            .ToList();

        // There should be at least one constructor projected
        Assert.NotEmpty(constructors);

        // Find the constructor with matching parameter count
        var expectedParamCount = ctor.GetParameters().Length;
        var matching = constructors.Where(c => c.Parameters.Count == expectedParamCount).ToList();
        Assert.NotEmpty(matching);

        // Verify all parameters have non-null DeclaredType
        foreach(var ctorSymbol in matching)
        {
            Assert.All(ctorSymbol.Parameters, p => Assert.NotNull(p.DeclaredType));
        }
    }

    private static void AssertMethodProjected(
        ImmutableArray<ISymbol> projectedMembers,
        MethodInfo method)
    {
        var methods = projectedMembers
            .Where(s => s.Kind == SymbolKind.Method && s.Name == method.Name)
            .OfType<Symbol>()
            .ToList();

        // There should be at least one method with this name
        Assert.NotEmpty(methods);

        // Find the method with matching parameter count
        var expectedParamCount = method.GetParameters().Length;
        var matching = methods.Where(m => m.Parameters.Count == expectedParamCount).ToList();
        Assert.NotEmpty(matching);

        // Verify return type is non-null
        foreach(var methodSymbol in matching)
        {
            Assert.NotNull(methodSymbol.DeclaredType);

            // Verify all parameters have non-null DeclaredType
            Assert.All(methodSymbol.Parameters, p => Assert.NotNull(p.DeclaredType));
        }
    }

    private static void AssertPropertyProjected(
        ImmutableArray<ISymbol> projectedMembers,
        PropertyInfo property)
    {
        var properties = projectedMembers
            .Where(s => s.Kind == SymbolKind.Property && s.Name == property.Name)
            .ToList();

        // The property should be projected
        Assert.NotEmpty(properties);

        // Verify the property has a non-null DeclaredType
        foreach(var propSymbol in properties)
        {
            Assert.NotNull(propSymbol.DeclaredType);
        }
    }

    private static void AssertFieldProjected(
        ImmutableArray<ISymbol> projectedMembers,
        FieldInfo field)
    {
        var fields = projectedMembers
            .Where(s => s.Kind == SymbolKind.Field && s.Name == field.Name)
            .ToList();

        // The field should be projected
        Assert.NotEmpty(fields);

        // Verify the field has a non-null DeclaredType
        foreach(var fieldSymbol in fields)
        {
            Assert.NotNull(fieldSymbol.DeclaredType);
        }
    }

    private static void AssertIndexerProjected(
        ImmutableArray<ISymbol> projectedMembers)
    {
        var indexers = projectedMembers
            .Where(s => s.Kind == SymbolKind.Indexer)
            .ToList();

        // There should be at least one indexer projected
        Assert.NotEmpty(indexers);

        // Verify the indexer has a non-null DeclaredType (element type)
        foreach(var indexerSymbol in indexers)
        {
            Assert.NotNull(indexerSymbol.DeclaredType);
        }
    }
}