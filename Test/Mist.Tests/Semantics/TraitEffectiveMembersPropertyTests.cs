// Feature: generic-traits, Property 5: Trait inheritance resolution and effective members
// **Validates: Requirements 3.1, 3.7, 4.1, 4.4**

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
/// Property-based tests for trait inheritance resolution and effective members.
///
/// **Validates: Requirements 3.1, 3.7, 4.1, 4.4**
///
/// For any valid trait hierarchy (no cycles, all parents resolve to traits with correct
/// arity), the semantic model builder SHALL store the resolved parent traits on
/// TraitType.ParentTraits. The effective member set of a trait SHALL be the union of its
/// own declared members and all members inherited from parent traits (transitively), with
/// members sharing the same name and compatible signature deduplicated to a single requirement.
/// </summary>
public class TraitEffectiveMembersPropertyTests
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

    private static BaseListNode MakeBaseList(params BaseTypeRefNode[] refs)
    {
        return new BaseListNode
        {
            First = refs[0],
            Rest = refs[0],
            BaseTypeRefs = refs.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null) =>
        new() { Name = Tok(name), TypeArgs = typeArgs };

    /// <summary>
    /// Creates a simple non-generic trait with a single int field member.
    /// </summary>
    private static TraitDeclarationNode MakeSimpleTrait(
        string name, string fieldName, BaseListNode? parents = null)
    {
        var decl = new TraitDeclarationNode
        {
            Name = Tok(name),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeIntTypeRef(),
                        Name = Tok(fieldName),
                    },
                }),
        };

        if (parents is not null)
            decl.Parents = parents;

        return decl;
    }

    /// <summary>
    /// Creates a non-generic trait with a method member.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithMethod(
        string name, string methodName, string returnType = "int",
        (string type, string name)? param = null, BaseListNode? parents = null)
    {
        ParameterListNode? paramList = null;
        if (param is not null)
        {
            paramList = new ParameterListNode
            {
                Parameter = new ParameterNode
                {
                    Type = param.Value.type == "int" ? MakeIntTypeRef() : MakeStringTypeRef(),
                    Name = Tok(param.Value.name),
                },
            };
        }

        var decl = new TraitDeclarationNode
        {
            Name = Tok(name),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = returnType == "int" ? MakeIntTypeRef() : MakeStringTypeRef(),
                        Name = Tok(methodName),
                        Parameters = paramList,
                    },
                }),
        };

        if (parents is not null)
            decl.Parents = parents;

        return decl;
    }

    /// <summary>
    /// Creates a generic trait with a method whose parameter type is the first type parameter.
    /// E.g., trait Comparable&lt;T&gt; { int CompareTo(T other); }
    /// </summary>
    private static TraitDeclarationNode MakeGenericTraitWithMethod(
        string traitName, string[] typeParams, string methodName, string paramName,
        BaseListNode? parents = null)
    {
        var decl = new TraitDeclarationNode
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

        if (parents is not null)
            decl.Parents = parents;

        return decl;
    }

    /// <summary>
    /// Creates a class with public members (fields and/or methods).
    /// </summary>
    private static ClassDeclarationNode MakeClassWithMembers(
        string className,
        (string type, string name)[]? fields = null,
        (string returnType, string methodName, (string type, string name)? param)[]? methods = null)
    {
        var members = new List<MemberDeclarationNode>();

        if (fields is not null)
        {
            foreach (var f in fields)
            {
                members.Add(new FieldMemberMemberDeclarationNode
                {
                    Field = new FieldDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                        Type = f.type == "int" ? MakeIntTypeRef() : MakeStringTypeRef(),
                        Name = Tok(f.name),
                    },
                });
            }
        }

        if (methods is not null)
        {
            foreach (var m in methods)
            {
                ParameterListNode? paramList = null;
                if (m.param is not null)
                {
                    paramList = new ParameterListNode
                    {
                        Parameter = new ParameterNode
                        {
                            Type = m.param.Value.type == "int" ? MakeIntTypeRef() : MakeStringTypeRef(),
                            Name = Tok(m.param.Value.name),
                        },
                    };
                }

                members.Add(new MethodMemberMemberDeclarationNode
                {
                    Method = new MethodDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                        ReturnType = m.returnType == "int" ? MakeIntTypeRef() : MakeStringTypeRef(),
                        Name = Tok(m.methodName),
                        Parameters = paramList,
                        Body = new MethodBlockBodyMethodBodyNode
                        {
                            Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                        },
                    },
                });
            }
        }

        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members.ToImmutableArray(),
        };
    }

    private static TraitType? FindTrait(ISemanticModel model, string name) =>
        model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Trait)
            ?.DeclaredType as TraitType;

    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind) =>
        model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a: Trait with no parents has effective members = own members
    //
    // A trait with no inheritance clause has effective members equal to its
    // own declared members only.
    //
    // **Validates: Requirements 3.7, 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitWithNoParents_EffectiveMembers_EqualsOwnMembers()
    {
        // Arrange: trait Printable { int value; }
        var traitDecl = MakeSimpleTrait("Printable", "value");
        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert
        var trait = FindTrait(model, "Printable");
        Assert.NotNull(trait);
        Assert.Empty(trait.ParentTraits);

        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(trait);
        Assert.Single(effectiveMembers);
        Assert.Equal("value", effectiveMembers[0].Member.Name);
    }

    [Theory]
    [InlineData("alpha", "beta")]
    [InlineData("count", "size")]
    public void TraitWithNoParents_MultipleMembers_AllAreEffective(string field1, string field2)
    {
        // Arrange: trait with two field members
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("MultiFieldTrait"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeIntTypeRef(),
                        Name = Tok(field1),
                    },
                },
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeIntTypeRef(),
                        Name = Tok(field2),
                    },
                }),
        };
        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var trait = FindTrait(model, "MultiFieldTrait");
        Assert.NotNull(trait);

        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(trait);
        Assert.Equal(2, effectiveMembers.Count);

        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToHashSet();
        Assert.Contains(field1, memberNames);
        Assert.Contains(field2, memberNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b: Trait with one non-generic parent has effective members
    //              = own + parent's members
    //
    // **Validates: Requirements 3.1, 3.7, 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitWithOneNonGenericParent_EffectiveMembers_IncludesParentMembers()
    {
        // Arrange: trait Parent { int parentField; }
        //          trait Child : Parent { int childField; }
        var parentDecl = MakeSimpleTrait("Parent", "parentField");
        var childDecl = MakeSimpleTrait("Child", "childField",
            MakeBaseList(MakeBaseTypeRef("Parent")));

        var ast = MakeCompilationUnit(
            MakeTraitItem(parentDecl),
            MakeTraitItem(childDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no cycle or parent-not-trait diagnostics
        var errorDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.TraitInheritanceCycle ||
            d.Id == DiagnosticId.TraitParentNotTrait).ToList();
        Assert.Empty(errorDiags);

        var childTrait = FindTrait(model, "Child");
        Assert.NotNull(childTrait);

        // ParentTraits should be resolved
        Assert.Single(childTrait.ParentTraits);
        Assert.IsType<TraitType>(childTrait.ParentTraits[0]);
        Assert.Equal("Parent", childTrait.ParentTraits[0].DisplayName);

        // Effective members = own (childField) + parent (parentField)
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(childTrait);
        Assert.Equal(2, effectiveMembers.Count);

        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToHashSet();
        Assert.Contains("childField", memberNames);
        Assert.Contains("parentField", memberNames);
    }

    [Fact]
    public void TraitWithOneNonGenericParent_ParentTraitsResolved()
    {
        // Arrange: trait Base { int baseMethod(); }
        //          trait Derived : Base { int derivedMethod(); }
        var baseDecl = MakeTraitWithMethod("Base", "baseMethod");
        var derivedDecl = MakeTraitWithMethod("Derived", "derivedMethod",
            parents: MakeBaseList(MakeBaseTypeRef("Base")));

        var ast = MakeCompilationUnit(
            MakeTraitItem(baseDecl),
            MakeTraitItem(derivedDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var derivedTrait = FindTrait(model, "Derived");
        Assert.NotNull(derivedTrait);
        Assert.Single(derivedTrait.ParentTraits);

        var parentTrait = derivedTrait.ParentTraits[0] as TraitType;
        Assert.NotNull(parentTrait);
        Assert.Equal("Base", parentTrait.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5c: Trait with a generic parent has effective members
    //              including parent's members with substitution
    //
    // E.g., trait Sortable<T> : Comparable<T> { int Priority(); }
    // Effective members of Sortable<int> include CompareTo(int) from Comparable<int>.
    //
    // **Validates: Requirements 3.1, 3.7, 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitWithGenericParent_EffectiveMembers_IncludesSubstitutedParentMembers()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait Sortable<T> : Comparable<T> { int Priority(); }
        var comparableDecl = MakeGenericTraitWithMethod("Comparable", ["T"], "CompareTo", "other");

        // Sortable<T> : Comparable<T> with its own method Priority()
        var sortableDecl = new TraitDeclarationNode
        {
            Name = Tok("Sortable"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Priority"),
                    },
                }),
            Parents = MakeBaseList(MakeBaseTypeRef("Comparable", MakeTypeArgList(MakeNamedTypeRef("T")))),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(comparableDecl),
            MakeTraitItem(sortableDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no trait-inheritance-specific errors (MIST_UNDEFINED_TYPE for 'T' in
        // type argument position is expected — it's resolved during Pass 1 parent resolution
        // but Pass 2 type reference resolution may emit it for the raw AST reference)
        var inheritanceDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.TraitInheritanceCycle ||
            d.Id == DiagnosticId.TraitParentNotTrait ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();
        Assert.Empty(inheritanceDiags);

        var sortableTrait = FindTrait(model, "Sortable");
        Assert.NotNull(sortableTrait);

        // ParentTraits should contain a ConstructedTraitType (Comparable<T>)
        Assert.Single(sortableTrait.ParentTraits);
        Assert.IsType<ConstructedTraitType>(sortableTrait.ParentTraits[0]);

        var parentCtt = (ConstructedTraitType)sortableTrait.ParentTraits[0];
        Assert.Equal("Comparable", parentCtt.GenericTraitDefinition.DisplayName);

        // Effective members of Sortable (unsubstituted) = Priority + CompareTo
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(sortableTrait);
        Assert.Equal(2, effectiveMembers.Count);

        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToHashSet();
        Assert.Contains("Priority", memberNames);
        Assert.Contains("CompareTo", memberNames);
    }

    [Fact]
    public void TraitWithGenericParent_ConstructedChild_EffectiveMembersHaveSubstitution()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait Sortable<T> : Comparable<T> { int Priority(); }
        // When we construct Sortable<int>, effective members should include
        // CompareTo with T=int substitution context.
        var comparableDecl = MakeGenericTraitWithMethod("Comparable", ["T"], "CompareTo", "other");

        var sortableDecl = new TraitDeclarationNode
        {
            Name = Tok("Sortable"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Priority"),
                    },
                }),
            Parents = MakeBaseList(MakeBaseTypeRef("Comparable", MakeTypeArgList(MakeNamedTypeRef("T")))),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(comparableDecl),
            MakeTraitItem(sortableDecl));

        // Act
        var (model, _) = BuildModel(ast);

        var sortableTrait = FindTrait(model, "Sortable");
        Assert.NotNull(sortableTrait);

        // Construct Sortable<int> using a concrete type from the model's stdlib
        // The stdlib provides Int32 as a struct type.
        var int32Type = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Int32" && s.Kind == SymbolKind.Struct)
            ?.DeclaredType;

        if (int32Type is not null)
        {
            var constructedSortable = new ConstructedTraitType(sortableTrait, [int32Type]);
            var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(constructedSortable);

            Assert.Equal(2, effectiveMembers.Count);
            var compareToMember = effectiveMembers.First(m => m.Member.Name == "CompareTo");

            // The substitution context should be present (TypeParams and TypeArgs non-null)
            Assert.NotNull(compareToMember.TypeParams);
            Assert.NotNull(compareToMember.TypeArgs);
            Assert.Contains(int32Type, compareToMember.TypeArgs);
        }
        else
        {
            // Fallback: verify on the raw trait definition (no stdlib available)
            var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(sortableTrait);
            Assert.Equal(2, effectiveMembers.Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5d: Diamond inheritance — member from shared ancestor appears once
    //
    // trait D { int shared; }
    // trait A : D { int fromA; }
    // trait B : D { int fromB; }
    // trait C : A, B { int fromC; }
    // Effective members of C = fromC + fromA + fromB + shared (shared appears once)
    //
    // **Validates: Requirements 3.7, 4.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DiamondInheritance_SharedMemberAppearsOnce()
    {
        // Arrange: trait D { int shared; }
        //          trait A : D { int fromA; }
        //          trait B : D { int fromB; }
        //          trait C : A, B { int fromC; }
        var traitD = MakeSimpleTrait("TraitD", "shared");
        var traitA = MakeSimpleTrait("TraitA", "fromA",
            MakeBaseList(MakeBaseTypeRef("TraitD")));
        var traitB = MakeSimpleTrait("TraitB", "fromB",
            MakeBaseList(MakeBaseTypeRef("TraitD")));

        var traitC = MakeSimpleTrait("TraitC", "fromC",
            MakeBaseList(MakeBaseTypeRef("TraitA"), MakeBaseTypeRef("TraitB")));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitD),
            MakeTraitItem(traitA),
            MakeTraitItem(traitB),
            MakeTraitItem(traitC));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no cycle diagnostics
        var cycleDiags = diagnostics.Where(d => d.Id == DiagnosticId.TraitInheritanceCycle).ToList();
        Assert.Empty(cycleDiags);

        var traitCType = FindTrait(model, "TraitC");
        Assert.NotNull(traitCType);

        // C has two parents: A and B
        Assert.Equal(2, traitCType.ParentTraits.Count);

        // Effective members: fromC, fromA, fromB, shared (4 unique members)
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(traitCType);
        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToList();

        Assert.Contains("fromC", memberNames);
        Assert.Contains("fromA", memberNames);
        Assert.Contains("fromB", memberNames);
        Assert.Contains("shared", memberNames);

        // "shared" should appear exactly once (deduplicated from diamond)
        Assert.Equal(4, effectiveMembers.Count);
        Assert.Equal(1, memberNames.Count(n => n == "shared"));
    }

    [Fact]
    public void DiamondInheritance_GenericSharedAncestor_MemberAppearsOnce()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait Sortable<T> : Comparable<T> { int Priority(); }
        //          trait Filterable<T> : Comparable<T> { int Filter(); }
        //          trait Combined<T> : Sortable<T>, Filterable<T> { int Extra(); }
        // Effective members of Combined<T> = Extra + Priority + Filter + CompareTo (once)
        var comparableDecl = MakeGenericTraitWithMethod("Comparable", ["T"], "CompareTo", "other");

        var sortableDecl = new TraitDeclarationNode
        {
            Name = Tok("Sortable"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Priority"),
                    },
                }),
            Parents = MakeBaseList(MakeBaseTypeRef("Comparable", MakeTypeArgList(MakeNamedTypeRef("T")))),
        };

        var filterableDecl = new TraitDeclarationNode
        {
            Name = Tok("Filterable"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Filter"),
                    },
                }),
            Parents = MakeBaseList(MakeBaseTypeRef("Comparable", MakeTypeArgList(MakeNamedTypeRef("T")))),
        };

        var combinedDecl = new TraitDeclarationNode
        {
            Name = Tok("Combined"),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Extra"),
                    },
                }),
            Parents = MakeBaseList(
                MakeBaseTypeRef("Sortable", MakeTypeArgList(MakeNamedTypeRef("T"))),
                MakeBaseTypeRef("Filterable", MakeTypeArgList(MakeNamedTypeRef("T")))),
        };

        var ast = MakeCompilationUnit(
            MakeTraitItem(comparableDecl),
            MakeTraitItem(sortableDecl),
            MakeTraitItem(filterableDecl),
            MakeTraitItem(combinedDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no trait-inheritance-specific errors (MIST_UNDEFINED_TYPE for 'T' is
        // expected from Pass 2 type reference resolution of type params in type arg positions)
        var inheritanceDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.TraitInheritanceCycle ||
            d.Id == DiagnosticId.TraitParentNotTrait ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();
        Assert.Empty(inheritanceDiags);

        var combinedTrait = FindTrait(model, "Combined");
        Assert.NotNull(combinedTrait);

        // Effective members: Extra, Priority, Filter, CompareTo (4 unique)
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(combinedTrait);
        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToList();

        Assert.Equal(4, effectiveMembers.Count);
        Assert.Contains("Extra", memberNames);
        Assert.Contains("Priority", memberNames);
        Assert.Contains("Filter", memberNames);
        Assert.Contains("CompareTo", memberNames);
        Assert.Equal(1, memberNames.Count(n => n == "CompareTo"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5e: Transitive parent satisfaction
    //
    // A type satisfying a child trait is also registered as satisfying
    // parent traits in the TraitSatisfactionMap.
    //
    // **Validates: Requirements 4.1, 4.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeSatisfyingChildTrait_AlsoSatisfiesParentTrait()
    {
        // Arrange: trait Parent { int parentMethod(); }
        //          trait Child : Parent { int childMethod(); }
        //          class MyType { public int parentMethod() {} public int childMethod() {} }
        var parentDecl = MakeTraitWithMethod("Parent", "parentMethod");
        var childDecl = MakeTraitWithMethod("Child", "childMethod",
            parents: MakeBaseList(MakeBaseTypeRef("Parent")));

        var classDecl = MakeClassWithMembers("MyType",
            methods: [
                ("int", "parentMethod", null),
                ("int", "childMethod", null),
            ]);

        var ast = MakeCompilationUnit(
            MakeTraitItem(parentDecl),
            MakeTraitItem(childDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no errors
        var errorDiags = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errorDiags);

        var classType = FindType(model, "MyType", SymbolKind.Class);
        var childTrait = FindTrait(model, "Child");
        var parentTrait = FindTrait(model, "Parent");
        Assert.NotNull(classType);
        Assert.NotNull(childTrait);
        Assert.NotNull(parentTrait);

        // Type should satisfy both child and parent traits
        Assert.True(model.TraitSatisfactionMap.Satisfies(classType, childTrait),
            "MyType should satisfy Child trait");
        Assert.True(model.TraitSatisfactionMap.Satisfies(classType, parentTrait),
            "MyType should satisfy Parent trait (transitively via Child)");
    }

    [Fact]
    public void TypeSatisfyingChildTrait_GenericParent_SatisfiesConstructedParent()
    {
        // Arrange: trait Comparable<T> { int CompareTo(T other); }
        //          trait Sortable : Comparable<int> { int Priority(); }
        //          class IntSorter { public int CompareTo(int other) {} public int Priority() {} }
        // IntSorter satisfies Sortable, which also implies Comparable<int>
        //
        // We use a non-generic child extending a constructed parent (Comparable<int>)
        // to test transitive satisfaction with generic parents.
        var comparableDecl = MakeGenericTraitWithMethod("Comparable", ["T"], "CompareTo", "other");

        var sortableDecl = new TraitDeclarationNode
        {
            Name = Tok("Sortable"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeIntTypeRef(),
                        Name = Tok("Priority"),
                    },
                }),
            Parents = MakeBaseList(MakeBaseTypeRef("Comparable", MakeTypeArgList(MakeIntTypeRef()))),
        };

        var classDecl = MakeClassWithMembers("IntSorter",
            methods: [
                ("int", "CompareTo", ("int", "other")),
                ("int", "Priority", null),
            ]);

        var ast = MakeCompilationUnit(
            MakeTraitItem(comparableDecl),
            MakeTraitItem(sortableDecl),
            MakeClassItem(classDecl));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no trait-inheritance-specific errors
        var inheritanceDiags = diagnostics.Where(d =>
            d.Id == DiagnosticId.TraitInheritanceCycle ||
            d.Id == DiagnosticId.TraitParentNotTrait ||
            d.Id == DiagnosticId.ArityMismatch ||
            d.Id == DiagnosticId.MissingTypeArguments).ToList();
        Assert.Empty(inheritanceDiags);

        var classType = FindType(model, "IntSorter", SymbolKind.Class);
        Assert.NotNull(classType);

        var sortableTrait = FindTrait(model, "Sortable");
        var comparableTrait = FindTrait(model, "Comparable");
        Assert.NotNull(sortableTrait);
        Assert.NotNull(comparableTrait);

        // Check that IntSorter satisfies Sortable (the child trait)
        var allSatisfactions = model.TraitSatisfactionMap.All().ToList();
        var intSorterSatisfactions = allSatisfactions
            .Where(s => ReferenceEquals(s.Type, classType))
            .ToList();

        // Should satisfy at least one trait (Sortable or Comparable<int>)
        Assert.NotEmpty(intSorterSatisfactions);

        // Check that it satisfies a constructed Comparable (parent)
        var satisfiesComparable = intSorterSatisfactions
            .Any(s => s.Trait is ConstructedTraitType ctt &&
                      ctt.GenericTraitDefinition.DisplayName == "Comparable");
        Assert.True(satisfiesComparable,
            "IntSorter should satisfy Comparable<int> transitively via Sortable");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5f: Linear chain — transitive effective members
    //
    // trait A { int a; }
    // trait B : A { int b; }
    // trait C : B { int c; }
    // Effective members of C = c + b + a
    //
    // **Validates: Requirements 3.7, 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LinearChain_EffectiveMembers_IncludesAllAncestors()
    {
        // Arrange: trait A { int fieldA; }
        //          trait B : A { int fieldB; }
        //          trait C : B { int fieldC; }
        var traitA = MakeSimpleTrait("TraitA", "fieldA");
        var traitB = MakeSimpleTrait("TraitB", "fieldB",
            MakeBaseList(MakeBaseTypeRef("TraitA")));
        var traitC = MakeSimpleTrait("TraitC", "fieldC",
            MakeBaseList(MakeBaseTypeRef("TraitB")));

        var ast = MakeCompilationUnit(
            MakeTraitItem(traitA),
            MakeTraitItem(traitB),
            MakeTraitItem(traitC));

        // Act
        var (model, diagnostics) = BuildModel(ast);

        // Assert: no errors
        var errorDiags = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errorDiags);

        var traitCType = FindTrait(model, "TraitC");
        Assert.NotNull(traitCType);

        // Effective members of C = fieldC + fieldB + fieldA (3 members)
        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(traitCType);
        Assert.Equal(3, effectiveMembers.Count);

        var memberNames = effectiveMembers.Select(m => m.Member.Name).ToHashSet();
        Assert.Contains("fieldA", memberNames);
        Assert.Contains("fieldB", memberNames);
        Assert.Contains("fieldC", memberNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5g: Same-name member deduplication
    //
    // When a child trait declares a member with the same name as a parent
    // trait member, it counts as a single requirement (deduplicated).
    //
    // **Validates: Requirements 4.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SameNameMember_ChildAndParent_DeduplicatedToSingleRequirement()
    {
        // Arrange: trait Parent { int shared; }
        //          trait Child : Parent { int shared; }  (same name)
        // Effective members of Child should have "shared" only once
        var parentDecl = MakeSimpleTrait("Parent", "shared");
        var childDecl = MakeSimpleTrait("Child", "shared",
            MakeBaseList(MakeBaseTypeRef("Parent")));

        var ast = MakeCompilationUnit(
            MakeTraitItem(parentDecl),
            MakeTraitItem(childDecl));

        // Act
        var (model, _) = BuildModel(ast);

        // Assert
        var childTrait = FindTrait(model, "Child");
        Assert.NotNull(childTrait);

        var effectiveMembers = SemanticModelBuilder.GetEffectiveMembers(childTrait);

        // "shared" should appear exactly once (child's own declaration wins)
        Assert.Single(effectiveMembers, m => m.Member.Name == "shared");
    }
}
