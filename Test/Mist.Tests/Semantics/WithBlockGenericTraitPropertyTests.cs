// Feature: generic-traits, Property 7: With block validation for generic traits
// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 17.5**

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for with block validation for generic traits.
///
/// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 17.5**
///
/// Property 7: When a with block references a generic trait with type arguments,
/// the semantic model builder resolves the trait to a ConstructedTraitType and validates
/// member mappings against the substituted member signatures. When a member mapping maps
/// a source member to a target trait member whose type involves a type parameter, the
/// semantic model builder checks type compatibility using the substituted type. When a
/// with block provides valid mappings for all unmapped trait members (after type argument
/// substitution), the semantic model builder registers the type as satisfying the
/// constructed trait. When a with block does not cover all trait members after substitution,
/// the semantic model builder emits a diagnostic error listing the unsatisfied trait members
/// with their substituted types.
/// </summary>
public class WithBlockGenericTraitPropertyTests
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

    private static TopLevelItemNode MakeWithItem(WithDeclarationNode withDecl) =>
        new WithItemTopLevelItemNode { WithDeclaration = withDecl };

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
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
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };

    private static TypeReferenceNode MakeIntTypeRef() =>
        new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } };

    private static TypeReferenceNode MakeStringTypeRef() =>
        new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } };

    private static TypeReferenceNode MakeBoolTypeRef() =>
        new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } };

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(name),
                IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
            },
        } };

    /// <summary>
    /// Creates a generic trait with a field member whose type is the first type parameter.
    /// E.g., trait Comparable&lt;T&gt; { T value; }
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
    /// Creates a generic trait with a method member whose parameter type is the first type parameter
    /// and return type is int.
    /// E.g., trait Comparable&lt;T&gt; { int CompareTo(T other); }
    /// </summary>
    private static TraitDeclarationNode MakeGenericTraitWithMethod(
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
    /// Creates a class with a public method.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicMethod(
        string className, string returnType, string methodName,
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
                        ReturnType = returnType == "int" ? MakeIntTypeRef() :
                                     returnType == "string" ? MakeStringTypeRef() :
                                     MakeBoolTypeRef(),
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

    /// <summary>
    /// Creates a WithDeclarationNode with type arguments on the trait reference.
    /// </summary>
    private static WithDeclarationNode MakeGenericWithDeclaration(
        string typeName, string traitName, TypeArgumentListNode typeArgs,
        params (string source, string target)[] mappings)
    {
        return new WithDeclarationNode
        {
            TypeName = Tok(typeName),
            TraitName = Tok(traitName),
            TraitTypeArgs = typeArgs,
            Mappings = mappings.Select(m => new MemberMappingNode
            {
                SourceName = Tok(m.source),
                TargetName = Tok(m.target),
            }).ToImmutableArray(),
        };
    }

    /// <summary>
    /// Creates a WithDeclarationNode without type arguments (non-generic).
    /// </summary>
    private static WithDeclarationNode MakeWithDeclaration(
        string typeName, string traitName,
        params (string source, string target)[] mappings)
    {
        return new WithDeclarationNode
        {
            TypeName = Tok(typeName),
            TraitName = Tok(traitName),
            Mappings = mappings.Select(m => new MemberMappingNode
            {
                SourceName = Tok(m.source),
                TargetName = Tok(m.target),
            }).ToImmutableArray(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Valid with block mapping with type argument substitution
    //
    // When a with block references a generic trait with correct type arguments
    // and provides valid mappings for all trait members (after substitution),
    // the semantic model builder SHALL register the type as satisfying the
    // constructed trait with no with-block-related diagnostics.
    //
    // **Validates: Requirements 13.1, 13.2, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_GenericTrait_ValidMethodMapping_NoDiagnostics()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          class MyType { public int Compare(int other) { } }
        //          with MyType : Comparable<int> { Compare as CompareTo; }
        var traitDecl = MakeGenericTraitWithMethod("Comparable", ["T"], "CompareTo", "other");
        var classDecl = MakeClassWithPublicMethod("MyType", "int", "Compare",
            (MakeIntTypeRef(), "other"));
        var withDecl = MakeGenericWithDeclaration("MyType", "Comparable",
            MakeTypeArgList(MakeIntTypeRef()),
            ("Compare", "CompareTo"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no with-block-related diagnostics
        var withDiags = diagnostics.Where(d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_") ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();

        Assert.Empty(withDiags);
    }

    [Fact]
    public void WithBlock_GenericTrait_ValidFieldMapping_NoDiagnostics()
    {
        // Arrange: trait Container<T> { T value; }
        //          class MyType { public int data; }
        //          with MyType : Container<int> { data as value; }
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var classDecl = MakeClassWithPublicField("MyType", MakeIntTypeRef(), "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Container",
            MakeTypeArgList(MakeIntTypeRef()),
            ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no with-block-related diagnostics
        var withDiags = diagnostics.Where(d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_") ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();

        Assert.Empty(withDiags);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    public void WithBlock_GenericTrait_ValidFieldMapping_VariousTypeArgs_NoDiagnostics(string typeArgName)
    {
        // Arrange: trait Container<T> { T value; }
        //          class MyType { public <typeArgName> data; }
        //          with MyType : Container<typeArgName> { data as value; }
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");

        TypeReferenceNode typeRef = typeArgName switch
        {
            "int" => MakeIntTypeRef(),
            "string" => MakeStringTypeRef(),
            "bool" => MakeBoolTypeRef(),
            _ => MakeIntTypeRef(),
        };

        var classDecl = MakeClassWithPublicField("MyType", typeRef, "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Container",
            MakeTypeArgList(typeRef),
            ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no with-block-related diagnostics
        var withDiags = diagnostics.Where(d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_") ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();

        Assert.Empty(withDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Type mismatch after substitution
    //
    // When a member mapping maps a source member to a target trait member
    // whose type involves a type parameter, and the source member's type
    // does not match the substituted type, the semantic model builder SHALL
    // emit a MIST_TRAIT_WITH_TYPE_MISMATCH diagnostic.
    //
    // **Validates: Requirements 13.2, 17.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_GenericTrait_ReturnTypeMismatchAfterSubstitution_EmitsDiagnostic()
    {
        // Arrange: trait Returner<T> { T GetValue(); }
        //          class MyType { public string Fetch() { } }
        //          with MyType : Returner<int> { Fetch as GetValue; }
        // After substitution T=int, trait expects int return type, but class returns string.
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Returner"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeNamedTypeRef("T"),
                        Name = Tok("GetValue"),
                    },
                }),
        };

        var classDecl = MakeClassWithPublicMethod("MyType", "string", "Fetch");
        var withDecl = MakeGenericWithDeclaration("MyType", "Returner",
            MakeTypeArgList(MakeIntTypeRef()),
            ("Fetch", "GetValue"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: type mismatch diagnostic
        var mismatchDiags = diagnostics.Where(d =>
            d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH").ToList();

        Assert.Single(mismatchDiags);
        Assert.Contains("MyType", mismatchDiags[0].Message);
        Assert.Contains("Fetch", mismatchDiags[0].Message);
    }

    [Fact]
    public void WithBlock_GenericTrait_FieldTypeMismatchAfterSubstitution_EmitsDiagnostic()
    {
        // Arrange: trait Container<T> { T value; }
        //          class MyType { public string data; }
        //          with MyType : Container<int> { data as value; }
        // After substitution T=int, trait expects int field, but class has string field.
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var classDecl = MakeClassWithPublicField("MyType", MakeStringTypeRef(), "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Container",
            MakeTypeArgList(MakeIntTypeRef()),
            ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: type mismatch diagnostic
        var mismatchDiags = diagnostics.Where(d =>
            d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH").ToList();

        Assert.Single(mismatchDiags);
        Assert.Contains("MyType", mismatchDiags[0].Message);
        Assert.Contains("data", mismatchDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7c: Missing type arguments on with block trait reference
    //
    // When a with block references a generic trait without providing type
    // arguments, the semantic model builder SHALL emit MIST_MISSING_TYPE_ARGUMENTS.
    //
    // **Validates: Requirements 13.1, 17.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WithBlock_GenericTrait_MissingTypeArgs_EmitsDiagnostic(int typeParamCount)
    {
        // Arrange: generic trait with N type params, with block references it without type args
        var typeParams = Enumerable.Range(0, typeParamCount)
            .Select(i => $"T{i}")
            .ToArray();

        var traitDecl = MakeGenericTraitWithField("GenericTrait", typeParams, "value");
        var classDecl = MakeClassWithPublicField("MyType", MakeIntTypeRef(), "data");

        // With block without type arguments (non-generic reference to a generic trait)
        var withDecl = MakeWithDeclaration("MyType", "GenericTrait", ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: missing type arguments diagnostic
        var missingArgsDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.MissingTypeArguments)
            .ToList();

        Assert.Single(missingArgsDiags);
        Assert.Contains("GenericTrait", missingArgsDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7d: Arity mismatch on with block trait reference
    //
    // When a with block references a generic trait with the wrong number of
    // type arguments, the semantic model builder SHALL emit MIST_ARITY_MISMATCH.
    //
    // **Validates: Requirements 13.1, 17.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 2)]  // trait has 1 param, provided 2 args
    [InlineData(2, 1)]  // trait has 2 params, provided 1 arg
    [InlineData(2, 3)]  // trait has 2 params, provided 3 args
    [InlineData(3, 1)]  // trait has 3 params, provided 1 arg
    public void WithBlock_GenericTrait_ArityMismatch_EmitsDiagnostic(int declaredCount, int providedCount)
    {
        // Arrange: generic trait with declaredCount type params,
        // with block provides providedCount type args
        var typeParams = Enumerable.Range(0, declaredCount)
            .Select(i => $"T{i}")
            .ToArray();

        var traitDecl = MakeGenericTraitWithField("GenericTrait", typeParams, "value");
        var classDecl = MakeClassWithPublicField("MyType", MakeIntTypeRef(), "data");

        // Build type argument list with providedCount primitive type refs
        var typeArgs = Enumerable.Range(0, providedCount)
            .Select(_ => MakeIntTypeRef())
            .ToArray();

        var withDecl = MakeGenericWithDeclaration("MyType", "GenericTrait",
            MakeTypeArgList(typeArgs),
            ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: arity mismatch diagnostic
        var arityDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.ArityMismatch)
            .ToList();

        Assert.Single(arityDiags);
        Assert.Contains("GenericTrait", arityDiags[0].Message);
        Assert.Contains(declaredCount.ToString(), arityDiags[0].Message);
        Assert.Contains(providedCount.ToString(), arityDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7e: Correct arity with valid mapping — no diagnostics
    //
    // When a with block references a generic trait with the correct number
    // of type arguments and valid mappings, no arity or missing type args
    // diagnostics are emitted.
    //
    // **Validates: Requirements 13.1, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_GenericTrait_CorrectArity_NoArityDiagnostic()
    {
        // Arrange: trait Container<T> { T value; } with correct type arg
        var traitDecl = MakeGenericTraitWithField("Container", ["T"], "value");
        var classDecl = MakeClassWithPublicField("MyType", MakeStringTypeRef(), "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Container",
            MakeTypeArgList(MakeStringTypeRef()),
            ("data", "value"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no arity or missing type args diagnostics
        var arityDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.ArityMismatch)
            .ToList();
        Assert.Empty(arityDiags);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.MissingTypeArguments)
            .ToList();
        Assert.Empty(missingArgsDiags);

        // Also no type mismatch
        var mismatchDiags = diagnostics.Where(d =>
            d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH").ToList();
        Assert.Empty(mismatchDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7f: Multiple type parameters with substitution
    //
    // When a generic trait has multiple type parameters and the with block
    // provides correct type arguments, member mappings are validated against
    // the fully substituted signatures.
    //
    // **Validates: Requirements 13.1, 13.2, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_GenericTrait_MultipleTypeParams_ValidMapping_NoDiagnostics()
    {
        // Arrange: trait Converter<TIn, TOut> { TIn input; }
        //          class MyType { public int data; }
        //          with MyType : Converter<int, string> { data as input; }
        // The field 'input' has type TIn, which substitutes to int.
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Converter"),
            TypeParams = MakeTypeParamList("TIn", "TOut"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeNamedTypeRef("TIn"),
                        Name = Tok("input"),
                    },
                }),
        };

        var classDecl = MakeClassWithPublicField("MyType", MakeIntTypeRef(), "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Converter",
            MakeTypeArgList(MakeIntTypeRef(), MakeStringTypeRef()),
            ("data", "input"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no with-block-related diagnostics
        var withDiags = diagnostics.Where(d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_") ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();

        Assert.Empty(withDiags);
    }

    [Fact]
    public void WithBlock_GenericTrait_MultipleTypeParams_TypeMismatch_EmitsDiagnostic()
    {
        // Arrange: trait Converter<TIn, TOut> { TIn input; }
        //          class MyType { public string data; }
        //          with MyType : Converter<int, string> { data as input; }
        // The field 'input' has type TIn=int, but class field is string.
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Converter"),
            TypeParams = MakeTypeParamList("TIn", "TOut"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeNamedTypeRef("TIn"),
                        Name = Tok("input"),
                    },
                }),
        };

        var classDecl = MakeClassWithPublicField("MyType", MakeStringTypeRef(), "data");
        var withDecl = MakeGenericWithDeclaration("MyType", "Converter",
            MakeTypeArgList(MakeIntTypeRef(), MakeStringTypeRef()),
            ("data", "input"));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: type mismatch diagnostic
        var mismatchDiags = diagnostics.Where(d =>
            d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH").ToList();

        Assert.Single(mismatchDiags);
        Assert.Contains("MyType", mismatchDiags[0].Message);
        Assert.Contains("data", mismatchDiags[0].Message);
    }
}
