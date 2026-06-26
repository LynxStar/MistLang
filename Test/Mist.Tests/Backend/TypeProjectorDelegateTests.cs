using System;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="TypeProjector"/> delegate projection (Task 2.4).
///
/// Validates the projection pipeline correctly classifies delegate types:
/// <list type="bullet">
///   <item><c>System.Func&lt;...&gt;</c> / <c>System.Action&lt;...&gt;</c> → <see cref="ConstructedType"/> (NOT DelegateType)</item>
///   <item><c>System.Delegate</c> / <c>System.MulticastDelegate</c> → <see cref="BaseDelegateType.Instance"/></item>
///   <item>Named delegates (e.g., <c>Predicate&lt;int&gt;</c>) → <see cref="DelegateType"/> with correct CLR identity and FuncSignature</item>
///   <item>Open generic delegates (e.g., <c>Predicate&lt;T&gt;</c>) → open <see cref="DelegateType"/> with TypeParameters</item>
/// </list>
///
/// Requirements: 1.1, 1.2, 1.5, 3.1.
///
/// All tests run fully in-process via <see cref="TypeProjector.ProjectMemberType"/>;
/// no <c>dotnet</c> process is spawned.
/// </summary>
public sealed class TypeProjectorDelegateTests
{
    private readonly TypeProjector projector;
    private readonly DelegateTestStdlibTypeProvider stdlib;

    public TypeProjectorDelegateTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
        stdlib = new DelegateTestStdlibTypeProvider();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test stdlib provider — supplies the Standard.Func family (arity-aware)
    // plus Standard.Void / Standard.Int32 / Standard.String / Standard.Boolean,
    // mirroring what a loaded stdlib would expose.
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class DelegateTestStdlibTypeProvider : IStdlibTypeProvider
    {
        private readonly Dictionary<int, ClassType> funcByArity = new();

        private static readonly StructType VoidType =
            new("Void", new Scope(null), "Standard", isBackendBacked: true);
        private static readonly StructType Int32Type =
            new("Int32", new Scope(null), "Standard", isBackendBacked: true);
        private static readonly StructType BooleanType =
            new("Boolean", new Scope(null), "Standard", isBackendBacked: true);
        private static readonly ClassType StringType =
            new("String", new Scope(null), "Standard", isBackendBacked: true);

        public ClassType FuncOfArity(int arity)
        {
            if(!funcByArity.TryGetValue(arity, out var func))
            {
                func = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
                var tps = new IType[arity];
                for(var i = 0; i < arity; i++)
                {
                    tps[i] = new TypeParameterType($"T{i}", i);
                }

                func.TypeParameters = tps;
                funcByArity[arity] = func;
            }
            return func;
        }

        public IType LookupStandardType(string simpleName) => LookupStandardType(simpleName, -1);

        public IType LookupStandardType(string simpleName, int typeArgumentCount)
        {
            return simpleName switch
            {
                "Void" => VoidType,
                "Int32" => Int32Type,
                "Boolean" => BooleanType,
                "String" => StringType,
                "Func" when typeArgumentCount >= 1 => FuncOfArity(typeArgumentCount),
                _ => throw new InvalidOperationException($"No test stdlib type for '{simpleName}'."),
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Func projects to ConstructedType, NOT DelegateType (Req 1.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_SystemFuncIntString_ProjectsToConstructedType_NotDelegateType()
    {
        var projected = projector.ProjectMemberType(typeof(Func<int, string>), stdlib);

        _ = Assert.IsType<ConstructedType>(projected);
        Assert.IsNotType<DelegateType>(projected);

        var ct = (ConstructedType)projected;
        var def = Assert.IsType<ClassType>(ct.GenericDefinition);
        Assert.Equal("Func", def.DisplayName);
        Assert.Equal("Standard", def.ContainingNamespace);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Action / System.Action<int> project to ConstructedType (Req 1.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_NonGenericSystemAction_ProjectsToConstructedType_NotDelegateType()
    {
        var projected = projector.ProjectMemberType(typeof(Action), stdlib);

        _ = Assert.IsType<ConstructedType>(projected);
        Assert.IsNotType<DelegateType>(projected);

        var ct = (ConstructedType)projected;
        var def = Assert.IsType<ClassType>(ct.GenericDefinition);
        Assert.Equal("Func", def.DisplayName);
        Assert.Equal("Standard", def.ContainingNamespace);
    }

    [Fact]
    public void ProjectMemberType_SystemActionOfInt_ProjectsToConstructedType_NotDelegateType()
    {
        var projected = projector.ProjectMemberType(typeof(Action<int>), stdlib);

        _ = Assert.IsType<ConstructedType>(projected);
        Assert.IsNotType<DelegateType>(projected);

        var ct = (ConstructedType)projected;
        var def = Assert.IsType<ClassType>(ct.GenericDefinition);
        Assert.Equal("Func", def.DisplayName);
        Assert.Equal("Standard", def.ContainingNamespace);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Delegate and System.MulticastDelegate → BaseDelegateType (Req 3.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_SystemDelegate_ProjectsToBaseDelegateTypeInstance()
    {
        var projected = projector.ProjectMemberType(typeof(Delegate), stdlib);

        Assert.Same(BaseDelegateType.Instance, projected);
    }

    [Fact]
    public void ProjectMemberType_SystemMulticastDelegate_ProjectsToBaseDelegateTypeInstance()
    {
        var projected = projector.ProjectMemberType(typeof(MulticastDelegate), stdlib);

        Assert.Same(BaseDelegateType.Instance, projected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Predicate<int> → DelegateType with correct identity (Req 1.1, 1.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_PredicateOfInt_ProjectsToDelegateType()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);

        _ = Assert.IsType<DelegateType>(projected);
    }

    [Fact]
    public void ProjectMemberType_PredicateOfInt_HasCorrectClrFullName()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        // System.Predicate`1[[System.Int32]] — the FullName for constructed generics
        // includes type argument details, but our ClrFullName preserves the CLR type's FullName.
        Assert.Equal(typeof(Predicate<int>).FullName, dt.ClrFullName);
    }

    [Fact]
    public void ProjectMemberType_PredicateOfInt_HasCorrectNamespace()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        Assert.Equal("System", dt.ClrNamespace);
    }

    [Fact]
    public void ProjectMemberType_PredicateOfInt_HasCorrectSimpleName()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        Assert.Equal("Predicate", dt.ClrSimpleName);
    }

    [Fact]
    public void ProjectMemberType_PredicateOfInt_HasCorrectFuncSignature()
    {
        // Predicate<int> has signature: bool Invoke(int obj)
        // → Standard.Func<Int32, Boolean> (arity 2)
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        var funcSig = dt.FuncSignature;
        Assert.NotNull(funcSig);

        var funcDef = Assert.IsType<ClassType>(funcSig.GenericDefinition);
        Assert.Equal("Func", funcDef.DisplayName);
        Assert.Equal("Standard", funcDef.ContainingNamespace);

        // Arity = 2 (one parameter Int32 + return Boolean)
        Assert.Equal(2, funcSig.TypeArguments.Count);
        Assert.Equal("Int32", funcSig.TypeArguments[0].DisplayName);
        Assert.Equal("Boolean", funcSig.TypeArguments[1].DisplayName);
    }

    [Fact]
    public void ProjectMemberType_PredicateOfInt_HasConcreteTypeArguments()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        // Predicate<int> is a constructed generic delegate — TypeArguments should be [Int32]
        _ = Assert.Single(dt.TypeArguments);
        Assert.Equal("Int32", dt.TypeArguments[0].DisplayName);

        // TypeParameters should be empty for a constructed generic
        Assert.Empty(dt.TypeParameters);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic open delegate Predicate<T> → open DelegateType with TypeParameters
    // Generic constructed Predicate<int> → constructed DelegateType with TypeArguments
    // (Req 1.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_OpenPredicateT_ProjectsToOpenDelegateType()
    {
        // Predicate<T> (generic type definition) should produce a DelegateType with
        // TypeParameters (open type params) and no TypeArguments.
        var projected = projector.ProjectMemberType(typeof(Predicate<>), stdlib);

        var dt = Assert.IsType<DelegateType>(projected);

        // Open generic → has type parameters
        _ = Assert.Single(dt.TypeParameters);
        var tp = Assert.IsType<TypeParameterType>(dt.TypeParameters[0]);
        Assert.Equal("T", tp.Name);

        // No concrete type arguments
        Assert.Empty(dt.TypeArguments);
    }

    [Fact]
    public void ProjectMemberType_OpenPredicateT_HasCorrectClrSimpleName()
    {
        var projected = projector.ProjectMemberType(typeof(Predicate<>), stdlib);
        var dt = Assert.IsType<DelegateType>(projected);

        // Simple name should have arity suffix stripped
        Assert.Equal("Predicate", dt.ClrSimpleName);
    }

    [Fact]
    public void ProjectMemberType_ConstructedPredicateInt_DiffersFromOpenPredicateT()
    {
        var openProjected = projector.ProjectMemberType(typeof(Predicate<>), stdlib);
        var constructedProjected = projector.ProjectMemberType(typeof(Predicate<int>), stdlib);

        var openDt = Assert.IsType<DelegateType>(openProjected);
        var constructedDt = Assert.IsType<DelegateType>(constructedProjected);

        // Open has TypeParameters, no TypeArguments
        Assert.NotEmpty(openDt.TypeParameters);
        Assert.Empty(openDt.TypeArguments);

        // Constructed has TypeArguments, no TypeParameters
        Assert.Empty(constructedDt.TypeParameters);
        Assert.NotEmpty(constructedDt.TypeArguments);
    }

    [Fact]
    public void ProjectMemberType_ComparisonOfInt_ProjectsToDelegateType()
    {
        // Additional named delegate: Comparison<int> has signature int Invoke(int x, int y)
        // → Standard.Func<Int32, Int32, Int32> (arity 3)
        var projected = projector.ProjectMemberType(typeof(Comparison<int>), stdlib);

        var dt = Assert.IsType<DelegateType>(projected);
        Assert.Equal("Comparison", dt.ClrSimpleName);
        Assert.Equal("System", dt.ClrNamespace);

        var funcSig = dt.FuncSignature;
        Assert.Equal(3, funcSig.TypeArguments.Count);
        Assert.Equal("Int32", funcSig.TypeArguments[0].DisplayName);
        Assert.Equal("Int32", funcSig.TypeArguments[1].DisplayName);
        Assert.Equal("Int32", funcSig.TypeArguments[2].DisplayName);
    }
}