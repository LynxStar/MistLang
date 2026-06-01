using System;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="TypeProjector"/> delegate projection
/// (first-class-functions Task 15.1).
///
/// Validates that the well-known .NET delegate families from the <c>System</c>
/// namespace project into the Mist <c>Standard.Func</c> type family:
/// <list type="bullet">
///   <item><c>System.Func&lt;T1, ..., TN, TResult&gt;</c> → <c>Standard.Func&lt;T1, ..., TN, TResult&gt;</c></item>
///   <item><c>System.Action&lt;T1, ..., TN&gt;</c> → <c>Standard.Func&lt;T1, ..., TN, Void&gt;</c></item>
///   <item><c>System.Action</c> (non-generic) → <c>Standard.Func&lt;Void&gt;</c></item>
/// </list>
///
/// Requirements: 15.1, 15.2.
///
/// All tests run fully in-process via <see cref="TypeProjector.ProjectMemberType"/>;
/// no <c>dotnet</c> process is spawned.
/// </summary>
public sealed class DelegateProjectionTests
{
    private readonly TypeProjector _projector;
    private readonly FuncTestStdlibTypeProvider _stdlib;

    public DelegateProjectionTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, filter);
        _stdlib = new FuncTestStdlibTypeProvider();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test stdlib provider — supplies the Standard.Func family (arity-aware)
    // plus Standard.Void / Standard.Int32 / Standard.String, mirroring what a
    // loaded stdlib would expose.
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class FuncTestStdlibTypeProvider : IStdlibTypeProvider
    {
        // One cached Func ClassType per arity (1..17), so projected types of the
        // same arity are reference-equal — matching real stdlib behavior, which
        // is what ConstructedType structural equality relies on.
        private readonly System.Collections.Generic.Dictionary<int, ClassType> _funcByArity = new();

        private static readonly StructType VoidType =
            new("Void", new Scope(null), "Standard", isBackendBacked: true);
        private static readonly StructType Int32Type =
            new("Int32", new Scope(null), "Standard", isBackendBacked: true);
        private static readonly ClassType StringType =
            new("String", new Scope(null), "Standard", isBackendBacked: true);

        public ClassType FuncOfArity(int arity)
        {
            if (!_funcByArity.TryGetValue(arity, out var func))
            {
                func = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
                var tps = new IType[arity];
                for (var i = 0; i < arity; i++)
                    tps[i] = new TypeParameterType($"T{i}", i);
                func.TypeParameters = tps;
                _funcByArity[arity] = func;
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
                "String" => StringType,
                "Func" when typeArgumentCount >= 1 => FuncOfArity(typeArgumentCount),
                _ => throw new InvalidOperationException($"No test stdlib type for '{simpleName}'."),
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Func projection (Requirement 15.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_SystemFuncIntString_ProjectsStandardFuncIntString()
    {
        var (def, args) = ProjectConstructedFunc(typeof(Func<int, string>));

        // Func`2 → Standard.Func with 2 type arguments [Int32, String].
        Assert.Equal("Func", def.DisplayName);
        Assert.Equal("Standard", def.ContainingNamespace);
        Assert.Equal(2, args.Length);
        Assert.Equal("Int32", args[0].DisplayName);
        Assert.Equal("String", args[1].DisplayName);
    }

    [Fact]
    public void ProjectMemberType_SystemFuncSingleReturn_ProjectsStandardFuncWithOneArg()
    {
        // System.Func<int> (zero-parameter function returning int) → Standard.Func<Int32>.
        var (def, args) = ProjectConstructedFunc(typeof(Func<int>));

        Assert.Equal("Func", def.DisplayName);
        Assert.Single(args);
        Assert.Equal("Int32", args[0].DisplayName);
    }

    [Fact]
    public void ProjectMemberType_SystemFuncThreeArgs_ProjectsStandardFuncArity3()
    {
        // System.Func<int, string, int> → Standard.Func<Int32, String, Int32>.
        var (def, args) = ProjectConstructedFunc(typeof(Func<int, string, int>));

        Assert.Equal("Func", def.DisplayName);
        Assert.Equal(3, args.Length);
        Assert.Equal("Int32", args[0].DisplayName);
        Assert.Equal("String", args[1].DisplayName);
        Assert.Equal("Int32", args[2].DisplayName);
    }

    [Fact]
    public void ProjectMemberType_SystemFunc_ReturnTypeIsLastArgument_NotVoid()
    {
        // The last type argument of a projected Func is the return type, and for
        // System.Func it must be the genuine return type (not Void).
        var (_, args) = ProjectConstructedFunc(typeof(Func<int, string>));
        Assert.False(args[^1].IsVoid(), "System.Func return type must not be Void.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System.Action<...> projection (Requirement 15.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_SystemActionOfInt_ProjectsStandardFuncIntVoid()
    {
        // System.Action<int> → Standard.Func<Int32, Void>.
        var (def, args) = ProjectConstructedFunc(typeof(Action<int>));

        Assert.Equal("Func", def.DisplayName);
        Assert.Equal(2, args.Length);
        Assert.Equal("Int32", args[0].DisplayName);
        Assert.True(args[1].IsVoid(), "Action's trailing return type must be Void.");
    }

    [Fact]
    public void ProjectMemberType_SystemActionOfTwo_ProjectsStandardFuncWithTrailingVoid()
    {
        // System.Action<int, string> → Standard.Func<Int32, String, Void>.
        var (def, args) = ProjectConstructedFunc(typeof(Action<int, string>));

        Assert.Equal("Func", def.DisplayName);
        Assert.Equal(3, args.Length);
        Assert.Equal("Int32", args[0].DisplayName);
        Assert.Equal("String", args[1].DisplayName);
        Assert.True(args[2].IsVoid());
    }

    [Fact]
    public void ProjectMemberType_SystemActionArity_MatchesFuncArityPlusOne()
    {
        // The projected Func definition's arity is the Action arity + 1 (the
        // synthesized Void return type), so it is reference-equal to the
        // matching member of the stdlib Func family.
        var projected = (ConstructedType)_projector.ProjectMemberType(typeof(Action<int>), _stdlib);
        Assert.Same(_stdlib.FuncOfArity(2), projected.GenericDefinition);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-generic System.Action projection (Requirement 15.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_NonGenericSystemAction_ProjectsStandardFuncVoid()
    {
        // System.Action (non-generic) → Standard.Func<Void>.
        var (def, args) = ProjectConstructedFunc(typeof(Action));

        Assert.Equal("Func", def.DisplayName);
        Assert.Single(args);
        Assert.True(args[0].IsVoid(), "Non-generic Action projects to Func<Void>.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: projected delegate emits the correct C# delegate type
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(typeof(Func<int, string>), "System.Func<int, string>")]
    [InlineData(typeof(Func<int>), "System.Func<int>")]
    [InlineData(typeof(Action<int>), "System.Action<int>")]
    [InlineData(typeof(Action<int, string>), "System.Action<int, string>")]
    [InlineData(typeof(Action), "System.Action")]
    public void ProjectedDelegate_EmitsOriginalDelegateShape(Type clrDelegate, string expectedCSharp)
    {
        // Round-trip: project the CLR delegate to Standard.Func, then map back to C#
        // via CSharpTypeMap. The projected type must emit the original delegate shape.
        var projected = _projector.ProjectMemberType(clrDelegate, _stdlib);
        Assert.Equal(expectedCSharp, CSharpTypeMap.GetTypeName(projected));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative: non-delegate System types are not projected as Func
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMemberType_NonDelegateSystemType_IsNotProjectedAsFunc()
    {
        // A plain System reference type (e.g., System.Exception) must not be
        // misidentified as a Func delegate.
        var projected = _projector.ProjectMemberType(typeof(Exception), _stdlib);
        var def = projected is ConstructedType ct ? ct.GenericDefinition : projected;
        Assert.NotEqual("Func", def.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Projects a CLR delegate type and asserts it became a <see cref="ConstructedType"/>
    /// wrapping a <see cref="ClassType"/> definition, returning the definition and its
    /// projected type arguments.
    /// </summary>
    private (ClassType Definition, IType[] Args) ProjectConstructedFunc(Type clrDelegate)
    {
        var projected = _projector.ProjectMemberType(clrDelegate, _stdlib);
        var ct = Assert.IsType<ConstructedType>(projected);
        var def = Assert.IsType<ClassType>(ct.GenericDefinition);
        return (def, ct.TypeArguments.ToArray());
    }
}
