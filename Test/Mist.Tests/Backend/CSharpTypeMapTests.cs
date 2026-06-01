using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="CSharpTypeMap"/> in its Backing_Type_Map form (Task 39).
/// Validates that stdlib types, legacy types, arrays, void, and other type
/// variants all map to the correct C# source representation.
/// </summary>
public class CSharpTypeMapTests
{
    // ── Helper factories ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a stdlib Backend_Backed StructType (e.g., Standard.Int32).
    /// </summary>
    private static StructType MakeStdlibStruct(string displayName) =>
        new(displayName, new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    /// <summary>
    /// Creates a stdlib Backend_Backed ClassType (e.g., Standard.String).
    /// </summary>
    private static ClassType MakeStdlibClass(string displayName) =>
        new(displayName, new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    // ── Task 39.1: stdlib struct types map to C# keywords ────────────────

    [Fact]
    public void GetTypeName_StandardInt32_ReturnsInt()
    {
        var standardInt32 = MakeStdlibStruct("Int32");
        Assert.Equal("int", CSharpTypeMap.GetTypeName(standardInt32));
    }

    [Fact]
    public void GetTypeName_StandardString_ReturnsString()
    {
        var standardString = MakeStdlibClass("String");
        Assert.Equal("string", CSharpTypeMap.GetTypeName(standardString));
    }

    [Fact]
    public void GetTypeName_ArrayOfStandardInt32_ReturnsIntArray()
    {
        var standardInt32 = MakeStdlibStruct("Int32");
        var arrayType = new ArrayType(standardInt32);
        Assert.Equal("int[]", CSharpTypeMap.GetTypeName(arrayType));
    }

    // ── All 14 stdlib types ──────────────────────────────────────────────

    [Theory]
    [InlineData("Int32", "int")]
    [InlineData("UInt32", "uint")]
    [InlineData("Int64", "long")]
    [InlineData("UInt64", "ulong")]
    [InlineData("Int16", "short")]
    [InlineData("UInt16", "ushort")]
    [InlineData("Byte", "byte")]
    [InlineData("SByte", "sbyte")]
    [InlineData("Single", "float")]
    [InlineData("Double", "double")]
    [InlineData("Decimal", "decimal")]
    [InlineData("Boolean", "bool")]
    [InlineData("Char", "char")]
    public void GetTypeName_AllStdlibStructTypes_ReturnCorrectKeyword(string displayName, string expected)
    {
        var stdlibType = MakeStdlibStruct(displayName);
        Assert.Equal(expected, CSharpTypeMap.GetTypeName(stdlibType));
    }

    [Fact]
    public void GetKeyword_StandardInt32_ReturnsInt()
    {
        var standardInt32 = MakeStdlibStruct("Int32");
        Assert.Equal("int", CSharpTypeMap.GetKeyword(standardInt32));
    }

    [Fact]
    public void GetKeyword_StandardString_ReturnsString()
    {
        var standardString = MakeStdlibClass("String");
        Assert.Equal("string", CSharpTypeMap.GetKeyword(standardString));
    }

    // ── Legacy PrimitiveType / StringType backward compat ────────────────

    [Fact]
    public void GetTypeName_LegacyPrimitiveTypeInt_ReturnsInt()
    {
        // LegacyPrimitiveTypes.Int has DisplayName = "int"
        Assert.Equal("int", CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Int));
    }

    [Fact]
    public void GetTypeName_LegacyStringType_ReturnsString()
    {
        Assert.Equal("string", CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.String));
    }

    // ── VoidType ─────────────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_VoidType_ReturnsVoid()
    {
        Assert.Equal("void", CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Void));
    }

    [Fact]
    public void GetTypeName_VoidType_InValueContext_ReturnsMistVoid()
    {
        Assert.Equal("MistVoid", CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Void, isValueContext: true));
    }

    // ── ArrayType ────────────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_NestedArray_ReturnsCorrectBrackets()
    {
        var standardInt32 = MakeStdlibStruct("Int32");
        var innerArray = new ArrayType(standardInt32);
        var outerArray = new ArrayType(innerArray);
        Assert.Equal("int[][]", CSharpTypeMap.GetTypeName(outerArray));
    }

    // ── TypeParameterType ────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_TypeParameter_ReturnsName()
    {
        var tp = new TypeParameterType("T", 0);
        Assert.Equal("T", CSharpTypeMap.GetTypeName(tp));
    }

    // ── ConstructedType ──────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_ConstructedType_ReturnsGenericForm()
    {
        var scope = new Scope(parent: null);
        var boxType = new ClassType("Box", scope, containingNamespace: "MyApp");
        boxType.TypeParameters = new[] { new TypeParameterType("T", 0) };
        var standardInt32 = MakeStdlibStruct("Int32");
        var constructed = new ConstructedType(boxType, new[] { standardInt32 });
        Assert.Equal("Box<int>", CSharpTypeMap.GetTypeName(constructed));
    }

    // ── Generic ClassType / StructType (non-stdlib) ──────────────────────

    [Fact]
    public void GetTypeName_UserClassType_ReturnsDisplayName()
    {
        var scope = new Scope(parent: null);
        var userClass = new ClassType("MyClass", scope, containingNamespace: "UserNs");
        Assert.Equal("MyClass", CSharpTypeMap.GetTypeName(userClass));
    }

    [Fact]
    public void GetTypeName_UserStructType_ReturnsDisplayName()
    {
        var scope = new Scope(parent: null);
        var userStruct = new StructType("Vector3", scope, containingNamespace: "UserNs");
        Assert.Equal("Vector3", CSharpTypeMap.GetTypeName(userStruct));
    }

    // ── InterfaceType ────────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_InterfaceType_ReturnsDisplayName()
    {
        var scope = new Scope(parent: null);
        var iface = new InterfaceType("IDrawable", scope, containingNamespace: "UserNs");
        Assert.Equal("IDrawable", CSharpTypeMap.GetTypeName(iface));
    }

    // ── TypeType ─────────────────────────────────────────────────────────

    [Fact]
    public void GetTypeName_TypeType_ReturnsSystemType()
    {
        Assert.Equal("System.Type", CSharpTypeMap.GetTypeName(LegacyPrimitiveTypes.Type));
    }

    // ── UnknownType and Closed_Generic_With_Unknown ─────────────────────
    // Task 29.2 (bare unknown) / Task 31 revised Option C (closed generic
    // with unknown) / Requirements 15.1, 15.2.
    //
    // Bare UnknownType lowers to C# `object`. A Closed_Generic_With_Unknown
    // (a constructed type whose type arguments transitively contain
    // UnknownType) lowers to C# `dynamic`. Rationale: C# generic types are
    // invariant at the CLR level — `List<int>` is not assignable to
    // `List<object>` for user-defined generics — so the naive `List<object>`
    // lowering throws InvalidCastException at runtime. `dynamic` routes
    // member access through the DLR, which dispatches against the
    // receiver's actual runtime generic type. Reachability analysis
    // (Phase C) prevents write-path member calls on such a receiver at
    // compile time, so the DLR only sees reachability-safe calls.

    [Fact]
    public void GetTypeName_UnknownType_ReturnsObject()
    {
        Assert.Equal("object", CSharpTypeMap.GetTypeName(UnknownType.Instance));
    }

    [Fact]
    public void GetTypeName_ListOfUnknown_ReturnsDynamic()
    {
        var scope = new Scope(parent: null);
        var listType = new ClassType("List", scope, containingNamespace: "UserNs")
        {
            TypeParameters = new[] { new TypeParameterType("T", 0) },
        };
        var constructed = new ConstructedType(listType, new IType[] { UnknownType.Instance });
        Assert.Equal("dynamic", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_MapOfStringUnknown_ReturnsDynamic()
    {
        var scope = new Scope(parent: null);
        var mapType = new ClassType("Map", scope, containingNamespace: "UserNs")
        {
            TypeParameters = new[]
            {
                new TypeParameterType("K", 0),
                new TypeParameterType("V", 1),
            },
        };
        var standardString = MakeStdlibClass("String");
        var constructed = new ConstructedType(
            mapType,
            new IType[] { standardString, UnknownType.Instance });
        // Multi-argument generic with unknown at a single position is still
        // Closed_Generic_With_Unknown; no partial preservation.
        Assert.Equal("dynamic", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_OptionalOfListOfUnknown_ReturnsDynamic()
    {
        var scope = new Scope(parent: null);
        var listType = new ClassType("List", scope, containingNamespace: "UserNs")
        {
            TypeParameters = new[] { new TypeParameterType("T", 0) },
        };
        var optionalType = new StructType("Optional", scope, containingNamespace: "UserNs")
        {
            TypeParameters = new[] { new TypeParameterType("T", 0) },
        };
        var innerConstructed = new ConstructedType(listType, new IType[] { UnknownType.Instance });
        var outerConstructed = new ConstructedType(optionalType, new IType[] { innerConstructed });
        // Nested unknown at any depth still triggers dynamic lowering.
        Assert.Equal("dynamic", CSharpTypeMap.GetTypeName(outerConstructed));
    }

    [Fact]
    public void GetTypeName_ListOfInt_ReturnsConstructedForm()
    {
        // Regression: a constructed type WITHOUT any unknown in its type
        // arguments still lowers to the naive `List<int>` form. The
        // `dynamic` lowering only fires when unknown is present.
        var scope = new Scope(parent: null);
        var listType = new ClassType("List", scope, containingNamespace: "UserNs")
        {
            TypeParameters = new[] { new TypeParameterType("T", 0) },
        };
        var standardInt32 = MakeStdlibStruct("Int32");
        var constructed = new ConstructedType(listType, new IType[] { standardInt32 });
        Assert.Equal("List<int>", CSharpTypeMap.GetTypeName(constructed));
    }

    // ── Stdlib Console/Math/Convert type map entries (Task 7.3) ──────────

    [Theory]
    [InlineData("Console", "System.Console")]
    [InlineData("Math", "System.Math")]
    [InlineData("Convert", "System.Convert")]
    public void TryGetMappedName_StdlibUtilityTypes_ReturnsClrEquivalent(string mistName, string expected)
    {
        var result = CSharpTypeMap.TryGetMappedName(mistName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Console", "System.Console")]
    [InlineData("Math", "System.Math")]
    [InlineData("Convert", "System.Convert")]
    public void GetKeyword_StdlibUtilityClassTypes_ReturnsClrEquivalent(string displayName, string expected)
    {
        var utilityType = MakeStdlibClass(displayName);
        Assert.Equal(expected, CSharpTypeMap.GetKeyword(utilityType));
    }

    // ── Standard.Func → System.Func / System.Action (first-class-functions Task 1.3) ──
    // Requirements 14.1, 14.2, 14.3: a Standard.Func constructed type maps to
    // System.Func<...> when the return type (last type argument) is non-void,
    // System.Action<...> when the return type is void with >= 1 parameter, and
    // the non-generic System.Action when the only type argument is void.

    /// <summary>
    /// Creates a stdlib Backend_Backed <c>Standard.Func</c> ClassType with the given
    /// number of type parameters (arity = parameters + return type).
    /// </summary>
    private static ClassType MakeStdlibFunc(int arity)
    {
        var func = new ClassType("Func", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);
        var typeParams = new IType[arity];
        for (var i = 0; i < arity; i++)
            typeParams[i] = new TypeParameterType($"T{i}", i);
        func.TypeParameters = typeParams;
        return func;
    }

    /// <summary>Creates the stdlib Backend_Backed <c>Standard.Void</c> struct.</summary>
    private static StructType MakeStdlibVoid() => MakeStdlibStruct("Void");

    [Fact]
    public void GetTypeName_FuncWithNonVoidReturn_ReturnsSystemFunc()
    {
        // func<int, string> → System.Func<int, string>
        var func = MakeStdlibFunc(2);
        var constructed = new ConstructedType(func, new IType[]
        {
            MakeStdlibStruct("Int32"),
            MakeStdlibClass("String"),
        });

        Assert.Equal("System.Func<int, string>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_FuncWithOnlyReturnType_ReturnsSystemFuncSingle()
    {
        // func<int> (zero-parameter function returning int) → System.Func<int>
        var func = MakeStdlibFunc(1);
        var constructed = new ConstructedType(func, new IType[] { MakeStdlibStruct("Int32") });

        Assert.Equal("System.Func<int>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_FuncWithVoidReturnAndOneParam_ReturnsSystemActionOfOne()
    {
        // func<int, void> (Action<int>) → System.Action<int>
        var func = MakeStdlibFunc(2);
        var constructed = new ConstructedType(func, new IType[]
        {
            MakeStdlibStruct("Int32"),
            MakeStdlibVoid(),
        });

        Assert.Equal("System.Action<int>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_FuncWithVoidReturnAndTwoParams_ReturnsSystemActionOfTwo()
    {
        // func<int, string, void> (Action<int, string>) → System.Action<int, string>
        var func = MakeStdlibFunc(3);
        var constructed = new ConstructedType(func, new IType[]
        {
            MakeStdlibStruct("Int32"),
            MakeStdlibClass("String"),
            MakeStdlibVoid(),
        });

        Assert.Equal("System.Action<int, string>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void GetTypeName_FuncWithOnlyVoid_ReturnsNonGenericSystemAction()
    {
        // func<void> (Action) → System.Action (non-generic)
        var func = MakeStdlibFunc(1);
        var constructed = new ConstructedType(func, new IType[] { MakeStdlibVoid() });

        Assert.Equal("System.Action", CSharpTypeMap.GetTypeName(constructed));
    }
}
