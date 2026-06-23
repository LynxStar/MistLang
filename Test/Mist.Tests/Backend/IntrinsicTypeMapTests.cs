using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="IntrinsicTypeMap"/>.
/// Validates that the reversed BackingTypes map correctly maps CLR type names
/// to Mist Standard Library names, that recursive substitution works in nested
/// generics, and that non-intrinsic types pass through unchanged.
/// </summary>
public class IntrinsicTypeMapTests
{
    private readonly IntrinsicTypeMap _map = new();
    private readonly IStdlibTypeProvider _stdlib = FallbackStdlibTypeProvider.Instance;

    // ── Helper factories ─────────────────────────────────────────────────

    private static StructType MakeClrStruct(string displayName, string ns) =>
        new(displayName, new Scope(parent: null), containingNamespace: ns);

    private static ClassType MakeClrClass(string displayName, string ns) =>
        new(displayName, new Scope(parent: null), containingNamespace: ns);

    private static AbstractType MakeClrInterface(string displayName, string ns) =>
        new(displayName, new Scope(parent: null), containingNamespace: ns);

    private static ClassType MakeGenericClass(string displayName, string ns, int arity)
    {
        var scope = new Scope(parent: null);
        var cls = new ClassType(displayName, scope, containingNamespace: ns);
        var typeParams = new TypeParameterType[arity];
        for (var i = 0; i < arity; i++)
            typeParams[i] = new TypeParameterType($"T{i}", i);
        cls.TypeParameters = typeParams;
        return cls;
    }

    // ── Requirement 7.1, 7.4: All BackingTypes entries reverse correctly ─

    [Theory]
    [InlineData("int", "Standard.Int32")]
    [InlineData("uint", "Standard.UInt32")]
    [InlineData("long", "Standard.Int64")]
    [InlineData("ulong", "Standard.UInt64")]
    [InlineData("short", "Standard.Int16")]
    [InlineData("ushort", "Standard.UInt16")]
    [InlineData("byte", "Standard.Byte")]
    [InlineData("sbyte", "Standard.SByte")]
    [InlineData("float", "Standard.Single")]
    [InlineData("double", "Standard.Double")]
    [InlineData("decimal", "Standard.Decimal")]
    [InlineData("bool", "Standard.Boolean")]
    [InlineData("char", "Standard.Char")]
    [InlineData("string", "Standard.String")]
    [InlineData("void", "Standard.Void")]
    public void GetMistTypeName_CSharpKeyword_ReturnsCorrectStdlibName(string keyword, string expected)
    {
        var result = _map.GetMistTypeName(keyword);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("System.Int32", "Standard.Int32")]
    [InlineData("System.UInt32", "Standard.UInt32")]
    [InlineData("System.Int64", "Standard.Int64")]
    [InlineData("System.UInt64", "Standard.UInt64")]
    [InlineData("System.Int16", "Standard.Int16")]
    [InlineData("System.UInt16", "Standard.UInt16")]
    [InlineData("System.Byte", "Standard.Byte")]
    [InlineData("System.SByte", "Standard.SByte")]
    [InlineData("System.Single", "Standard.Single")]
    [InlineData("System.Double", "Standard.Double")]
    [InlineData("System.Decimal", "Standard.Decimal")]
    [InlineData("System.Boolean", "Standard.Boolean")]
    [InlineData("System.Char", "Standard.Char")]
    [InlineData("System.String", "Standard.String")]
    [InlineData("System.Void", "Standard.Void")]
    public void GetMistTypeName_FullClrName_ReturnsCorrectStdlibName(string clrName, string expected)
    {
        var result = _map.GetMistTypeName(clrName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("System.Type", "Standard.Type")]
    [InlineData("System.Console", "Standard.Console")]
    [InlineData("System.Math", "Standard.Math")]
    [InlineData("System.Convert", "Standard.Convert")]
    [InlineData("System.Threading.Tasks.Task", "Standard.Task")]
    public void GetMistTypeName_QualifiedClrUtilityTypes_ReturnsCorrectStdlibName(string clrName, string expected)
    {
        var result = _map.GetMistTypeName(clrName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("System.Collections.Generic.IEnumerator", "Standard.IEnumerator")]
    [InlineData("System.Collections.Generic.IEnumerable", "Standard.IEnumerable")]
    public void GetMistTypeName_GenericCollectionInterfaces_ReturnsCorrectStdlibName(string clrName, string expected)
    {
        var result = _map.GetMistTypeName(clrName);
        Assert.Equal(expected, result);
    }

    // ── Requirement 7.6: Non-intrinsic types pass through unchanged ──────

    [Theory]
    [InlineData("System.Collections.Generic.List")]
    [InlineData("System.IO.File")]
    [InlineData("MyApp.CustomType")]
    [InlineData("UnknownType")]
    public void GetMistTypeName_NonIntrinsicType_ReturnsNull(string clrName)
    {
        var result = _map.GetMistTypeName(clrName);
        Assert.Null(result);
    }

    // ── SubstituteIntrinsics: intrinsic struct type substitution ──────────

    [Fact]
    public void SubstituteIntrinsics_SystemInt32Struct_ReturnsStdlibType()
    {
        var systemInt32 = MakeClrStruct("Int32", "System");
        var result = _map.SubstituteIntrinsics(systemInt32, _stdlib);

        // FallbackStdlibTypeProvider returns a StructType with DisplayName "int"
        Assert.IsType<StructType>(result);
        Assert.Equal("int", result.DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_SystemString_ReturnsStdlibType()
    {
        var systemString = MakeClrClass("String", "System");
        var result = _map.SubstituteIntrinsics(systemString, _stdlib);

        Assert.IsType<ClassType>(result);
        Assert.Equal("string", result.DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_SystemBoolean_ReturnsStdlibType()
    {
        var systemBool = MakeClrStruct("Boolean", "System");
        var result = _map.SubstituteIntrinsics(systemBool, _stdlib);

        Assert.IsType<StructType>(result);
        Assert.Equal("bool", result.DisplayName);
    }

    // ── SubstituteIntrinsics: non-intrinsic types pass through unchanged ─

    [Fact]
    public void SubstituteIntrinsics_NonIntrinsicClass_ReturnsUnchanged()
    {
        var userClass = MakeClrClass("MyService", "MyApp");
        var result = _map.SubstituteIntrinsics(userClass, _stdlib);

        Assert.Same(userClass, result);
    }

    [Fact]
    public void SubstituteIntrinsics_NonIntrinsicStruct_ReturnsUnchanged()
    {
        var userStruct = MakeClrStruct("Vector3", "MyApp");
        var result = _map.SubstituteIntrinsics(userStruct, _stdlib);

        Assert.Same(userStruct, result);
    }

    [Fact]
    public void SubstituteIntrinsics_NonIntrinsicInterface_ReturnsUnchanged()
    {
        var userInterface = MakeClrInterface("IDrawable", "MyApp");
        var result = _map.SubstituteIntrinsics(userInterface, _stdlib);

        Assert.Same(userInterface, result);
    }

    // ── SubstituteIntrinsics: already-stdlib types are not re-substituted ─

    [Fact]
    public void SubstituteIntrinsics_AlreadyStdlibStruct_ReturnsUnchanged()
    {
        var stdlibInt = new StructType("Int32", new Scope(null), containingNamespace: "Standard", isBackendBacked: true);
        var result = _map.SubstituteIntrinsics(stdlibInt, _stdlib);

        Assert.Same(stdlibInt, result);
    }

    [Fact]
    public void SubstituteIntrinsics_AlreadyStdlibClass_ReturnsUnchanged()
    {
        var stdlibString = new ClassType("String", new Scope(null), containingNamespace: "Standard", isBackendBacked: true);
        var result = _map.SubstituteIntrinsics(stdlibString, _stdlib);

        Assert.Same(stdlibString, result);
    }

    // ── Requirement 7.5: Recursive substitution in nested generics ───────

    [Fact]
    public void SubstituteIntrinsics_ConstructedTypeWithIntrinsicArg_SubstitutesArg()
    {
        // List<System.Int32> → List<stdlib int>
        var listDef = MakeGenericClass("List", "System.Collections.Generic", 1);
        var systemInt32 = MakeClrStruct("Int32", "System");
        var constructed = new ConstructedType(listDef, new IType[] { systemInt32 });

        var result = _map.SubstituteIntrinsics(constructed, _stdlib);

        Assert.IsType<ConstructedType>(result);
        var resultCt = (ConstructedType)result;
        Assert.Same(listDef, resultCt.GenericDefinition);
        Assert.Equal("int", resultCt.TypeArguments[0].DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_ConstructedTypeWithMultipleIntrinsicArgs_SubstitutesAll()
    {
        // Dictionary<System.String, System.Int32> → Dictionary<stdlib string, stdlib int>
        var dictDef = MakeGenericClass("Dictionary", "System.Collections.Generic", 2);
        var systemString = MakeClrClass("String", "System");
        var systemInt32 = MakeClrStruct("Int32", "System");
        var constructed = new ConstructedType(dictDef, new IType[] { systemString, systemInt32 });

        var result = _map.SubstituteIntrinsics(constructed, _stdlib);

        Assert.IsType<ConstructedType>(result);
        var resultCt = (ConstructedType)result;
        Assert.Equal("string", resultCt.TypeArguments[0].DisplayName);
        Assert.Equal("int", resultCt.TypeArguments[1].DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_NestedGenericWithIntrinsicArgs_SubstitutesRecursively()
    {
        // Dictionary<System.String, List<System.Int32>>
        // → Dictionary<stdlib string, List<stdlib int>>
        var listDef = MakeGenericClass("List", "System.Collections.Generic", 1);
        var dictDef = MakeGenericClass("Dictionary", "System.Collections.Generic", 2);

        var systemString = MakeClrClass("String", "System");
        var systemInt32 = MakeClrStruct("Int32", "System");

        var innerList = new ConstructedType(listDef, new IType[] { systemInt32 });
        var outerDict = new ConstructedType(dictDef, new IType[] { systemString, innerList });

        var result = _map.SubstituteIntrinsics(outerDict, _stdlib);

        Assert.IsType<ConstructedType>(result);
        var resultCt = (ConstructedType)result;
        Assert.Equal("string", resultCt.TypeArguments[0].DisplayName);

        var innerResult = Assert.IsType<ConstructedType>(resultCt.TypeArguments[1]);
        Assert.Equal("int", innerResult.TypeArguments[0].DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_ConstructedTypeWithNoIntrinsicArgs_ReturnsUnchanged()
    {
        // List<UserType> → unchanged
        var listDef = MakeGenericClass("List", "System.Collections.Generic", 1);
        var userType = MakeClrClass("MyService", "MyApp");
        var constructed = new ConstructedType(listDef, new IType[] { userType });

        var result = _map.SubstituteIntrinsics(constructed, _stdlib);

        Assert.Same(constructed, result);
    }

    [Fact]
    public void SubstituteIntrinsics_ConstructedTypeWithMixedArgs_SubstitutesOnlyIntrinsics()
    {
        // Dictionary<UserType, System.Int32> → Dictionary<UserType, stdlib int>
        var dictDef = MakeGenericClass("Dictionary", "System.Collections.Generic", 2);
        var userType = MakeClrClass("MyService", "MyApp");
        var systemInt32 = MakeClrStruct("Int32", "System");
        var constructed = new ConstructedType(dictDef, new IType[] { userType, systemInt32 });

        var result = _map.SubstituteIntrinsics(constructed, _stdlib);

        Assert.IsType<ConstructedType>(result);
        var resultCt = (ConstructedType)result;
        Assert.Same(userType, resultCt.TypeArguments[0]);
        Assert.Equal("int", resultCt.TypeArguments[1].DisplayName);
    }

    [Fact]
    public void SubstituteIntrinsics_DeeplyNestedGenerics_SubstitutesAtAllLevels()
    {
        // List<Dictionary<System.String, List<System.Boolean>>>
        // → List<Dictionary<stdlib string, List<stdlib bool>>>
        var listDef = MakeGenericClass("List", "System.Collections.Generic", 1);
        var dictDef = MakeGenericClass("Dictionary", "System.Collections.Generic", 2);

        var systemString = MakeClrClass("String", "System");
        var systemBool = MakeClrStruct("Boolean", "System");

        var innerList = new ConstructedType(listDef, new IType[] { systemBool });
        var middleDict = new ConstructedType(dictDef, new IType[] { systemString, innerList });
        var outerList = new ConstructedType(listDef, new IType[] { middleDict });

        var result = _map.SubstituteIntrinsics(outerList, _stdlib);

        Assert.IsType<ConstructedType>(result);
        var outerResult = (ConstructedType)result;

        var middleResult = Assert.IsType<ConstructedType>(outerResult.TypeArguments[0]);
        Assert.Equal("string", middleResult.TypeArguments[0].DisplayName);

        var innerResult = Assert.IsType<ConstructedType>(middleResult.TypeArguments[1]);
        Assert.Equal("bool", innerResult.TypeArguments[0].DisplayName);
    }

    // ── TypeParameterType passes through unchanged ───────────────────────

    [Fact]
    public void SubstituteIntrinsics_TypeParameter_ReturnsUnchanged()
    {
        var tp = new TypeParameterType("T", 0);
        var result = _map.SubstituteIntrinsics(tp, _stdlib);

        Assert.Same(tp, result);
    }

    // ── Regex type (non-keyword qualified type) ──────────────────────────

    [Fact]
    public void GetMistTypeName_SystemTextRegex_ReturnsStandardRegex()
    {
        var result = _map.GetMistTypeName("System.Text.RegularExpressions.Regex");
        Assert.Equal("Standard.Regex", result);
    }
}
