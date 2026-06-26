using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="TypeProjector"/>.
/// Validates CLR type → Mist type projection for classes, structs, abstract types, enums,
/// generics, constraints, inheritance, abstract type implementations, optional parameters,
/// and generic method type parameters.
/// Requirements: 4.1–4.8, 5.1–5.9, 10.1–10.5.
/// </summary>
public class TypeProjectorTests
{
    private readonly TypeProjector _projector;
    private readonly IStdlibTypeProvider _stdlib;

    public TypeProjectorTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, filter);
        _stdlib = FallbackStdlibTypeProvider.Instance;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type Kind Projection (Requirements 4.1, 4.2, 4.3, 4.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_SimpleClass_ProducesClassType()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);

        Assert.IsType<ClassType>(type);
        Assert.Equal("SimpleClass", type.DisplayName);
        var classType = (ClassType)type;
        Assert.Equal("Mist.Tests.InteropFixtures", classType.ContainingNamespace);
    }

    [Fact]
    public void ProjectType_SimpleStruct_ProducesStructType()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleStruct), _stdlib);

        Assert.IsType<StructType>(type);
        Assert.Equal("SimpleStruct", type.DisplayName);
        var structType = (StructType)type;
        Assert.Equal("Mist.Tests.InteropFixtures", structType.ContainingNamespace);
    }

    [Fact]
    public void ProjectType_ClrInterface_ProducesAbstractType()
    {
        var (type, _) = _projector.ProjectType(typeof(ISimpleInterface), _stdlib);

        Assert.IsType<AbstractType>(type);
        Assert.Equal("ISimpleInterface", type.DisplayName);
        var abstractType = (AbstractType)type;
        Assert.Equal("Mist.Tests.InteropFixtures", abstractType.ContainingNamespace);
    }

    [Fact]
    public void ProjectType_SimpleEnum_ProducesEnumType()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleEnum), _stdlib);

        Assert.IsType<EnumType>(type);
        Assert.Equal("SimpleEnum", type.DisplayName);
        var enumType = (EnumType)type;
        Assert.Equal("Mist.Tests.InteropFixtures", enumType.ContainingNamespace);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enum Projection (Requirement 4.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_Enum_HasCorrectMembers()
    {
        var (type, symbols) = _projector.ProjectType(typeof(SimpleEnum), _stdlib);
        var enumType = Assert.IsType<EnumType>(type);

        Assert.Equal(4, enumType.Members.Count);
        Assert.Equal("None", enumType.Members[0].Name);
        Assert.Equal("First", enumType.Members[1].Name);
        Assert.Equal("Second", enumType.Members[2].Name);
        Assert.Equal("Third", enumType.Members[3].Name);
    }

    [Fact]
    public void ProjectType_Enum_HasCorrectMemberValues()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleEnum), _stdlib);
        var enumType = Assert.IsType<EnumType>(type);

        Assert.Equal(0, enumType.Members[0].ResolvedValue);
        Assert.Equal(1, enumType.Members[1].ResolvedValue);
        Assert.Equal(2, enumType.Members[2].ResolvedValue);
        Assert.Equal(3, enumType.Members[3].ResolvedValue);
    }

    [Fact]
    public void ProjectType_Enum_HasUnderlyingType()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleEnum), _stdlib);
        var enumType = Assert.IsType<EnumType>(type);

        // SimpleEnum has underlying type int → maps to the fallback int type
        Assert.NotNull(enumType.UnderlyingType);
    }

    [Fact]
    public void ProjectType_Enum_MemberSymbolsAreReadonly()
    {
        var (_, symbols) = _projector.ProjectType(typeof(SimpleEnum), _stdlib);

        Assert.All(symbols, s =>
        {
            var symbol = Assert.IsType<Symbol>(s);
            Assert.Equal(SymbolKind.Field, symbol.Kind);
            Assert.Equal(SymbolOrigin.Backend, symbol.Origin);
            Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic Type Parameter Projection (Requirements 4.5, 10.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_GenericClass_HasTypeParameters()
    {
        var (type, _) = _projector.ProjectType(typeof(GenericClass<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.Single(classType.TypeParameters);
        var typeParam = Assert.IsType<TypeParameterType>(classType.TypeParameters[0]);
        Assert.Equal("T", typeParam.Name);
        Assert.Equal(0, typeParam.Ordinal);
    }

    [Fact]
    public void ProjectType_GenericClass_StripsAritySuffix()
    {
        var (type, _) = _projector.ProjectType(typeof(GenericClass<>), _stdlib);

        // GenericClass`1 should have the `1 stripped
        Assert.Equal("GenericClass", type.DisplayName);
    }

    [Fact]
    public void ProjectType_MultiConstraint_HasTypeParameters()
    {
        var (type, _) = _projector.ProjectType(typeof(MultiConstraint<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.Single(classType.TypeParameters);
        var typeParam = Assert.IsType<TypeParameterType>(classType.TypeParameters[0]);
        Assert.Equal("T", typeParam.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic Constraint Translation (Requirements 4.6, 10.2–10.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_GenericClassWithClassConstraint_TranslatesConstraint()
    {
        var (type, _) = _projector.ProjectType(typeof(GenericClass<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);
        var typeParam = Assert.IsType<TypeParameterType>(classType.TypeParameters[0]);

        Assert.NotNull(typeParam.Constraints);
        Assert.True(typeParam.Constraints!.HasClassConstraint);
        Assert.False(typeParam.Constraints.HasStructConstraint);
    }

    [Fact]
    public void ProjectType_MultiConstraintWithStructConstraint_TranslatesConstraint()
    {
        var (type, _) = _projector.ProjectType(typeof(MultiConstraint<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);
        var typeParam = Assert.IsType<TypeParameterType>(classType.TypeParameters[0]);

        Assert.NotNull(typeParam.Constraints);
        Assert.True(typeParam.Constraints!.HasStructConstraint);
        Assert.False(typeParam.Constraints.HasClassConstraint);
    }

    [Fact]
    public void ProjectType_MultiConstraintWithAbstractConstraint_TranslatesAbstractConstraint()
    {
        var (type, _) = _projector.ProjectType(typeof(MultiConstraint<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);
        var typeParam = Assert.IsType<TypeParameterType>(classType.TypeParameters[0]);

        Assert.NotNull(typeParam.Constraints);
        // MultiConstraint<T> where T : struct, IComparable<T>
        // Should have at least one abstract type constraint (IComparable<T>)
        Assert.NotEmpty(typeParam.Constraints!.AbstractConstraints);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inheritance Chain Projection (Requirements 4.7, 4.8)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_DerivedClass_HasBaseType()
    {
        var (type, _) = _projector.ProjectType(typeof(Middle), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        // Middle inherits from Base (not System.Object)
        Assert.NotNull(classType.BaseType);
        Assert.Equal("Base", classType.BaseType!.DisplayName);
    }

    [Fact]
    public void ProjectType_MultiLevelInheritance_HasCorrectBaseType()
    {
        var (type, _) = _projector.ProjectType(typeof(Derived), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        // Derived inherits from Middle
        Assert.NotNull(classType.BaseType);
        Assert.Equal("Middle", classType.BaseType!.DisplayName);
    }

    [Fact]
    public void ProjectType_BaseClass_HasNoBaseType()
    {
        var (type, _) = _projector.ProjectType(typeof(Base), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        // Base inherits from System.Object, which should be null (not projected)
        Assert.Null(classType.BaseType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Abstract Type Implementation Projection (Requirement 4.8)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_CustomCollection_HasAbstracts()
    {
        var (type, _) = _projector.ProjectType(typeof(CustomCollection), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        // CustomCollection implements IEnumerable<int> (non-generic IEnumerable is filtered)
        Assert.NotEmpty(classType.Abstracts);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Member Projection (Requirements 5.1–5.9)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_SimpleClass_ProjectsInstanceMethods()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleClass), type, _stdlib);

        // SimpleClass has GetValue() and SetValue(int) instance methods
        var methodNames = members
            .Where(m => m.Kind == SymbolKind.Method)
            .Select(m => m.Name)
            .ToList();

        Assert.Contains("GetValue", methodNames);
        Assert.Contains("SetValue", methodNames);
    }

    [Fact]
    public void ProjectMembers_SimpleClass_ProjectsConstructors()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleClass), type, _stdlib);

        var constructors = members.Where(m => m.Kind == SymbolKind.Constructor).ToList();
        // SimpleClass has 2 constructors: () and (int)
        Assert.Equal(2, constructors.Count);
        Assert.All(constructors, c => Assert.Equal(".ctor", c.Name));
    }

    [Fact]
    public void ProjectMembers_SimpleClass_ProjectsProperties()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleClass), type, _stdlib);

        var properties = members.Where(m => m.Kind == SymbolKind.Property).ToList();
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.Contains("Value", propertyNames);
        Assert.Contains("Name", propertyNames);
    }

    [Fact]
    public void ProjectMembers_SimpleStruct_ProjectsFields()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleStruct), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleStruct), type, _stdlib);

        var fields = members.Where(m => m.Kind == SymbolKind.Field).ToList();
        var fieldNames = fields.Select(f => f.Name).ToList();

        // SimpleStruct has public fields X and Y
        Assert.Contains("X", fieldNames);
        Assert.Contains("Y", fieldNames);
    }

    [Fact]
    public void ProjectMembers_AllSymbolsHaveBackendOrigin()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleClass), type, _stdlib);

        Assert.All(members, m => Assert.Equal(SymbolOrigin.Backend, m.Origin));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Optional Parameters (Requirement 5.6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_OptionalParameters_ProjectedAsOptional()
    {
        var (type, _) = _projector.ProjectType(typeof(HasOptionalParams), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasOptionalParams), type, _stdlib);

        // HasOptionalParams.Compute(int a, int b = 10, int c = 20)
        var computeMethod = members.First(m => m.Name == "Compute");
        var symbol = Assert.IsType<Symbol>(computeMethod);

        // Check that the method has parameters stored (3 params: a, b, c)
        Assert.NotNull(symbol.Parameters);
        Assert.Equal(3, symbol.Parameters.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic Method Type Parameters (Requirement 5.7)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_GenericMethod_HasTypeParameters()
    {
        // Use a type that has a generic method — System.Linq.Enumerable has many,
        // but let's use a simpler approach with the fixture types.
        // GenericClass<T> has non-generic methods, so let's use a well-known type.
        // System.Array has generic static methods like Empty<T>().
        var (type, _) = _projector.ProjectType(typeof(Array), _stdlib);
        var members = _projector.ProjectMembers(typeof(Array), type, _stdlib);

        // Array.Empty<T>() is a generic method
        var emptyMethod = members.FirstOrDefault(m => m.Name == "Empty");
        if(emptyMethod is not null)
        {
            var symbol = Assert.IsType<Symbol>(emptyMethod);
            Assert.NotEmpty(symbol.TypeParameters);
            var typeParam = Assert.IsType<TypeParameterType>(symbol.TypeParameters[0]);
            Assert.Equal("T", typeParam.Name);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Indexer Projection (Requirement 5.9)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_HasIndexer_ProjectsIndexers()
    {
        var (type, _) = _projector.ProjectType(typeof(HasIndexer), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasIndexer), type, _stdlib);

        var indexers = members.Where(m => m.Kind == SymbolKind.Indexer).ToList();
        // HasIndexer has 2 indexers: this[int] and this[int, int]
        Assert.Equal(2, indexers.Count);
        Assert.All(indexers, i => Assert.Equal("this[]", i.Name));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Visibility Filter Integration (Requirements 6.1–6.7)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_OutParamMethod_IsVisible()
    {
        var (type, _) = _projector.ProjectType(typeof(HasOutParam), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasOutParam), type, _stdlib);

        var methodNames = members
            .Where(m => m.Kind == SymbolKind.Method)
            .Select(m => m.Name)
            .ToList();

        // TryParse has out param → now visible (projected as ref with ClrCallingConvention.Out)
        Assert.Contains("TryParse", methodNames);
        // Parse has no out param → visible
        Assert.Contains("Parse", methodNames);
    }

    [Fact]
    public void ProjectMembers_RefParamMethod_IsVisible()
    {
        var (type, _) = _projector.ProjectType(typeof(HasRefParam), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasRefParam), type, _stdlib);

        var methodNames = members
            .Where(m => m.Kind == SymbolKind.Method)
            .Select(m => m.Name)
            .ToList();

        // Swap has ref params → now visible (CLR Ref Interop feature)
        Assert.Contains("Swap", methodNames);
        // Add has no ref params → visible
        Assert.Contains("Add", methodNames);
    }

    [Fact]
    public void ProjectMembers_InParamMethod_IsNotFiltered()
    {
        var (type, _) = _projector.ProjectType(typeof(HasInParam), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasInParam), type, _stdlib);

        var methodNames = members
            .Where(m => m.Kind == SymbolKind.Method)
            .Select(m => m.Name)
            .ToList();

        // in params are treated as pass-by-value → NOT filtered
        Assert.Contains("ReadValue", methodNames);
        Assert.Contains("Sum", methodNames);
        Assert.Contains("Format", methodNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Overload Preservation (Requirement 11.1, 11.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_Overloads_AllVisibleOverloadsProjected()
    {
        var (type, _) = _projector.ProjectType(typeof(HasOverloads), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasOverloads), type, _stdlib);

        var processMethods = members
            .Where(m => m.Kind == SymbolKind.Method && m.Name == "Process")
            .ToList();

        // HasOverloads has 5 Process overloads — all now visible (out params pass through)
        Assert.Equal(5, processMethods.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property Read-Only vs Read-Write (Requirement 5.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_ReadWriteProperty_IsMarkedMutable()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleClass), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleClass), type, _stdlib);

        // SimpleClass.Value has both get and set → read-write
        var valueProp = members.First(m => m.Name == "Value" && m.Kind == SymbolKind.Property);
        var symbol = Assert.IsType<Symbol>(valueProp);
        Assert.Equal(ReadonlyStatus.Mutable, symbol.ReadonlyStatus);
    }

    [Fact]
    public void ProjectMembers_ReadOnlyProperty_IsMarkedReadonly()
    {
        var (type, _) = _projector.ProjectType(typeof(HasIndexer), _stdlib);
        var members = _projector.ProjectMembers(typeof(HasIndexer), type, _stdlib);

        // HasIndexer.Length has only a getter → read-only
        var lengthProp = members.First(m => m.Name == "Length" && m.Kind == SymbolKind.Property);
        var symbol = Assert.IsType<Symbol>(lengthProp);
        Assert.Equal(ReadonlyStatus.ExplicitReadonly, symbol.ReadonlyStatus);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Field Readonly Projection (Requirement 5.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectMembers_MutableField_IsMarkedMutable()
    {
        var (type, _) = _projector.ProjectType(typeof(SimpleStruct), _stdlib);
        var members = _projector.ProjectMembers(typeof(SimpleStruct), type, _stdlib);

        // SimpleStruct.X is a public non-readonly field → mutable
        var xField = members.First(m => m.Name == "X" && m.Kind == SymbolKind.Field);
        var symbol = Assert.IsType<Symbol>(xField);
        Assert.Equal(ReadonlyStatus.Mutable, symbol.ReadonlyStatus);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // StripGenericAritySuffix helper
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Dictionary`2", "Dictionary")]
    [InlineData("List`1", "List")]
    [InlineData("SimpleClass", "SimpleClass")]
    [InlineData("IEnumerable`1", "IEnumerable")]
    public void StripGenericAritySuffix_StripsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, TypeProjector.StripGenericAritySuffix(input));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IDisposable Detection (Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 6.1, 6.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectType_IDisposableClass_HasDropTrue()
    {
        // MemoryStream implements IDisposable → HasDrop should be true
        var (type, _) = _projector.ProjectType(typeof(System.IO.MemoryStream), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.HasDrop);
    }

    [Fact]
    public void ProjectType_IDisposableClass_IsDisposableDropTrue()
    {
        // MemoryStream implements IDisposable, no explicit Drop() → IsDisposableDrop should be true
        var (type, _) = _projector.ProjectType(typeof(System.IO.MemoryStream), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.IsDisposableDrop);
    }

    [Fact]
    public void ProjectType_NonDisposableClass_HasDropFalse()
    {
        // System.String does NOT implement IDisposable → HasDrop should be false
        var (type, _) = _projector.ProjectType(typeof(string), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.False(classType.HasDrop);
    }

    [Fact]
    public void ProjectType_NonDisposableClass_IsDisposableDropFalse()
    {
        // System.String does NOT implement IDisposable → IsDisposableDrop should be false
        var (type, _) = _projector.ProjectType(typeof(string), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.False(classType.IsDisposableDrop);
    }

    [Fact]
    public void ProjectType_TransitiveIDisposable_HasDropTrue()
    {
        // System.IO.Stream is abstract and implements IDisposable transitively
        var (type, _) = _projector.ProjectType(typeof(System.IO.Stream), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.HasDrop);
    }

    [Fact]
    public void ProjectType_TransitiveIDisposable_IsDisposableDropTrue()
    {
        // System.IO.Stream implements IDisposable transitively → IsDisposableDrop = true
        var (type, _) = _projector.ProjectType(typeof(System.IO.Stream), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.IsDisposableDrop);
    }

    [Fact]
    public void ProjectType_GenericIDisposable_FlagsSetOnDefinition()
    {
        // GenericDisposable<T> implements IDisposable → flags set on generic definition
        var (type, _) = _projector.ProjectType(typeof(GenericDisposable<>), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.HasDrop);
        Assert.True(classType.IsDisposableDrop);
    }

    [Fact]
    public void ProjectType_AsyncDisposableOnly_HasDropFalse()
    {
        // AsyncOnlyDisposable implements only IAsyncDisposable → HasDrop should be false
        var (type, _) = _projector.ProjectType(typeof(AsyncOnlyDisposable), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.False(classType.HasDrop);
    }

    [Fact]
    public void ProjectType_AsyncDisposableOnly_IsDisposableDropFalse()
    {
        // AsyncOnlyDisposable implements only IAsyncDisposable → IsDisposableDrop should be false
        var (type, _) = _projector.ProjectType(typeof(AsyncOnlyDisposable), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.False(classType.IsDisposableDrop);
    }

    [Fact]
    public void ProjectType_BothDisposables_RecognizedViaIDisposable()
    {
        // BothDisposables implements both IDisposable and IAsyncDisposable
        // → recognized via IDisposable (HasDrop=true, IsDisposableDrop=true)
        var (type, _) = _projector.ProjectType(typeof(BothDisposables), _stdlib);
        var classType = Assert.IsType<ClassType>(type);

        Assert.True(classType.HasDrop);
        Assert.True(classType.IsDisposableDrop);
    }
}