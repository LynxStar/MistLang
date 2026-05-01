using Mist.Backends.CSharpTranspiler;
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
        Assert.Equal("void", CSharpTypeMap.GetTypeName(VoidType.Instance));
    }

    [Fact]
    public void GetTypeName_VoidType_InValueContext_ReturnsMistVoid()
    {
        Assert.Equal("MistVoid", CSharpTypeMap.GetTypeName(VoidType.Instance, isValueContext: true));
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
        Assert.Equal("System.Type", CSharpTypeMap.GetTypeName(TypeType.Instance));
    }
}
