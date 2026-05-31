// Feature: generics-type-arguments, Property 7: Generic inheritance resolution
// **Validates: Requirements 8.1, 8.2, 8.3, 8.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying generic inheritance resolution in the semantic model.
///
/// Property 7: For any class that specifies a generic base class with type arguments,
/// and for any type that specifies generic interfaces with type arguments, the semantic
/// model builder SHALL resolve each base type and interface to a ConstructedType with
/// the correct type arguments. When a generic type inherits from another generic type
/// using its own type parameters (e.g., class OrderedList&lt;T&gt; : List&lt;T&gt;),
/// the base type's type arguments SHALL reference the derived type's TypeParameterType
/// instances. When a non-generic type inherits from a generic type with concrete arguments
/// (e.g., class IntList : List&lt;int&gt;), the base type's type arguments SHALL be the
/// concrete types.
/// </summary>
public class GenericInheritanceResolutionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = MakeToken(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MakeToken(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name)
    {
        return new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
        } };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    private static BaseListNode MakeBaseList(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            Rest = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new BaseTypeRefNode { Name = MakeToken(name), TypeArgs = typeArgs };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: A generic class inherits from another generic class, forwarding type parameters.
    /// e.g., class Derived&lt;T&gt; : Base&lt;T&gt;
    /// </summary>
    public record GenericBaseClassScenario(
        string BaseName,
        string DerivedName,
        ImmutableArray<string> TypeParamNames)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            return $"class {DerivedName}<{tps}> : {BaseName}<{tps}>";
        }
    }

    /// <summary>
    /// Scenario: A non-generic class inherits from a generic class with concrete type arguments.
    /// e.g., class IntList : List&lt;int&gt;
    /// </summary>
    public record ConcreteBaseClassScenario(
        string BaseName,
        string DerivedName,
        ImmutableArray<string> BaseTypeParamNames,
        ImmutableArray<string> ConcreteArgPrimitives)
    {
        public override string ToString()
        {
            var args = string.Join(", ", ConcreteArgPrimitives);
            return $"class {DerivedName} : {BaseName}<{args}>";
        }
    }

    /// <summary>
    /// Scenario: A type implements a generic interface with type arguments.
    /// e.g., class Box&lt;T&gt; : IContainer&lt;T&gt;
    /// </summary>
    public record GenericInterfaceScenario(
        string TypeName,
        string InterfaceName,
        ImmutableArray<string> TypeParamNames,
        bool TypeIsGeneric)
    {
        public override string ToString()
        {
            if (TypeIsGeneric)
            {
                var tps = string.Join(", ", TypeParamNames);
                return $"class {TypeName}<{tps}> : {InterfaceName}<{tps}>";
            }
            return $"class {TypeName} : {InterfaceName}<int>";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] BaseNamePool = ["Base", "Parent", "Root"];
    private static readonly string[] DerivedNamePool = ["Derived", "Child", "Sub"];
    private static readonly string[] InterfaceNamePool = ["IContainer", "ICollection", "IReadable"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    public static Gen<GenericBaseClassScenario> GenGenericBaseClassScenario()
    {
        return from baseName in Gen.Elements(BaseNamePool)
               from derivedName in Gen.Elements(DerivedNamePool)
               from count in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               select new GenericBaseClassScenario(baseName, derivedName, typeParams);
    }

    public static Gen<ConcreteBaseClassScenario> GenConcreteBaseClassScenario()
    {
        return from baseName in Gen.Elements(BaseNamePool)
               from derivedName in Gen.Elements(DerivedNamePool)
               from count in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               from concreteArgs in Gen.ArrayOf(Gen.Elements(PrimitivePool), count)
               select new ConcreteBaseClassScenario(
                   baseName, derivedName, typeParams,
                   concreteArgs.ToImmutableArray());
    }

    public static Gen<GenericInterfaceScenario> GenGenericInterfaceScenario()
    {
        return from typeName in Gen.Elements(DerivedNamePool)
               from interfaceName in Gen.Elements(InterfaceNamePool)
               from count in Gen.Choose(1, 3)
               from isGeneric in Gen.Elements(true, false)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               select new GenericInterfaceScenario(typeName, interfaceName, typeParams, isGeneric);
    }

    public static Arbitrary<GenericBaseClassScenario> ArbitraryGenericBaseClassScenario() =>
        GenGenericBaseClassScenario().ToArbitrary();

    public static Arbitrary<ConcreteBaseClassScenario> ArbitraryConcreteBaseClassScenario() =>
        GenConcreteBaseClassScenario().ToArbitrary();

    public static Arbitrary<GenericInterfaceScenario> ArbitraryGenericInterfaceScenario() =>
        GenGenericInterfaceScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7a: When a generic class inherits from another generic class using its
    /// own type parameters (e.g., class Derived&lt;T&gt; : Base&lt;T&gt;), the base type
    /// resolves to a ConstructedType whose type arguments reference the derived type's
    /// TypeParameterType instances.
    ///
    /// **Validates: Requirements 8.1, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericInheritanceResolutionPropertyTests) }, MaxTest = 100)]
    public bool GenericBaseClass_ResolvesToConstructedTypeWithTypeParams(GenericBaseClassScenario scenario)
    {
        // Build AST: generic base class + generic derived class inheriting from it with forwarded type params
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.BaseName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list referencing the derived class's type parameters
        var typeArgRefs = scenario.TypeParamNames
            .Select(tp => MakeNamedTypeRef(tp))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.BaseName, typeArgList)]);

        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.DerivedName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No arity or missing type argument diagnostics should be emitted
        var genericDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments ||
            d.Id == DiagnosticId.NotGeneric).ToList();
        if (genericDiags.Count > 0)
            return false;

        // Find the derived class symbol
        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.DerivedName && s.Kind == SymbolKind.Class);
        if (derivedSymbol?.DeclaredType is not ClassType derivedType)
            return false;

        // BaseType should be a ConstructedType
        if (derivedType.BaseType is not ConstructedType constructedBase)
            return false;

        // GenericDefinition should be the base class
        if (constructedBase.GenericDefinition is not ClassType baseType)
            return false;
        if (baseType.DisplayName != scenario.BaseName)
            return false;

        // TypeArguments count should match
        if (constructedBase.TypeArguments.Count != scenario.TypeParamNames.Length)
            return false;

        // Each type argument should be a TypeParameterType matching the derived class's type params
        for (var i = 0; i < constructedBase.TypeArguments.Count; i++)
        {
            if (constructedBase.TypeArguments[i] is not TypeParameterType tpt)
                return false;
            if (tpt.Name != scenario.TypeParamNames[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 7b: When a non-generic class inherits from a generic class with concrete
    /// type arguments (e.g., class IntList : List&lt;int&gt;), the base type resolves to
    /// a ConstructedType whose type arguments are the concrete types.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericInheritanceResolutionPropertyTests) }, MaxTest = 100)]
    public bool ConcreteBaseClass_ResolvesToConstructedTypeWithConcreteArgs(ConcreteBaseClassScenario scenario)
    {
        // Build AST: generic base class + non-generic derived class with concrete type args
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.BaseName),
            TypeParams = MakeTypeParamList(scenario.BaseTypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list with concrete primitive types
        var typeArgRefs = scenario.ConcreteArgPrimitives
            .Select(p => MakePrimitiveTypeRef(p))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.BaseName, typeArgList)]);

        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.DerivedName),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No arity or missing type argument diagnostics
        var genericDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments ||
            d.Id == DiagnosticId.NotGeneric).ToList();
        if (genericDiags.Count > 0)
            return false;

        // Find the derived class symbol
        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.DerivedName && s.Kind == SymbolKind.Class);
        if (derivedSymbol?.DeclaredType is not ClassType derivedType)
            return false;

        // BaseType should be a ConstructedType
        if (derivedType.BaseType is not ConstructedType constructedBase)
            return false;

        // GenericDefinition should be the base class
        if (constructedBase.GenericDefinition is not ClassType baseType)
            return false;
        if (baseType.DisplayName != scenario.BaseName)
            return false;

        // TypeArguments count should match
        if (constructedBase.TypeArguments.Count != scenario.ConcreteArgPrimitives.Length)
            return false;

        // Each type argument should be a concrete primitive type (not a TypeParameterType)
        for (var i = 0; i < constructedBase.TypeArguments.Count; i++)
        {
            if (constructedBase.TypeArguments[i] is TypeParameterType)
                return false;
            // Verify the type argument is not null
            if (constructedBase.TypeArguments[i] is null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 7c: When a type implements a generic interface with type arguments,
    /// the interface resolves to a ConstructedType with the correct type arguments.
    ///
    /// **Validates: Requirements 8.3, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericInheritanceResolutionPropertyTests) }, MaxTest = 100)]
    public bool GenericInterface_ResolvesToConstructedTypeWithCorrectArgs(GenericInterfaceScenario scenario)
    {
        // Build AST: generic interface + class implementing it
        var interfaceDecl = new InterfaceDeclarationNode
        {
            Name = MakeToken(scenario.InterfaceName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };

        TypeArgumentListNode typeArgList;
        ClassDeclarationNode classDecl;

        if (scenario.TypeIsGeneric)
        {
            // class Box<T> : IContainer<T>
            var typeArgRefs = scenario.TypeParamNames
                .Select(tp => MakeNamedTypeRef(tp))
                .ToArray();
            typeArgList = MakeTypeArgList(typeArgRefs);

            var baseList = MakeBaseList([MakeBaseTypeRef(scenario.InterfaceName, typeArgList)]);

            classDecl = new ClassDeclarationNode
            {
                Name = MakeToken(scenario.TypeName),
                TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
                Members = ImmutableArray<MemberDeclarationNode>.Empty,
                BaseClause = baseList,
                BaseList = baseList,
            };
        }
        else
        {
            // class Derived : IContainer<int>
            var concreteArgs = scenario.TypeParamNames
                .Select(_ => MakePrimitiveTypeRef("int"))
                .ToArray();
            typeArgList = MakeTypeArgList(concreteArgs);

            var baseList = MakeBaseList([MakeBaseTypeRef(scenario.InterfaceName, typeArgList)]);

            classDecl = new ClassDeclarationNode
            {
                Name = MakeToken(scenario.TypeName),
                Members = ImmutableArray<MemberDeclarationNode>.Empty,
                BaseClause = baseList,
                BaseList = baseList,
            };
        }

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No arity or missing type argument diagnostics
        var genericDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments ||
            d.Id == DiagnosticId.NotGeneric).ToList();
        if (genericDiags.Count > 0)
            return false;

        // Find the class symbol
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == SymbolKind.Class);
        if (classSymbol?.DeclaredType is not ClassType classType)
            return false;

        // Interfaces should contain at least one ConstructedType
        if (classType.Interfaces.Count == 0)
            return false;

        var constructedInterface = classType.Interfaces[0] as ConstructedType;
        if (constructedInterface is null)
            return false;

        // GenericDefinition should be the interface
        if (constructedInterface.GenericDefinition is not InterfaceType resolvedInterface)
            return false;
        if (resolvedInterface.DisplayName != scenario.InterfaceName)
            return false;

        // TypeArguments count should match
        if (constructedInterface.TypeArguments.Count != scenario.TypeParamNames.Length)
            return false;

        if (scenario.TypeIsGeneric)
        {
            // Type arguments should be TypeParameterType instances
            for (var i = 0; i < constructedInterface.TypeArguments.Count; i++)
            {
                if (constructedInterface.TypeArguments[i] is not TypeParameterType tpt)
                    return false;
                if (tpt.Name != scenario.TypeParamNames[i])
                    return false;
            }
        }
        else
        {
            // Type arguments should be concrete types (not TypeParameterType)
            for (var i = 0; i < constructedInterface.TypeArguments.Count; i++)
            {
                if (constructedInterface.TypeArguments[i] is TypeParameterType)
                    return false;
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void GenericClassInheritance_SingleTypeParam_ResolvesToConstructedType()
    {
        // class Base<T> { }
        // class Derived<T> : Base<T> { }
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Base"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef("Base", typeArgList)]);

        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Derived"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var derivedSymbol = model.AllSymbols.First(s => s.Name == "Derived" && s.Kind == SymbolKind.Class);
        var derivedType = Assert.IsType<ClassType>(derivedSymbol.DeclaredType);

        var constructedBase = Assert.IsType<ConstructedType>(derivedType.BaseType);
        Assert.Equal("Base", ((ClassType)constructedBase.GenericDefinition).DisplayName);
        Assert.Single(constructedBase.TypeArguments);
        var tpt = Assert.IsType<TypeParameterType>(constructedBase.TypeArguments[0]);
        Assert.Equal("T", tpt.Name);
    }

    [Fact]
    public void NonGenericClassInheritance_ConcreteArgs_ResolvesToConstructedType()
    {
        // class Base<T> { }
        // class IntChild : Base<int> { }
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Base"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var baseList = MakeBaseList([MakeBaseTypeRef("Base", typeArgList)]);

        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("IntChild"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var derivedSymbol = model.AllSymbols.First(s => s.Name == "IntChild" && s.Kind == SymbolKind.Class);
        var derivedType = Assert.IsType<ClassType>(derivedSymbol.DeclaredType);

        var constructedBase = Assert.IsType<ConstructedType>(derivedType.BaseType);
        Assert.Equal("Base", ((ClassType)constructedBase.GenericDefinition).DisplayName);
        Assert.Single(constructedBase.TypeArguments);
        Assert.Equal("int", constructedBase.TypeArguments[0].DisplayName);
    }

    [Fact]
    public void GenericInterfaceImplementation_ResolvesToConstructedType()
    {
        // interface IContainer<T> { }
        // class Box<T> : IContainer<T> { }
        var interfaceDecl = new InterfaceDeclarationNode
        {
            Name = MakeToken("IContainer"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef("IContainer", typeArgList)]);

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Box" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);

        Assert.Single(classType.Interfaces);
        var constructedInterface = Assert.IsType<ConstructedType>(classType.Interfaces[0]);
        Assert.Equal("IContainer", ((InterfaceType)constructedInterface.GenericDefinition).DisplayName);
        Assert.Single(constructedInterface.TypeArguments);
        var tpt = Assert.IsType<TypeParameterType>(constructedInterface.TypeArguments[0]);
        Assert.Equal("T", tpt.Name);
    }

    [Fact]
    public void MultipleTypeParams_BaseClass_AllForwarded()
    {
        // class Map<K, V> { }
        // class OrderedMap<K, V> : Map<K, V> { }
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("K"), MakeNamedTypeRef("V")]);
        var baseList = MakeBaseList([MakeBaseTypeRef("Map", typeArgList)]);

        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("OrderedMap"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var derivedSymbol = model.AllSymbols.First(s => s.Name == "OrderedMap" && s.Kind == SymbolKind.Class);
        var derivedType = Assert.IsType<ClassType>(derivedSymbol.DeclaredType);

        var constructedBase = Assert.IsType<ConstructedType>(derivedType.BaseType);
        Assert.Equal(2, constructedBase.TypeArguments.Count);

        var k = Assert.IsType<TypeParameterType>(constructedBase.TypeArguments[0]);
        Assert.Equal("K", k.Name);
        var v = Assert.IsType<TypeParameterType>(constructedBase.TypeArguments[1]);
        Assert.Equal("V", v.Name);
    }
}
