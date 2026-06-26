// Feature: clr-ref-interop, Property 3: Ref and in parameters projected as RefKind.Ref
// Feature: clr-ref-interop, Property 4: Intrinsic type substitution through ref unwrapping
// Feature: clr-ref-interop, Property 7: Ref returns projected as ReturnRefKind.Ref
//
// **Validates: Requirements 2.1, 2.2, 3.2, 3.3, 5.1, 5.2**

using System.Collections.Immutable;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for the TypeProjector's handling of CLR ref/in parameters
/// and mutable ref returns.
///
/// Uses the real fixture assembly types as input — no mocked metadata.
/// Tests operate entirely in-process using TypeProjector directly (no process spawning).
/// </summary>
public sealed class ClrRefProjectionPropertyTests
{
    private readonly TypeProjector _projector;
    private readonly IStdlibTypeProvider _stdlib;

    // ─── Pre-computed member sets from fixture types ─────────────────────

    /// <summary>
    /// All (MethodInfo, ParameterInfo) pairs from RefParamFixture and InParamFixture
    /// where the parameter is ref or in (IsByRef &amp;&amp; !IsOut).
    /// </summary>
    private static readonly (MethodInfo Method, ParameterInfo Param)[] RefAndInParams = BuildRefAndInParams();

    /// <summary>
    /// Subset of <see cref="RefAndInParams"/> where the element type is a CLR intrinsic
    /// (System.Int32 or System.String). Used for Property 4.
    /// </summary>
    private static readonly (MethodInfo Method, ParameterInfo Param)[] IntrinsicRefParams = BuildIntrinsicRefParams();

    /// <summary>
    /// All public methods from MutableRefReturnFixture that have mutable ref returns.
    /// </summary>
    private static readonly MethodInfo[] MutableRefReturnMethods = BuildMutableRefReturnMethods();

    public ClrRefProjectionPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, filter);
        _stdlib = FallbackStdlibTypeProvider.Instance;
    }

    // ─── Builder methods ─────────────────────────────────────────────────

    private static (MethodInfo, ParameterInfo)[] BuildRefAndInParams()
    {
        var result = new List<(MethodInfo, ParameterInfo)>();

        foreach(var type in new[] { typeof(RefParamFixture), typeof(InParamFixture) })
        {
            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if(method.IsSpecialName)
                {
                    continue;
                }

                foreach(var param in method.GetParameters())
                {
                    if(param.ParameterType.IsByRef && !param.IsOut)
                    {
                        result.Add((method, param));
                    }
                }
            }
        }

        return result.ToArray();
    }

    private static (MethodInfo, ParameterInfo)[] BuildIntrinsicRefParams()
    {
        var intrinsicFullNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Int32",
            "System.String",
        };

        return RefAndInParams
            .Where(pair =>
            {
                var elementType = pair.Param.ParameterType.GetElementType()!;
                return elementType.FullName is not null && intrinsicFullNames.Contains(elementType.FullName);
            })
            .ToArray();
    }

    private static MethodInfo[] BuildMutableRefReturnMethods()
    {
        var result = new List<MethodInfo>();

        foreach(var method in typeof(MutableRefReturnFixture).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if(method.IsSpecialName)
            {
                continue;
            }

            if(method.ReturnType.IsByRef && !IsRefReadonlyReturn(method))
            {
                result.Add(method);
            }
        }

        return result.ToArray();
    }

    private static bool IsRefReadonlyReturn(MethodInfo method)
    {
        return method.ReturnParameter
            .GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName ==
                "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    // ─── Helper ──────────────────────────────────────────────────────────

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
    /// Projects a fixture type and returns the projected method symbols.
    /// </summary>
    private ImmutableArray<ISymbol> ProjectFixtureMembers(Type fixtureType)
    {
        var (projectedType, _) = _projector.ProjectType(fixtureType, _stdlib);
        return _projector.ProjectMembers(fixtureType, projectedType, _stdlib);
    }

    /// <summary>
    /// Finds the projected method symbol matching the given CLR method by name and parameter count.
    /// </summary>
    private static Symbol? FindProjectedMethod(ImmutableArray<ISymbol> members, MethodInfo clrMethod)
    {
        return members
            .Where(s => s.Kind == SymbolKind.Method && s.Name == clrMethod.Name)
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Parameters.Count == clrMethod.GetParameters().Length);
    }

    // ─── Property 3: Ref and in parameters projected as RefKind.Ref ──────────────

    /// <summary>
    /// Property 3: For any CLR method parameter that is ref or in, the TypeProjector
    /// SHALL produce a parameter symbol with RefKind == RefKind.Ref, and the parameter's
    /// declared type SHALL be the element type (not the by-ref wrapper type).
    ///
    /// **Validates: Requirements 2.1, 3.2**
    /// </summary>
    [Fact]
    public void Property3_RefAndInParams_ProjectedAsRefKindRef()
    {
        Assert.NotEmpty(RefAndInParams);

        RunProperty(Gen.Choose(0, RefAndInParams.Length - 1), index =>
        {
            var (method, param) = RefAndInParams[index];
            var declaringType = method.DeclaringType!;

            var members = ProjectFixtureMembers(declaringType);
            var projectedMethod = FindProjectedMethod(members, method);

            Assert.NotNull(projectedMethod);

            // Find the projected parameter at the same position
            var projectedParam = projectedMethod!.Parameters[param.Position] as Symbol;
            Assert.NotNull(projectedParam);

            // The parameter should have RefKind.Ref
            Assert.Equal(RefKind.Ref, projectedParam!.RefKind);

            // The parameter's declared type should be the element type, not the by-ref wrapper.
            // By-ref types have names ending with "&" — the projected type should NOT.
            var elementType = param.ParameterType.GetElementType()!;
            Assert.NotNull(projectedParam.DeclaredType);
            Assert.DoesNotContain("&", projectedParam.DeclaredType!.DisplayName);
        });
    }

    // ─── Property 4: Intrinsic type substitution through ref unwrapping ──────────

    /// <summary>
    /// Property 4: For any CLR ref or in parameter whose element type is a CLR intrinsic
    /// (e.g., System.Int32, System.String), the projected Mist parameter type SHALL be
    /// the corresponding Mist Standard Library type (e.g., Standard.Int32 → "int",
    /// Standard.String → "string"), not the raw CLR type name.
    ///
    /// The emitter parameter symbols use nullable projection, so reference types like
    /// string appear as "string | void". We verify that the stdlib name is present
    /// and the raw CLR name (e.g., "Int32", "String") is absent.
    ///
    /// **Validates: Requirements 2.2, 3.3, 5.2**
    /// </summary>
    [Fact]
    public void Property4_IntrinsicRefParams_SubstitutedToStdlibTypes()
    {
        Assert.NotEmpty(IntrinsicRefParams);

        RunProperty(Gen.Choose(0, IntrinsicRefParams.Length - 1), index =>
        {
            var (method, param) = IntrinsicRefParams[index];
            var declaringType = method.DeclaringType!;

            var members = ProjectFixtureMembers(declaringType);
            var projectedMethod = FindProjectedMethod(members, method);

            Assert.NotNull(projectedMethod);

            // Find the projected parameter at the same position
            var projectedParam = projectedMethod!.Parameters[param.Position] as Symbol;
            Assert.NotNull(projectedParam);

            // Determine the expected and forbidden display names for the intrinsic element type.
            var elementType = param.ParameterType.GetElementType()!;
            var (expectedStdlibName, rawClrName) = elementType.FullName switch
            {
                "System.Int32" => ("int", "Int32"),
                "System.String" => ("string", "String"),
                _ => throw new InvalidOperationException(
                    $"Unexpected intrinsic type: {elementType.FullName}")
            };

            // The projected type should contain the stdlib name (possibly nullable-wrapped
            // for reference types, e.g., "string | void") and must NOT be the raw CLR name.
            Assert.NotNull(projectedParam!.DeclaredType);
            var displayName = projectedParam.DeclaredType!.DisplayName;

            Assert.Contains(expectedStdlibName, displayName);
            Assert.DoesNotContain(rawClrName, displayName);
        });
    }

    // ─── Property 7: Ref returns projected as ReturnRefKind.Ref ──────────────────

    /// <summary>
    /// Property 7: For any CLR method with a mutable ref return type, the TypeProjector
    /// SHALL produce a method symbol with ReturnRefKind == RefKind.Ref, and the method's
    /// declared return type SHALL be the element type (not the by-ref wrapper type).
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Fact]
    public void Property7_MutableRefReturns_ProjectedAsReturnRefKindRef()
    {
        Assert.NotEmpty(MutableRefReturnMethods);

        RunProperty(Gen.Choose(0, MutableRefReturnMethods.Length - 1), index =>
        {
            var method = MutableRefReturnMethods[index];
            var declaringType = method.DeclaringType!;

            var members = ProjectFixtureMembers(declaringType);
            var projectedMethod = FindProjectedMethod(members, method);

            Assert.NotNull(projectedMethod);

            // The method should have ReturnRefKind == RefKind.Ref
            Assert.Equal(RefKind.Ref, projectedMethod!.ReturnRefKind);

            // The return type should be the element type, not the by-ref wrapper.
            var elementType = method.ReturnType.GetElementType()!;
            Assert.NotNull(projectedMethod.DeclaredType);
            Assert.DoesNotContain("&", projectedMethod.DeclaredType!.DisplayName);
        });
    }
}