// Feature: generic-traits, Property 3: Trait inheritance validation diagnostics
// **Validates: Requirements 3.2, 3.5, 3.6, 17.6, 17.7, 17.8**

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
/// Property-based tests for trait inheritance validation diagnostics.
///
/// **Validates: Requirements 3.2, 3.5, 3.6, 17.6, 17.7, 17.8**
///
/// For any trait declaration whose inheritance clause references a name that resolves
/// to a class, struct, or interface (not a trait), the semantic model builder SHALL
/// emit a MIST_TRAIT_PARENT_NOT_TRAIT diagnostic. For any trait inheritance graph
/// containing a cycle, the semantic model builder SHALL emit a
/// MIST_TRAIT_INHERITANCE_CYCLE diagnostic for each trait involved. For any trait
/// that lists the same parent trait more than once in its inheritance clause, the
/// semantic model builder SHALL emit a MIST_TRAIT_DUPLICATE_PARENT diagnostic.
/// </summary>
public class TraitInheritanceValidationPropertyTests
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

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items)
    {
        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    private static TopLevelItemNode MakeTraitItem(TraitDeclarationNode traitDecl)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode
            {
                TraitDeclaration = traitDecl,
            },
        };
    }

    private static TopLevelItemNode MakeClassItem(string name)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = new ClassDeclarationNode
                {
                    Name = Tok(name),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                },
            },
        };
    }

    private static TopLevelItemNode MakeStructItem(string name)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new StructTypeTypeDeclarationNode
            {
                StructDeclaration = new StructDeclarationNode
                {
                    Name = Tok(name),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                },
            },
        };
    }

    private static TopLevelItemNode MakeInterfaceItem(string name)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new InterfaceTypeTypeDeclarationNode
            {
                InterfaceDeclaration = new InterfaceDeclarationNode
                {
                    Name = Tok(name),
                    Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
                },
            },
        };
    }

    private static BaseListNode MakeBaseList(params string[] parentNames)
    {
        var first = new BaseTypeRefNode { Name = Tok(parentNames[0]) };
        var rest = parentNames.Skip(1)
            .Select(n => new BaseTypeRefNode { Name = Tok(n) })
            .ToImmutableArray();
        return new BaseListNode
        {
            First = first,
            Rest = first,
            BaseTypeRefs = rest,
        };
    }

    private static TraitDeclarationNode MakeSimpleTrait(string name, BaseListNode? parents = null)
    {
        var decl = new TraitDeclarationNode
        {
            Name = Tok(name),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = new PrimitiveTypeRefTypeReferenceNode
                        {
                            PrimitiveType = new IntTypePrimitiveTypeNode(),
                        },
                        Name = Tok("dummy"),
                    },
                }),
        };

        if (parents is not null)
            decl.Parents = parents;

        return decl;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3a: MIST_TRAIT_PARENT_NOT_TRAIT
    //
    // When a trait's inheritance clause references a name that resolves to a
    // class, struct, or interface (not a trait), the semantic model builder
    // SHALL emit MIST_TRAIT_PARENT_NOT_TRAIT.
    //
    // **Validates: Requirements 3.2, 17.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("interface")]
    public void ParentNotTrait_EmitsDiagnostic(string parentKind)
    {
        // Arrange: declare a non-trait type and a trait that tries to extend it
        var parentName = "NonTraitParent";
        TopLevelItemNode parentItem = parentKind switch
        {
            "class" => MakeClassItem(parentName),
            "struct" => MakeStructItem(parentName),
            "interface" => MakeInterfaceItem(parentName),
            _ => throw new ArgumentException($"Unknown kind: {parentKind}"),
        };

        var traitDecl = MakeSimpleTrait("MyTrait", MakeBaseList(parentName));
        var ast = MakeCompilationUnit(parentItem, MakeTraitItem(traitDecl));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var parentNotTraitDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitParentNotTrait)
            .ToList();

        Assert.Single(parentNotTraitDiags);
        Assert.Contains(parentName, parentNotTraitDiags[0].Message);
    }

    [Fact]
    public void ParentNotTrait_MultipleNonTraitParents_EmitsDiagnosticForEach()
    {
        // Arrange: trait extends both a class and an interface
        var ast = MakeCompilationUnit(
            MakeClassItem("MyClass"),
            MakeInterfaceItem("MyInterface"),
            MakeTraitItem(MakeSimpleTrait("MyTrait", MakeBaseList("MyClass", "MyInterface"))));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var parentNotTraitDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitParentNotTrait)
            .ToList();

        Assert.Equal(2, parentNotTraitDiags.Count);
    }

    [Fact]
    public void ParentIsTrait_NoDiagnostic()
    {
        // Arrange: trait extends another trait (valid case)
        var parentTrait = MakeSimpleTrait("ParentTrait");
        var childTrait = MakeSimpleTrait("ChildTrait", MakeBaseList("ParentTrait"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(parentTrait),
            MakeTraitItem(childTrait));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no MIST_TRAIT_PARENT_NOT_TRAIT diagnostic
        var parentNotTraitDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitParentNotTrait)
            .ToList();

        Assert.Empty(parentNotTraitDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3b: MIST_TRAIT_INHERITANCE_CYCLE
    //
    // When a trait inheritance graph contains a cycle, the semantic model
    // builder SHALL emit MIST_TRAIT_INHERITANCE_CYCLE for each trait involved.
    //
    // **Validates: Requirements 3.5, 17.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DirectCycle_TwoTraits_EmitsDiagnosticForBoth()
    {
        // Arrange: trait A : B, trait B : A
        var traitA = MakeSimpleTrait("TraitA", MakeBaseList("TraitB"));
        var traitB = MakeSimpleTrait("TraitB", MakeBaseList("TraitA"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(traitA),
            MakeTraitItem(traitB));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var cycleDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitInheritanceCycle)
            .ToList();

        Assert.True(cycleDiags.Count >= 2,
            $"Expected at least 2 cycle diagnostics, got {cycleDiags.Count}: " +
            $"{string.Join("; ", cycleDiags.Select(d => d.Message))}");
    }

    [Fact]
    public void SelfCycle_EmitsDiagnostic()
    {
        // Arrange: trait A : A (self-referencing)
        var traitA = MakeSimpleTrait("TraitA", MakeBaseList("TraitA"));
        var ast = MakeCompilationUnit(MakeTraitItem(traitA));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var cycleDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitInheritanceCycle)
            .ToList();

        Assert.NotEmpty(cycleDiags);
    }

    [Fact]
    public void IndirectCycle_ThreeTraits_EmitsDiagnosticForAll()
    {
        // Arrange: trait A : B, trait B : C, trait C : A
        var traitA = MakeSimpleTrait("TraitA", MakeBaseList("TraitB"));
        var traitB = MakeSimpleTrait("TraitB", MakeBaseList("TraitC"));
        var traitC = MakeSimpleTrait("TraitC", MakeBaseList("TraitA"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(traitA),
            MakeTraitItem(traitB),
            MakeTraitItem(traitC));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var cycleDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitInheritanceCycle)
            .ToList();

        Assert.True(cycleDiags.Count >= 3,
            $"Expected at least 3 cycle diagnostics, got {cycleDiags.Count}: " +
            $"{string.Join("; ", cycleDiags.Select(d => d.Message))}");
    }

    [Fact]
    public void NoCycle_LinearChain_NoDiagnostic()
    {
        // Arrange: trait A : B, trait B : C, trait C (no cycle)
        var traitC = MakeSimpleTrait("TraitC");
        var traitB = MakeSimpleTrait("TraitB", MakeBaseList("TraitC"));
        var traitA = MakeSimpleTrait("TraitA", MakeBaseList("TraitB"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(traitC),
            MakeTraitItem(traitB),
            MakeTraitItem(traitA));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no cycle diagnostics
        var cycleDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitInheritanceCycle)
            .ToList();

        Assert.Empty(cycleDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3c: MIST_TRAIT_DUPLICATE_PARENT
    //
    // When a trait lists the same parent trait more than once in its
    // inheritance clause, the semantic model builder SHALL emit
    // MIST_TRAIT_DUPLICATE_PARENT.
    //
    // **Validates: Requirements 3.6, 17.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateParent_SameTraitTwice_EmitsDiagnostic()
    {
        // Arrange: trait Child : Parent, Parent
        var parentTrait = MakeSimpleTrait("ParentTrait");
        var childTrait = MakeSimpleTrait("ChildTrait", MakeBaseList("ParentTrait", "ParentTrait"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(parentTrait),
            MakeTraitItem(childTrait));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert
        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitDuplicateParent)
            .ToList();

        Assert.Single(duplicateDiags);
        Assert.Contains("ParentTrait", duplicateDiags[0].Message);
    }

    [Fact]
    public void DuplicateParent_SameTraitThreeTimes_EmitsTwoDiagnostics()
    {
        // Arrange: trait Child : Parent, Parent, Parent
        var parentTrait = MakeSimpleTrait("ParentTrait");

        // Build a base list with three entries of the same parent
        var first = new BaseTypeRefNode { Name = Tok("ParentTrait") };
        var baseList = new BaseListNode
        {
            First = first,
            Rest = first,
            BaseTypeRefs = ImmutableArray.Create(
                new BaseTypeRefNode { Name = Tok("ParentTrait") },
                new BaseTypeRefNode { Name = Tok("ParentTrait") }),
        };

        var childTrait = MakeSimpleTrait("ChildTrait", baseList);
        var ast = MakeCompilationUnit(
            MakeTraitItem(parentTrait),
            MakeTraitItem(childTrait));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: two duplicate diagnostics (second and third occurrences)
        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitDuplicateParent)
            .ToList();

        Assert.Equal(2, duplicateDiags.Count);
    }

    [Fact]
    public void NoDuplicateParent_DistinctParents_NoDiagnostic()
    {
        // Arrange: trait Child : ParentA, ParentB (distinct parents)
        var parentA = MakeSimpleTrait("ParentA");
        var parentB = MakeSimpleTrait("ParentB");
        var childTrait = MakeSimpleTrait("ChildTrait", MakeBaseList("ParentA", "ParentB"));
        var ast = MakeCompilationUnit(
            MakeTraitItem(parentA),
            MakeTraitItem(parentB),
            MakeTraitItem(childTrait));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: no duplicate parent diagnostics
        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitDuplicateParent)
            .ToList();

        Assert.Empty(duplicateDiags);
    }

    [Fact]
    public void DuplicateParent_MixedValidAndDuplicate_EmitsDiagnosticOnlyForDuplicate()
    {
        // Arrange: trait Child : ParentA, ParentB, ParentA
        var parentA = MakeSimpleTrait("ParentA");
        var parentB = MakeSimpleTrait("ParentB");

        var first = new BaseTypeRefNode { Name = Tok("ParentA") };
        var baseList = new BaseListNode
        {
            First = first,
            Rest = first,
            BaseTypeRefs = ImmutableArray.Create(
                new BaseTypeRefNode { Name = Tok("ParentB") },
                new BaseTypeRefNode { Name = Tok("ParentA") }),
        };

        var childTrait = MakeSimpleTrait("ChildTrait", baseList);
        var ast = MakeCompilationUnit(
            MakeTraitItem(parentA),
            MakeTraitItem(parentB),
            MakeTraitItem(childTrait));

        // Act
        var (_, diagnostics) = BuildModel(ast);

        // Assert: exactly one duplicate diagnostic for ParentA
        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.TraitDuplicateParent)
            .ToList();

        Assert.Single(duplicateDiags);
        Assert.Contains("ParentA", duplicateDiags[0].Message);
    }
}
