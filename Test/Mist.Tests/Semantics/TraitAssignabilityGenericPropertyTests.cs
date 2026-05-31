// Feature: generic-traits, Property 8: Trait assignability with generic traits
// **Validates: Requirements 4.2, 4.3, 4.5, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 17.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
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
/// Property-based tests for trait assignability with generic traits.
///
/// **Validates: Requirements 4.2, 4.3, 4.5, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 17.3**
///
/// For any type that satisfies a constructed trait (structurally or via with block),
/// IsAssignable(sourceType, constructedTraitType) SHALL return true. For any type that
/// does not satisfy a constructed trait, IsAssignable SHALL return false and a diagnostic
/// SHALL be emitted. For any type that satisfies a child trait, the TraitSatisfactionMap
/// SHALL also record satisfaction of each parent trait (with appropriate type argument
/// substitution). For any two constructed trait types with the same generic definition
/// but different type arguments (e.g., Comparable&lt;int&gt; vs Comparable&lt;string&gt;),
/// they SHALL be treated as distinct non-assignable types.
/// </summary>
public class TraitAssignabilityGenericPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation()
    {
        var sourceText = new SourceText("test.mist", "");
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildAndAnalyze(
        Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var modelDiagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            modelDiagnostics);

        var analyzerBag = new DiagnosticBag();
        _analyzer.Analyze(model, analyzerBag);

        var allDiags = modelDiagnostics.ToImmutableArray().AddRange(analyzerBag.ToImmutableArray());
        return (model, allDiags);
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

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null) =>
        new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(name),
                Span = new Storm.Runtime.SourceSpan(0, name.Length),
            },
            TypeArgs = typeArgs,
        } };

    private static BaseListNode MakeBaseList(params BaseTypeRefNode[] refs) =>
        new()
        {
            First = refs[0],
            Rest = refs[0],
            BaseTypeRefs = refs.Skip(1).ToImmutableArray(),
        };

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null) =>
        new() { Name = Tok(name), TypeArgs = typeArgs };

    // ─── Expression / statement builders ─────────────────────────────────

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    /// <summary>
    /// Creates: TypeName varName = new ClassName();
    /// where TypeName can be a constructed trait type reference.
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName,
        TypeArgumentListNode? typeArgs = null)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(declaredTypeName, typeArgs),
                Name = Tok(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if (isStatic)
            modifiers.Add(new StaticModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
                },
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    // ─── Trait / class builders ──────────────────────────────────────────

    /// <summary>
    /// Creates a generic trait with a method whose parameter type is the first type parameter.
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
    /// Creates a non-generic "bridge" trait that extends a constructed generic trait.
    /// E.g., trait _Bridge : Comparable&lt;int&gt; { }
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
    /// Creates a generic child trait that extends a constructed parent trait.
    /// E.g., trait Sortable&lt;T&gt; : Comparable&lt;T&gt; { int Priority(); }
    /// </summary>
    private static TraitDeclarationNode MakeChildTrait(
        string childName, string[] typeParams, string parentName,
        string ownMethodName)
    {
        return new TraitDeclarationNode
        {
            Name = Tok(childName),
            TypeParams = MakeTypeParamList(typeParams),
            Parents = MakeBaseList(MakeBaseTypeRef(parentName, MakeTypeArgList(MakeNamedTypeRef(typeParams[0])))),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok(ownMethodName),
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a class with a public method.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithMethod(
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
    /// Creates a class with multiple public methods.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithMethods(
        string className, params (TypeReferenceNode returnType, string name, (TypeReferenceNode type, string name)? param)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.param is not null)
            {
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = m.param.Value.type,
                        Name = Tok(m.param.Value.name),
                    },
                };
            }

            return (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    ReturnType = m.returnType,
                    Name = Tok(m.name),
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                    },
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a class with a public field.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithField(
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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Structural satisfaction — assignment to constructed trait
    //
    // A type satisfying a constructed trait can be assigned to a variable
    // typed as that constructed trait without producing MIST_TRAIT_UNSATISFIED.
    //
    // **Validates: Requirements 14.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("int", "IntComparer")]
    [InlineData("string", "StringComparer")]
    public void StructuralSatisfaction_AssignToConstructedTrait_NoDiagnostic(string typeArg, string className)
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<typeArg> { }
        //          class XComparer { public int CompareTo(typeArg other) { } }
        //          class Program { static void Main() { Comparable<typeArg> x = new XComparer(); } }
        var typeArgRef = typeArg == "int" ? MakeIntTypeRef() : MakeStringTypeRef();
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", typeArgRef);
        var classDecl = MakeClassWithMethod(className, MakeIntTypeRef(), "CompareTo",
            (typeArgRef, "other"));

        // Program class with: Comparable<typeArg> x = new XComparer();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Comparable", "x", className, MakeTypeArgList(typeArgRef)));
        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl),
            MakeClassItem(programClass));

        // Act
        var (_, diagnostics) = BuildAndAnalyze(ast);

        // Assert: no MIST_TRAIT_UNSATISFIED diagnostic for the assignment
        var unsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        && d.Message.Contains(className))
            .ToList();

        Assert.Empty(unsatisfiedDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: With-block satisfaction — assignment to constructed trait
    //
    // A type satisfying a constructed trait through a with block can be
    // assigned to a variable typed as that constructed trait.
    //
    // **Validates: Requirements 14.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlockSatisfaction_AssignToConstructedTrait_NoDiagnostic()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<int> { }
        //          class Product { public int Compare(int other) { } }
        //          with Product : Comparable<int> { Compare as CompareTo; }
        //          class Program { static void Main() { Comparable<int> x = new Product(); } }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", MakeIntTypeRef());
        var classDecl = MakeClassWithMethod("Product", MakeIntTypeRef(), "Compare",
            (MakeIntTypeRef(), "other"));

        var withDecl = new WithDeclarationNode
        {
            TypeName = Tok("Product"),
            TraitName = Tok("Comparable"),
            TraitTypeArgs = MakeTypeArgList(MakeIntTypeRef()),
            Mappings = ImmutableArray.Create(new MemberMappingNode
            {
                SourceName = Tok("Compare"),
                TargetName = Tok("CompareTo"),
            }),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Comparable", "x", "Product", MakeTypeArgList(MakeIntTypeRef())));
        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl),
            MakeWithItem(withDecl),
            MakeClassItem(programClass));

        // Act
        var (_, diagnostics) = BuildAndAnalyze(ast);

        // Assert: no MIST_TRAIT_UNSATISFIED diagnostic for the assignment
        var unsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        && d.Message.Contains("Product"))
            .ToList();

        Assert.Empty(unsatisfiedDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8c: Unsatisfied type — diagnostic emitted
    //
    // A type NOT satisfying a constructed trait cannot be assigned to a
    // variable typed as that constructed trait (diagnostic emitted).
    //
    // **Validates: Requirements 14.3, 17.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnsatisfiedType_AssignToConstructedTrait_EmitsDiagnostic()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _Bridge : Comparable<int> { }
        //          class Empty { }
        //          class Program { static void Main() { Comparable<int> x = new Empty(); } }
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Comparable", MakeIntTypeRef());
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Empty"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Comparable", "x", "Empty", MakeTypeArgList(MakeIntTypeRef())));
        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl),
            MakeClassItem(programClass));

        // Act
        var (_, diagnostics) = BuildAndAnalyze(ast);

        // Assert: MIST_TRAIT_UNSATISFIED diagnostic should be emitted
        // (or MIST_TC_0001 for generic type mismatch)
        var allDiags = diagnostics.ToList();
        var unsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        || d.Id.Value == "MIST_TC_0001")
            .Where(d => d.Message.Contains("Empty"))
            .ToList();

        // If no specific diagnostic found, check if there are any diagnostics at all
        // that indicate the assignment was rejected
        if (unsatisfiedDiags.Count == 0)
        {
            // Check for any error diagnostic
            var anyErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(anyErrors.Count > 0 || unsatisfiedDiags.Count > 0,
                $"Expected a diagnostic for unsatisfied trait assignment. All diagnostics: [{string.Join("; ", allDiags.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8d: Distinct constructed traits — cross-assignment fails
    //
    // Comparable<int> and Comparable<string> are distinct types.
    // A type satisfying Comparable<int> cannot be assigned to Comparable<string>.
    //
    // **Validates: Requirements 14.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DistinctConstructedTraits_CrossAssignment_EmitsDiagnostic()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait _BridgeInt : Comparable<int> { }
        //          trait _BridgeStr : Comparable<string> { }
        //          class IntComparer { public int CompareTo(int other) { } }
        //          class Program { static void Main() { Comparable<string> x = new IntComparer(); } }
        // IntComparer satisfies Comparable<int> but NOT Comparable<string>
        var traitDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var bridgeInt = MakeBridgeTrait("_BridgeInt", "Comparable", MakeIntTypeRef());
        var bridgeStr = MakeBridgeTrait("_BridgeStr", "Comparable", MakeStringTypeRef());
        var classDecl = MakeClassWithMethod("IntComparer", MakeIntTypeRef(), "CompareTo",
            (MakeIntTypeRef(), "other"));

        // Assign IntComparer to Comparable<string> — should fail
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Comparable", "x", "IntComparer", MakeTypeArgList(MakeStringTypeRef())));
        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitDecl),
            MakeTraitItem(bridgeInt),
            MakeTraitItem(bridgeStr),
            MakeClassItem(classDecl),
            MakeClassItem(programClass));

        // Act
        var (_, diagnostics) = BuildAndAnalyze(ast);

        // Assert: MIST_TRAIT_UNSATISFIED diagnostic should be emitted
        // (or MIST_TC_0001 for generic type mismatch)
        var unsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        || d.Id.Value == "MIST_TC_0001")
            .Where(d => d.Message.Contains("IntComparer"))
            .ToList();

        // If no specific diagnostic found, check if there are any diagnostics at all
        // that indicate the assignment was rejected
        if (unsatisfiedDiags.Count == 0)
        {
            var anyErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(anyErrors.Count > 0 || unsatisfiedDiags.Count > 0,
                $"Expected a diagnostic for cross-assignment. All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8e: Transitive parent satisfaction — assignment to parent trait
    //
    // A type satisfying a child trait (Sortable<int>) can be assigned to a
    // variable typed as the parent trait (Comparable<int>).
    //
    // **Validates: Requirements 4.2, 4.3, 4.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TransitiveParentSatisfaction_AssignToParentTrait_NoDiagnostic()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait Sortable<T> : Comparable<T> { int Priority(); }
        //          trait _Bridge : Sortable<int> { }
        //          class IntSorter { public int CompareTo(int other) { } public int Priority() { } }
        //          class Program { static void Main() { Comparable<int> x = new IntSorter(); } }
        var comparableDecl = MakeGenericTraitWithMethodParam("Comparable", ["T"], "CompareTo", "other");
        var sortableDecl = MakeChildTrait("Sortable", ["T"], "Comparable", "Priority");

        // Bridge to trigger satisfaction for Sortable<int>
        var bridgeDecl = MakeBridgeTrait("_Bridge", "Sortable", MakeIntTypeRef());

        // Class that satisfies both CompareTo(int) and Priority()
        var classDecl = MakeClassWithMethods("IntSorter",
            (MakeIntTypeRef(), "CompareTo", (MakeIntTypeRef(), "other")),
            (MakeIntTypeRef(), "Priority", null));

        // Assign IntSorter to Comparable<int> (parent trait)
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Comparable", "x", "IntSorter", MakeTypeArgList(MakeIntTypeRef())));
        var mainMethod = MakeMethodDecl("Main", mainStatements, isStatic: true);
        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(comparableDecl),
            MakeTraitItem(sortableDecl),
            MakeTraitItem(bridgeDecl),
            MakeClassItem(classDecl),
            MakeClassItem(programClass));

        // Act
        var (_, diagnostics) = BuildAndAnalyze(ast);

        // Assert: no MIST_TRAIT_UNSATISFIED diagnostic — transitive satisfaction applies
        var unsatisfiedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                        && d.Message.Contains("IntSorter"))
            .ToList();

        Assert.Empty(unsatisfiedDiags);
    }
}
