// Feature: generics-type-arguments, Property 4: Constructed type resolution
// **Validates: Requirements 7.1, 7.4, 7.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying constructed type resolution in the semantic model.
///
/// Property 4: For any type reference that includes a TypeArgumentList where the referenced
/// type is a generic type declaration with matching arity, the semantic model builder SHALL
/// produce a ConstructedType whose GenericDefinition is the referenced type and whose
/// TypeArguments match the resolved type arguments. When type arguments are themselves type
/// parameters, the type arguments SHALL resolve to the corresponding TypeParameterType.
/// When type arguments are nested generics (e.g., List&lt;List&lt;int&gt;&gt;), the inner
/// type arguments SHALL be resolved first and the outer ConstructedType SHALL contain the
/// inner ConstructedType as a type argument.
/// </summary>
public class ConstructedTypeResolutionPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

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
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
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

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
                TypeArgs = typeArgs,
            }
        };
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

    private static MemberDeclarationNode MakeFieldMember(string name, TypeReferenceNode typeRef)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(name),
                Type = typeRef,
            },
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
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

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: A generic class with a field whose type is a constructed type using
    /// concrete primitive type arguments.
    /// e.g., class Box&lt;T&gt; { } class Holder { Box&lt;int&gt; field; }
    /// </summary>
    public record ConcreteConstructedTypeScenario(
        string GenericTypeName,
        string HolderTypeName,
        string FieldName,
        ImmutableArray<string> TypeParamNames,
        ImmutableArray<string> ConcreteArgPrimitives)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            var args = string.Join(", ", ConcreteArgPrimitives);
            return $"class {GenericTypeName}<{tps}> {{ }} class {HolderTypeName} {{ {GenericTypeName}<{args}> {FieldName}; }}";
        }
    }

    /// <summary>
    /// Scenario: A generic class inherits from another generic class, forwarding type parameters
    /// as type arguments. Verifies that type arguments resolve to TypeParameterType.
    /// e.g., class Base&lt;T&gt; { } class Derived&lt;T&gt; : Base&lt;T&gt; { }
    /// </summary>
    public record TypeParamConstructedTypeScenario(
        string GenericTypeName,
        string DerivedTypeName,
        ImmutableArray<string> TypeParamNames)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            return $"class {GenericTypeName}<{tps}> {{ }} class {DerivedTypeName}<{tps}> : {GenericTypeName}<{tps}> {{ }}";
        }
    }

    /// <summary>
    /// Scenario: Nested generics — a field whose type is a constructed type with a nested
    /// constructed type argument.
    /// e.g., class Box&lt;T&gt; { } class Holder { Box&lt;Box&lt;int&gt;&gt; field; }
    /// </summary>
    public record NestedConstructedTypeScenario(
        string OuterTypeName,
        string InnerTypeName,
        string HolderTypeName,
        string FieldName,
        string InnerPrimitive)
    {
        public override string ToString()
        {
            return $"class {OuterTypeName}<T> {{ }} class {InnerTypeName}<T> {{ }} class {HolderTypeName} {{ {OuterTypeName}<{InnerTypeName}<{InnerPrimitive}>> {FieldName}; }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper"];
    private static readonly string[] HolderNamePool = ["Holder", "Owner", "Client"];
    private static readonly string[] FieldNamePool = ["field", "value", "data"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    public static Gen<ConcreteConstructedTypeScenario> GenConcreteScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from holderName in Gen.Elements(HolderNamePool)
               from fieldName in Gen.Elements(FieldNamePool)
               from count in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               from concreteArgs in Gen.ArrayOf(Gen.Elements(PrimitivePool), count)
               select new ConcreteConstructedTypeScenario(
                   genericName, holderName, fieldName, typeParams,
                   concreteArgs.ToImmutableArray());
    }

    public static Gen<TypeParamConstructedTypeScenario> GenTypeParamScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from derivedName in Gen.Elements(HolderNamePool)
               from count in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               select new TypeParamConstructedTypeScenario(
                   genericName, derivedName, typeParams);
    }

    public static Gen<NestedConstructedTypeScenario> GenNestedScenario()
    {
        return from outerName in Gen.Elements(GenericNamePool)
               from innerName in Gen.Elements("Inner", "Nested", "Deep")
               from holderName in Gen.Elements(HolderNamePool)
               from fieldName in Gen.Elements(FieldNamePool)
               from primitive in Gen.Elements(PrimitivePool)
               select new NestedConstructedTypeScenario(
                   outerName, innerName, holderName, fieldName, primitive);
    }

    public static Arbitrary<ConcreteConstructedTypeScenario> ArbitraryConcreteScenario()
    {
        return GenConcreteScenario().ToArbitrary();
    }

    public static Arbitrary<TypeParamConstructedTypeScenario> ArbitraryTypeParamScenario()
    {
        return GenTypeParamScenario().ToArbitrary();
    }

    public static Arbitrary<NestedConstructedTypeScenario> ArbitraryNestedScenario()
    {
        return GenNestedScenario().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4a: For any type reference with concrete type arguments where the referenced
    /// type has matching arity, the semantic model builder produces a ConstructedType whose
    /// GenericDefinition is the referenced type and whose TypeArguments are the concrete types.
    ///
    /// **Validates: Requirements 7.1, 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool ConcreteTypeArgs_ProduceConstructedType(ConcreteConstructedTypeScenario scenario)
    {
        // Build AST: generic class + holder class with a field of constructed type
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list with concrete primitive types
        var typeArgRefs = scenario.ConcreteArgPrimitives
            .Select(MakePrimitiveTypeRef)
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        // Field with constructed type reference: GenericType<int, string, ...>
        var fieldTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList);
        var fieldMember = MakeFieldMember(scenario.FieldName, fieldTypeRef);

        var holderClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.HolderTypeName),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = holderClassDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No generic-related diagnostics should be emitted
        var genericDiags = diagnostics.Where(d =>
            d.Id == GenericsDiagnostics.ArityMismatch.Id ||
            d.Id == GenericsDiagnostics.NotGeneric.Id ||
            d.Id == GenericsDiagnostics.MissingTypeArguments.Id ||
            d.Id == GenericsDiagnostics.UndefinedTypeArgument.Id).ToList();
        if(genericDiags.Count > 0)
        {
            return false;
        }

        // Find the field symbol
        var fieldSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.FieldName && s.Kind == SymbolKind.Field);
        if(fieldSymbol is null)
        {
            return false;
        }

        // The field's declared type should be a ConstructedType
        if(fieldSymbol.DeclaredType is not ConstructedType constructedType)
        {
            return false;
        }

        // GenericDefinition should be the generic class
        if(constructedType.GenericDefinition is not ClassType genericDef)
        {
            return false;
        }

        if(genericDef.DisplayName != scenario.GenericTypeName)
        {
            return false;
        }

        // TypeArguments count should match
        if(constructedType.TypeArguments.Count != scenario.ConcreteArgPrimitives.Length)
        {
            return false;
        }

        // Each type argument should be a concrete type (not a TypeParameterType)
        for(var i = 0; i < constructedType.TypeArguments.Count; i++)
        {
            if(constructedType.TypeArguments[i] is TypeParameterType)
            {
                return false;
            }

            if(constructedType.TypeArguments[i] is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 4b: When type arguments are themselves type parameters (e.g., Base&lt;T&gt;
    /// in class Derived&lt;T&gt; : Base&lt;T&gt;), the type arguments resolve to the
    /// corresponding TypeParameterType.
    ///
    /// **Validates: Requirements 7.1, 7.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool TypeParamArgs_ResolveToTypeParameterType(TypeParamConstructedTypeScenario scenario)
    {
        // Build AST: generic base class + derived class inheriting with forwarded type params
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list referencing the derived class's type parameters
        var typeArgRefs = scenario.TypeParamNames
            .Select(tp => MakeNamedTypeRef(tp))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName, typeArgList)]);

        var derivedClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.DerivedTypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = derivedClassDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No generic-related diagnostics should be emitted
        var genericDiags = diagnostics.Where(d =>
            d.Id == GenericsDiagnostics.ArityMismatch.Id ||
            d.Id == GenericsDiagnostics.NotGeneric.Id ||
            d.Id == GenericsDiagnostics.MissingTypeArguments.Id ||
            d.Id == GenericsDiagnostics.UndefinedTypeArgument.Id).ToList();
        if(genericDiags.Count > 0)
        {
            return false;
        }

        // Find the derived class symbol
        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.DerivedTypeName && s.Kind == SymbolKind.Class);
        if(derivedSymbol?.DeclaredType is not ClassType derivedType)
        {
            return false;
        }

        // BaseType should be a ConstructedType
        if(derivedType.BaseType is not ConstructedType constructedBase)
        {
            return false;
        }

        // GenericDefinition should be the generic class
        if(constructedBase.GenericDefinition is not ClassType genericDef)
        {
            return false;
        }

        if(genericDef.DisplayName != scenario.GenericTypeName)
        {
            return false;
        }

        // TypeArguments count should match
        if(constructedBase.TypeArguments.Count != scenario.TypeParamNames.Length)
        {
            return false;
        }

        // Each type argument should be a TypeParameterType matching the derived class's type params
        for(var i = 0; i < constructedBase.TypeArguments.Count; i++)
        {
            if(constructedBase.TypeArguments[i] is not TypeParameterType tpt)
            {
                return false;
            }

            if(tpt.Name != scenario.TypeParamNames[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 4c: When type arguments are nested generics (e.g., Box&lt;Box&lt;int&gt;&gt;),
    /// the inner type arguments are resolved first and the outer ConstructedType contains
    /// the inner ConstructedType as a type argument.
    ///
    /// **Validates: Requirements 7.1, 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstructedTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool NestedGenerics_ResolveToNestedConstructedType(NestedConstructedTypeScenario scenario)
    {
        // Build AST: two generic classes + holder with nested constructed type field
        var outerClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.OuterTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var innerClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.InnerTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build nested type reference: Outer<Inner<int>>
        var innerTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.InnerPrimitive)]);
        var innerTypeRef = MakeNamedTypeRef(scenario.InnerTypeName, innerTypeArgList);
        var outerTypeArgList = MakeTypeArgList([innerTypeRef]);
        var outerTypeRef = MakeNamedTypeRef(scenario.OuterTypeName, outerTypeArgList);

        var fieldMember = MakeFieldMember(scenario.FieldName, outerTypeRef);

        var holderClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.HolderTypeName),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = outerClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = innerClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = holderClassDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No generic-related diagnostics should be emitted
        var genericDiags = diagnostics.Where(d =>
            d.Id == GenericsDiagnostics.ArityMismatch.Id ||
            d.Id == GenericsDiagnostics.NotGeneric.Id ||
            d.Id == GenericsDiagnostics.MissingTypeArguments.Id ||
            d.Id == GenericsDiagnostics.UndefinedTypeArgument.Id).ToList();
        if(genericDiags.Count > 0)
        {
            return false;
        }

        // Find the field symbol
        var fieldSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.FieldName && s.Kind == SymbolKind.Field);
        if(fieldSymbol is null)
        {
            return false;
        }

        // The field's declared type should be a ConstructedType (outer)
        if(fieldSymbol.DeclaredType is not ConstructedType outerConstructed)
        {
            return false;
        }

        // Outer GenericDefinition should be the outer class
        if(outerConstructed.GenericDefinition is not ClassType outerDef)
        {
            return false;
        }

        if(outerDef.DisplayName != scenario.OuterTypeName)
        {
            return false;
        }

        // Outer should have exactly 1 type argument
        if(outerConstructed.TypeArguments.Count != 1)
        {
            return false;
        }

        // The type argument should be a ConstructedType (inner)
        if(outerConstructed.TypeArguments[0] is not ConstructedType innerConstructed)
        {
            return false;
        }

        // Inner GenericDefinition should be the inner class
        if(innerConstructed.GenericDefinition is not ClassType innerDef)
        {
            return false;
        }

        if(innerDef.DisplayName != scenario.InnerTypeName)
        {
            return false;
        }

        // The inner type argument should not be a TypeParameterType
        if(innerConstructed.TypeArguments[0] is TypeParameterType)
        {
            return false;
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void SingleTypeArg_Field_ResolvesToConstructedType()
    {
        // class Box<T> { }
        // class Holder { Box<int> field; }
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var fieldTypeRef = MakeNamedTypeRef("Box", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = holderClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var fieldSymbol = model.AllSymbols.First(s => s.Name == "field" && s.Kind == SymbolKind.Field);
        var constructedType = Assert.IsType<ConstructedType>(fieldSymbol.DeclaredType);
        Assert.Equal("Box", ((ClassType)constructedType.GenericDefinition).DisplayName);
        _ = Assert.Single(constructedType.TypeArguments);
        Assert.Equal("int", constructedType.TypeArguments[0].DisplayName);
        Assert.Equal("Box<int>", constructedType.DisplayName);
    }

    [Fact]
    public void NestedGenerics_Field_ResolvesToNestedConstructedType()
    {
        // class Box<T> { }
        // class Holder { Box<Box<int>> field; }
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var innerTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var innerTypeRef = MakeNamedTypeRef("Box", innerTypeArgList);
        var outerTypeArgList = MakeTypeArgList([innerTypeRef]);
        var outerTypeRef = MakeNamedTypeRef("Box", outerTypeArgList);
        var fieldMember = MakeFieldMember("field", outerTypeRef);

        var holderClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = holderClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var fieldSymbol = model.AllSymbols.First(s => s.Name == "field" && s.Kind == SymbolKind.Field);
        var outerConstructed = Assert.IsType<ConstructedType>(fieldSymbol.DeclaredType);
        Assert.Equal("Box", ((ClassType)outerConstructed.GenericDefinition).DisplayName);
        _ = Assert.Single(outerConstructed.TypeArguments);

        var innerConstructed = Assert.IsType<ConstructedType>(outerConstructed.TypeArguments[0]);
        Assert.Equal("Box", ((ClassType)innerConstructed.GenericDefinition).DisplayName);
        _ = Assert.Single(innerConstructed.TypeArguments);
        Assert.Equal("int", innerConstructed.TypeArguments[0].DisplayName);
        Assert.Equal("Box<Box<int>>", outerConstructed.DisplayName);
    }

    [Fact]
    public void TypeParamAsArg_BaseClause_ResolvesToTypeParameterType()
    {
        // class Box<T> { }
        // class Wrapper<T> : Box<T> { }
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef("Box", typeArgList)]);

        var wrapperClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Wrapper"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = wrapperClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var wrapperSymbol = model.AllSymbols.First(s => s.Name == "Wrapper" && s.Kind == SymbolKind.Class);
        var wrapperType = Assert.IsType<ClassType>(wrapperSymbol.DeclaredType);
        var constructedBase = Assert.IsType<ConstructedType>(wrapperType.BaseType);
        Assert.Equal("Box", ((ClassType)constructedBase.GenericDefinition).DisplayName);
        _ = Assert.Single(constructedBase.TypeArguments);
        var tpt = Assert.IsType<TypeParameterType>(constructedBase.TypeArguments[0]);
        Assert.Equal("T", tpt.Name);
    }

    [Fact]
    public void MultipleTypeArgs_Field_ResolvesToConstructedType()
    {
        // class Map<K, V> { }
        // class Holder { Map<int, string> field; }
        var genericClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int"), MakePrimitiveTypeRef("string")]);
        var fieldTypeRef = MakeNamedTypeRef("Map", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new BodyTypeTypeDeclarationNode { TypeBody = genericClassDecl },
            new BodyTypeTypeDeclarationNode { TypeBody = holderClassDecl });

        var (model, _) = BuildModelWithDiagnostics(ast);

        var fieldSymbol = model.AllSymbols.First(s => s.Name == "field" && s.Kind == SymbolKind.Field);
        var constructedType = Assert.IsType<ConstructedType>(fieldSymbol.DeclaredType);
        Assert.Equal("Map", ((ClassType)constructedType.GenericDefinition).DisplayName);
        Assert.Equal(2, constructedType.TypeArguments.Count);
        Assert.Equal("int", constructedType.TypeArguments[0].DisplayName);
        Assert.Equal("string", constructedType.TypeArguments[1].DisplayName);
    }
}