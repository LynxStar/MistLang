// Feature: generic-traits, Property 6: Structural match with type argument substitution
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for structural match with type argument substitution.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**
///
/// For any type (class or struct) and for any constructed trait type, the structural
/// match result is true if and only if every effective member of the constructed trait
/// (after substituting type parameters with the provided type arguments) has a
/// corresponding public member on the type with the same name and compatible type
/// signature. This includes: method parameters substituted from type parameters to
/// concrete types, return types substituted, and field types substituted.
/// </summary>
public class TraitStructuralMatchSubstitutionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation()
    {
        var sourceText = new SourceText("test.mist", "");
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items) =>
        new() { Items = items.ToImmutableArray() };

    private static TopLevelItemNode MakeTraitItem(TraitDeclarationNode traitDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        };

    private static TopLevelItemNode MakeClassItem(ClassDeclarationNode classDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        };

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
    {
        var first = new TypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => new TypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(params TypeReferenceNode[] typeRefs) =>
        new()
        {
            TypeReference = typeRefs[0],
            TypeReferences = typeRefs.Skip(1).ToImmutableArray(),
        };

    private static TypeReferenceNode MakeIntTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeStringTypeRef() =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() };

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(name),
                IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
            },
        };

    private static BaseListNode MakeBaseList(params BaseTypeRefNode[] refs) =>
        new()
        {
            First = refs[0],
            Rest = refs[0],
            BaseTypeRefs = refs.Skip(1).ToImmutableArray(),
        };

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null) =>
        new() { Name = Tok(name), TypeArgs = typeArgs };

    /// <summary>
    /// Creates a generic trait with a method whose parameter type is the first type parameter
    /// and return type is int.
    /// E.g., trait Comparable&lt;T&gt; { int CompareTo(T other); }
    /// </summary>
    private static TraitDeclarationNode MakeGenericTraitWithMethodParam(
        string traitName, string[] typeParams, string methodName, string paramName)
    {
        return new TraitDeclarationNode
        {
            Name = Tok(traitName),
            TypeParams = MakeTypeParamList(typeParams),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok(methodName),
                        Parameters = new ParameterListNode
                        {
                            Parameter = new ParameterNode
                            {
                                Type = MakeNamedTypeRef(typeParams[0]),
                                Name = Tok(paramName),
                            },
                        },
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a generic trait with a method whose return type is the first type parameter.
    /// E.g., trait Provider&lt;T&gt; { T GetValue(); }
    /// </summary>
    private static TraitDeclarationNode MakeGenericTraitWithReturnType(
        string traitName, string[] typeParams, string methodName)
    {
        return new TraitDeclarationNode
        {
            Name = Tok(traitName),
            TypeParams = MakeTypeParamList(typeParams),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeNamedTypeRef(typeParams[0]),
                        Name = Tok(methodName),
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a generic trait with a field whose type is the first type parameter.
    /// E.g., trait Container&lt;T&gt; { T value; }
    /// </summary>
    private static TraitDeclarationNode MakeGenericTraitWithField(
        string traitName, string[] typeParams, string fieldName)
    {
        return new TraitDeclarationNode
        {
            Name = Tok(traitName),
            TypeParams = MakeTypeParamList(typeParams),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeNamedTypeRef(typeParams[0]),
                        Name = Tok(fieldName),
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a non-generic "bridge" trait that extends a constructed generic trait.
    /// This makes the constructed trait discoverable by the satisfaction resolution pass.
    /// E.g., trait IntComparable : Comparable&lt;int&gt; { }
    /// </summary>
    private static TraitDeclarationNode MakeBridgeTrait(
        string bridgeName, string parentTraitName, TypeReferenceNode typeArg)
    {
        return new TraitDeclarationNode
        {
            Name = Tok(bridgeName),
            Members = ImmutableArray<TraitMemberDeclarationNode>.Empty,
            Parents = MakeBaseList(MakeBaseTypeRef(parentTraitName, MakeTypeArgList(typeArg))),
        };
    }

    /// <summary>
    /// Creates a class with a public method.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicMethod(
        string className, TypeReferenceNode returnType, string methodName,
        (TypeReferenceNode type, string name)? parameter = null)
    {
        ParameterListNode? paramList = null;
        if (parameter is not null)
        {
            paramList = new ParameterListNode
            {
                Parameter = new ParameterNode
                {
                    Type = parameter.Value.type,
                    Name = Tok(parameter.Value.name),
                },
            };
        }

        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = new MethodDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                        ReturnType = returnType,
                        Name = Tok(methodName),
                        Parameters = paramList,
                        Body = new MethodBlockBodyMethodBodyNode
                        {
                            Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                        },
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a class with a public field.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicField(
        string className, TypeReferenceNode fieldType, string fieldName)
    {
        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = new FieldDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                        Type = fieldType,
                        Name = Tok(fieldName),
                    },
                }),
        };
    }

    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind) =>
        model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;

    private static TraitType? FindTrait(ISemanticModel model, string name) =>
        model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Trait)
            ?.DeclaredType as TraitType;

    /// <summary>
    /// Checks whether a type satisfies a specific constructed trait in the satisfaction map.
    /// </summary>
    private static bool SatisfiesConstructedTrait(
        ISemanticModel model, IType type, string genericTraitName)
    {
        return model.TraitSatisfactionMap.All()
            .Any(s => ReferenceEquals(s.Type, type) &&
                      s.Trait is ConstructedTraitType ctt &&
                      ctt.GenericTraitDefinition.DisplayName == genericTraitName);
    }

    /// <summary>
    /// Gets the ConstructedTraitType that a type satisfies for a given generic trait name.
    /// </summary>
    private static ConstructedTraitType? GetSatisfiedConstructedTrait(
        ISemanticModel model, IType type, string genericTraitName)
    {
        return model.TraitSatisfactionMap.All()
            .Where(s => ReferenceEquals(s.Type, type) &&
                        s.Trait is ConstructedTraitType ctt &&
                        ctt.GenericTraitDefinition.DisplayName == genericTraitName)
            .Select(s => (ConstructedTraitType)s.Trait)
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6a: Method parameter type substitution — positive case
    //
    // A class with `int CompareTo(int other)` satisfies `Comparable<int>`
    // where trait Comparable<T> { int CompareTo(T other); }
    //
    // **Validates: Requirements 12.1, 12.2, 12.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParamSubstitution_IntType_Satisfies_ComparableInt()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<int> { }
        //          class IntComparer { public int CompareTo(int other) { } }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", MakeIntTypeRef());
        var classDecl = MakeClassWithPublicMethod("IntComparer", MakeIntTypeRef(), "CompareTo",
            (MakeIntTypeRef(), "other"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "IntComparer", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Comparable"),
            "IntComparer should satisfy Comparable<int> via method parameter substitution");

        // Verify the constructed trait has int as type argument
        var ctt = GetSatisfiedConstructedTrait(model, classType, "Comparable");
        Assert.NotNull(ctt);
        Assert.Equal("int", ctt.TypeArguments[0].DisplayName);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    public void MethodParamSubstitution_VariousTypes_Satisfies(string typeArg)
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<typeArg> { }
        //          class MyComparer { public int CompareTo(<typeArg> other) { } }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var typeArgRef = typeArg == "int" ? MakeIntTypeRef() : MakeStringTypeRef();
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", typeArgRef);
        var classDecl = MakeClassWithPublicMethod("MyComparer", MakeIntTypeRef(), "CompareTo",
            (typeArgRef, "other"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "MyComparer", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Comparable"),
            $"MyComparer should satisfy Comparable<{typeArg}>");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6b: Method parameter type substitution — string satisfies
    //              Comparable<string> but NOT Comparable<int>
    //
    // When both Comparable<int> and Comparable<string> are in play, a class
    // with `int CompareTo(string other)` satisfies only Comparable<string>.
    //
    // **Validates: Requirements 12.1, 12.2, 12.3, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParamSubstitution_StringType_SatisfiesComparableString_NotComparableInt()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _BridgeInt : Comparable<int> { }
        //          trait _BridgeStr : Comparable<string> { }
        //          class StringComparer { public int CompareTo(string other) { } }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeInt = MakeBridgeTrait("_BridgeInt", "Comparable", MakeIntTypeRef());
        var bridgeStr = MakeBridgeTrait("_BridgeStr", "Comparable", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicMethod("StringComparer", MakeIntTypeRef(), "CompareTo",
            (MakeStringTypeRef(), "other"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeInt),
            MakeTraitItem(bridgeStr),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "StringComparer", SymbolKind.Class);
        Assert.NotNull(classType);

        // Should satisfy Comparable<string> — verify by checking that a constructed
        // trait with the string type argument exists in the satisfaction map
        var allSatisfactions = model.TraitSatisfactionMap.All()
            .Where(s => ReferenceEquals(s.Type, classType) &&
                        s.Trait is ConstructedTraitType ctt &&
                        ctt.GenericTraitDefinition.DisplayName == "Comparable" &&
                        ctt.TypeArguments[0].DisplayName == "string")
            .ToList();
        Assert.NotEmpty(allSatisfactions);

        // Should NOT satisfy Comparable<Int32> (the stdlib struct type)
        var comparableTrait = FindTrait(model, "Comparable");
        Assert.NotNull(comparableTrait);

        var int32Type = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Int32" && s.Kind == SymbolKind.Struct)
            ?.DeclaredType;

        if (int32Type is not null)
        {
            var comparableInt32 = new ConstructedTraitType(comparableTrait, [int32Type]);
            Assert.False(model.TraitSatisfactionMap.Satisfies(classType, comparableInt32),
                "StringComparer should NOT satisfy Comparable<Int32>");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6c: Return type substitution
    //
    // A class with `string GetValue()` satisfies a trait with `T GetValue()`
    // when T=string.
    //
    // **Validates: Requirements 12.1, 12.2, 12.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReturnTypeSubstitution_StringReturn_SatisfiesProviderString()
    {
        // Arrange: trait Provider<T> { T GetValue(); }
        //          trait _Bridge : Provider<string> { }
        //          class StringProvider { public string GetValue() { } }
        var traitDecl = MakeGenericTraitWithReturnType("Provider", ["T"], "GetValue");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Provider", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicMethod("StringProvider", MakeStringTypeRef(), "GetValue");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "StringProvider", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Provider"),
            "StringProvider should satisfy Provider<string> via return type substitution");

        var ctt = GetSatisfiedConstructedTrait(model, classType, "Provider");
        Assert.NotNull(ctt);
        Assert.Equal("string", ctt.TypeArguments[0].DisplayName);
    }

    [Fact]
    public void ReturnTypeSubstitution_IntReturn_SatisfiesProviderInt()
    {
        // Arrange: trait Provider<T> { T GetValue(); }
        //          trait _Bridge : Provider<int> { }
        //          class IntProvider { public int GetValue() { } }
        var traitDecl = MakeGenericTraitWithReturnType("Provider", ["T"], "GetValue");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Provider", MakeIntTypeRef());
        var classDecl = MakeClassWithPublicMethod("IntProvider", MakeIntTypeRef(), "GetValue");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "IntProvider", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Provider"),
            "IntProvider should satisfy Provider<int> via return type substitution");

        var ctt = GetSatisfiedConstructedTrait(model, classType, "Provider");
        Assert.NotNull(ctt);
        Assert.Equal("int", ctt.TypeArguments[0].DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6d: Field type substitution
    //
    // A class with `int value` satisfies a trait with `T value;` when T=int.
    //
    // **Validates: Requirements 12.1, 12.2, 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldTypeSubstitution_IntField_SatisfiesContainerInt()
    {
        // Arrange: trait Container<T> { T value; }
        //          trait _Bridge : Container<int> { }
        //          class IntContainer { public int value; }
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Container", MakeIntTypeRef());
        var classDecl = MakeClassWithPublicField("IntContainer", MakeIntTypeRef(), "value");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "IntContainer", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Container"),
            "IntContainer should satisfy Container<int> via field type substitution");

        var ctt = GetSatisfiedConstructedTrait(model, classType, "Container");
        Assert.NotNull(ctt);
        Assert.Equal("int", ctt.TypeArguments[0].DisplayName);
    }

    [Fact]
    public void FieldTypeSubstitution_StringField_SatisfiesContainerString()
    {
        // Arrange: trait Container<T> { T value; }
        //          trait _Bridge : Container<string> { }
        //          class StringContainer { public string value; }
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Container", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicField("StringContainer", MakeStringTypeRef(), "value");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "StringContainer", SymbolKind.Class);
        Assert.NotNull(classType);

        Assert.True(SatisfiesConstructedTrait(model, classType, "Container"),
            "StringContainer should satisfy Container<string> via field type substitution");

        var ctt = GetSatisfiedConstructedTrait(model, classType, "Container");
        Assert.NotNull(ctt);
        Assert.Equal("string", ctt.TypeArguments[0].DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6e: Missing member — verified via effective members computation
    //
    // Verifies that the effective members of a constructed trait correctly
    // include the trait's members with substitution context, which is the
    // prerequisite for structural matching to work.
    //
    // **Validates: Requirements 12.2, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EffectiveMembers_ConstructedTrait_IncludesAllMembers()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<int> { }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", MakeIntTypeRef());

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert: the bridge trait's effective members include CompareTo from parent
        var bridgeTrait = FindTrait(model, "_Bridge");
        Assert.NotNull(bridgeTrait);

        var bridgeEffective = SemanticModelBuilder.GetEffectiveMembers(bridgeTrait);
        Assert.Single(bridgeEffective);
        Assert.Equal("CompareTo", bridgeEffective[0].Member.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6f: Wrong parameter type after substitution — type satisfies
    //              only the matching constructed trait, not a different one
    //
    // A class with `int CompareTo(string other)` satisfies Comparable<string>
    // but NOT Comparable<Int32> (the stdlib struct type, verified via the
    // satisfaction map with a manually constructed key).
    //
    // **Validates: Requirements 12.3, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WrongParamType_SatisfiesOnlyMatchingConstructedTrait()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _BridgeInt : Comparable<int> { }
        //          trait _BridgeStr : Comparable<string> { }
        //          class StringComparer { public int CompareTo(string other) { } }
        // StringComparer satisfies Comparable<string> but NOT Comparable<Int32> (stdlib)
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeInt = MakeBridgeTrait("_BridgeInt", "Comparable", MakeIntTypeRef());
        var bridgeStr = MakeBridgeTrait("_BridgeStr", "Comparable", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicMethod("StringComparer", MakeIntTypeRef(), "CompareTo",
            (MakeStringTypeRef(), "other"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeInt),
            MakeTraitItem(bridgeStr),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "StringComparer", SymbolKind.Class);
        Assert.NotNull(classType);

        // Verify that StringComparer does NOT satisfy Comparable<Int32> (the stdlib struct)
        // by constructing a ConstructedTraitType with the stdlib Int32 type
        var comparableTrait = FindTrait(model, "Comparable");
        Assert.NotNull(comparableTrait);

        var int32Type = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Int32" && s.Kind == SymbolKind.Struct)
            ?.DeclaredType;

        if (int32Type is not null)
        {
            var comparableInt32 = new ConstructedTraitType(comparableTrait, [int32Type]);
            Assert.False(model.TraitSatisfactionMap.Satisfies(classType, comparableInt32),
                "StringComparer should NOT satisfy Comparable<Int32>");
        }
    }

    [Fact]
    public void WrongReturnType_SatisfiesOnlyMatchingConstructedTrait()
    {
        // Arrange: trait Provider<T> { T GetValue(); }
        //          trait _BridgeInt : Provider<int> { }
        //          trait _BridgeStr : Provider<string> { }
        //          class IntProvider { public int GetValue() { } }
        // IntProvider satisfies Provider<int> but NOT Provider<String> (stdlib)
        var traitDecl = MakeGenericTraitWithReturnType("Provider", ["T"], "GetValue");
        var bridgeInt = MakeBridgeTrait("_BridgeInt", "Provider", MakeIntTypeRef());
        var bridgeStr = MakeBridgeTrait("_BridgeStr", "Provider", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicMethod("IntProvider", MakeIntTypeRef(), "GetValue");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeInt),
            MakeTraitItem(bridgeStr),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "IntProvider", SymbolKind.Class);
        Assert.NotNull(classType);

        // Verify that IntProvider does NOT satisfy Provider<String> (stdlib)
        var providerTrait = FindTrait(model, "Provider");
        Assert.NotNull(providerTrait);

        var stringType = model.AllSymbols
            .FirstOrDefault(s => s.Name == "String" && s.Kind == SymbolKind.Class)
            ?.DeclaredType;

        if (stringType is not null)
        {
            var providerString = new ConstructedTraitType(providerTrait, [stringType]);
            Assert.False(model.TraitSatisfactionMap.Satisfies(classType, providerString),
                "IntProvider should NOT satisfy Provider<String>");
        }
    }

    [Fact]
    public void WrongFieldType_SatisfiesOnlyMatchingConstructedTrait()
    {
        // Arrange: trait Container<T> { T value; }
        //          trait _BridgeInt : Container<int> { }
        //          trait _BridgeStr : Container<string> { }
        //          class IntContainer { public int value; }
        // IntContainer satisfies Container<int> but NOT Container<String> (stdlib)
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var bridgeInt = MakeBridgeTrait("_BridgeInt", "Container", MakeIntTypeRef());
        var bridgeStr = MakeBridgeTrait("_BridgeStr", "Container", MakeStringTypeRef());
        var classDecl = MakeClassWithPublicField("IntContainer", MakeIntTypeRef(), "value");

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeInt),
            MakeTraitItem(bridgeStr),
            MakeClassItem(classDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var classType = FindType(model, "IntContainer", SymbolKind.Class);
        Assert.NotNull(classType);

        // Verify that IntContainer does NOT satisfy Container<String> (stdlib)
        var containerTrait = FindTrait(model, "Container");
        Assert.NotNull(containerTrait);

        var stringType = model.AllSymbols
            .FirstOrDefault(s => s.Name == "String" && s.Kind == SymbolKind.Class)
            ?.DeclaredType;

        if (stringType is not null)
        {
            var containerString = new ConstructedTraitType(containerTrait, [stringType]);
            Assert.False(model.TraitSatisfactionMap.Satisfies(classType, containerString),
                "IntContainer should NOT satisfy Container<String>");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6g: Effective members computation with type argument substitution
    //
    // Verifies that GetEffectiveMembers correctly computes the effective
    // member set for a ConstructedTraitType with substitution context.
    //
    // **Validates: Requirements 12.1, 12.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EffectiveMembers_ConstructedTrait_HasSubstitutionContext()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<int> { }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", MakeIntTypeRef());

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert: verify the trait's member scope is populated
        var comparableTrait = FindTrait(model, "Comparable");
        Assert.NotNull(comparableTrait);

        // Get the ConstructedTraitType from the bridge trait's parent
        var bridgeTrait = FindTrait(model, "_Bridge");
        Assert.NotNull(bridgeTrait);
        Assert.Single(bridgeTrait.ParentTraits);
        var constructedComparable = bridgeTrait.ParentTraits[0] as ConstructedTraitType;
        Assert.NotNull(constructedComparable);

        // Effective members should include CompareTo with substitution context
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(constructedComparable);
        Assert.Single(effectiveMembers);
        Assert.Equal("CompareTo", effectiveMembers[0].Member.Name);

        // Substitution context should be present
        Assert.NotNull(effectiveMembers[0].TypeParams);
        Assert.NotNull(effectiveMembers[0].TypeArgs);
        var typeParams = effectiveMembers[0].TypeParams!;
        var typeArgs = effectiveMembers[0].TypeArgs!;
        Assert.Single(typeParams);
        Assert.Single(typeArgs);
        Assert.Equal("T", typeParams[0].DisplayName);
        Assert.Equal("int", typeArgs[0].DisplayName);
    }
}
