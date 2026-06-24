// Feature: traits, Property 2: Trait type registration
// **Validates: Requirements 4.1, 4.2, 4.3**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying trait type registration in the semantic model.
///
/// Property 2: For any set of trait declarations with varying numbers of field, method,
/// and property members, the semantic model builder SHALL create a TraitType for each
/// trait with TypeKind.Trait, register a symbol with SymbolKind.Trait in the parent scope,
/// and register a symbol for each trait member in the trait's member scope
/// (SymbolKind.Field for fields, SymbolKind.Method for methods, SymbolKind.Property
/// for properties). The count of member symbols in the trait's member scope SHALL equal
/// the number of members declared in the trait.
/// </summary>
public class TraitTypeRegistrationPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

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
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Wraps a single trait declaration into a CompilationUnitNode.
    /// </summary>
    private static CompilationUnitNode MakeTraitCompilationUnit(TraitDeclarationNode traitDecl)
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new TraitTypeTypeDeclarationNode
                    {
                        TraitDeclaration = traitDecl,
                    },
                }),
        };
    }

    /// <summary>
    /// Wraps multiple trait declarations into a CompilationUnitNode.
    /// </summary>
    private static CompilationUnitNode MakeTraitCompilationUnit(
        params TraitDeclarationNode[] traitDecls)
    {
        var items = traitDecls.Select(t =>
            (TopLevelItemNode)new TypeItemTopLevelItemNode
            {
                TypeDeclaration = new TraitTypeTypeDeclarationNode
                {
                    TraitDeclaration = t,
                },
            }).ToImmutableArray();

        return new CompilationUnitNode { Items = items };
    }

    /// <summary>
    /// Counts the expected members from a trait declaration, categorized by kind.
    /// </summary>
    private static (int Fields, int Methods, int Properties) CountMembers(TraitDeclarationNode traitDecl)
    {
        int fields = 0, methods = 0, properties = 0;
        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode:
                    fields++;
                    break;
                case TraitMethodMemberTraitMemberDeclarationNode:
                    methods++;
                    break;
                case TraitPropertyMemberTraitMemberDeclarationNode:
                    properties++;
                    break;
            }
        }
        return (fields, methods, properties);
    }

    /// <summary>
    /// Gets the expected member name from a trait member declaration node.
    /// </summary>
    private static (string Name, SymbolKind Kind) GetMemberInfo(TraitMemberDeclarationNode member)
    {
        return member switch
        {
            TraitFieldMemberTraitMemberDeclarationNode f => (f.Field.Name.Lexeme, SymbolKind.Field),
            TraitMethodMemberTraitMemberDeclarationNode m => (m.Method.Name.Lexeme, SymbolKind.Method),
            TraitPropertyMemberTraitMemberDeclarationNode p => (p.Property.Name.Lexeme, SymbolKind.Property),
            _ => throw new InvalidOperationException("Unknown trait member type"),
        };
    }

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>FsCheck Arbitrary for trait declarations.</summary>
    public static Arbitrary<TraitDeclarationNode> ArbitraryTraitDeclaration() =>
        MistTraitGenerator.GenTraitDeclaration().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any trait declaration, the semantic model builder creates a
    /// TraitType with TypeKind.Trait, registers a SymbolKind.Trait symbol in the parent
    /// scope, and registers each trait member with the correct SymbolKind in the trait's
    /// member scope. The member count matches the declaration count.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TraitTypeRegistrationPropertyTests) }, MaxTest = 100)]
    public bool TraitTypeRegistration_TraitCorrectlyRegistered(TraitDeclarationNode traitDecl)
    {
        var traitName = traitDecl.Name.Lexeme;
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        // Requirement 4.2: A symbol with SymbolKind.Trait is registered in the parent scope
        var traitSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == traitName && s.Kind == SymbolKind.Trait);
        if (traitSymbol is null)
            return false;

        // Requirement 4.1: A TraitType is created with TypeKind.Trait
        if (traitSymbol.DeclaredType is not TraitType traitType)
            return false;
        if (traitType.Kind != TypeKind.Trait)
            return false;
        if (traitType.DisplayName != traitName)
            return false;

        // Requirement 4.3: Each trait member is registered with the correct SymbolKind
        var memberScope = traitType.MemberScope;
        var memberSymbols = memberScope.Members.ToList();

        // The count of member symbols SHALL equal the number of members declared
        if (memberSymbols.Count != traitDecl.Members.Length)
            return false;

        // Verify each declared member has a corresponding symbol with the correct kind
        foreach (var member in traitDecl.Members)
        {
            var (expectedName, expectedKind) = GetMemberInfo(member);
            if (!memberSymbols.Any(s => s.Name == expectedName && s.Kind == expectedKind))
                return false;
        }

        // Verify field, method, and property counts match
        var (expectedFields, expectedMethods, expectedProperties) = CountMembers(traitDecl);
        var actualFields = memberSymbols.Count(s => s.Kind == SymbolKind.Field);
        var actualMethods = memberSymbols.Count(s => s.Kind == SymbolKind.Method);
        var actualProperties = memberSymbols.Count(s => s.Kind == SymbolKind.Property);

        if (actualFields != expectedFields)
            return false;
        if (actualMethods != expectedMethods)
            return false;
        if (actualProperties != expectedProperties)
            return false;

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void EmptyTrait_RegistersTraitTypeWithNoMembers()
    {
        var traitDecl = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Printable"),
            Members = ImmutableArray<TraitMemberDeclarationNode>.Empty,
        };
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Printable" && s.Kind == SymbolKind.Trait);
        Assert.NotNull(symbol);

        var traitType = Assert.IsType<TraitType>(symbol.DeclaredType);
        Assert.Equal(TypeKind.Trait, traitType.Kind);
        Assert.Equal("Printable", traitType.DisplayName);
        Assert.Empty(traitType.MemberScope.Members);
    }

    [Fact]
    public void TraitWithFieldMembers_RegistersFieldSymbolsInMemberScope()
    {
        var fieldMember1 = new TraitFieldMemberTraitMemberDeclarationNode
        {
            Field = new TraitFieldDeclarationNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                } },
                Name = MistTypeGenerator.MakeToken("value"),
            },
        };
        var fieldMember2 = new TraitFieldMemberTraitMemberDeclarationNode
        {
            Field = new TraitFieldDeclarationNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                } },
                Name = MistTypeGenerator.MakeToken("label"),
            },
        };

        var traitDecl = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Describable"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(fieldMember1, fieldMember2),
        };
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Describable" && s.Kind == SymbolKind.Trait);
        Assert.NotNull(symbol);

        var traitType = Assert.IsType<TraitType>(symbol.DeclaredType);
        var fieldSymbols = traitType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Field)
            .ToList();

        Assert.Equal(2, fieldSymbols.Count);
        Assert.Contains(fieldSymbols, m => m.Name == "value");
        Assert.Contains(fieldSymbols, m => m.Name == "label");
    }

    [Fact]
    public void TraitWithMethodMembers_RegistersMethodSymbolsInMemberScope()
    {
        var methodMember = new TraitMethodMemberTraitMemberDeclarationNode
        {
            Method = new TraitMethodDeclarationNode
            {
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = MistTypeGenerator.MakeToken("Execute"),
                Parameters = null,
            },
        };

        var traitDecl = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Runnable"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(methodMember),
        };
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Runnable" && s.Kind == SymbolKind.Trait);
        Assert.NotNull(symbol);

        var traitType = Assert.IsType<TraitType>(symbol.DeclaredType);
        var methodSymbols = traitType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Method)
            .ToList();

        Assert.Single(methodSymbols);
        Assert.Equal("Execute", methodSymbols[0].Name);
    }

    [Fact]
    public void TraitWithPropertyMembers_RegistersPropertySymbolsInMemberScope()
    {
        var propertyMember = new TraitPropertyMemberTraitMemberDeclarationNode
        {
            Property = new TraitPropertyDeclarationNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                } },
                Name = MistTypeGenerator.MakeToken("Title"),
                Accessors = ImmutableArray.Create<AbstractAccessorDeclarationNode>(
                    new AbstractGetAccessorAbstractAccessorDeclarationNode()),
            },
        };

        var traitDecl = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Titled"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(propertyMember),
        };
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Titled" && s.Kind == SymbolKind.Trait);
        Assert.NotNull(symbol);

        var traitType = Assert.IsType<TraitType>(symbol.DeclaredType);
        var propSymbols = traitType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Property)
            .ToList();

        Assert.Single(propSymbols);
        Assert.Equal("Title", propSymbols[0].Name);
    }

    [Fact]
    public void TraitWithMixedMembers_RegistersAllMemberKindsCorrectly()
    {
        var fieldMember = new TraitFieldMemberTraitMemberDeclarationNode
        {
            Field = new TraitFieldDeclarationNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                } },
                Name = MistTypeGenerator.MakeToken("count"),
            },
        };
        var methodMember = new TraitMethodMemberTraitMemberDeclarationNode
        {
            Method = new TraitMethodDeclarationNode
            {
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = MistTypeGenerator.MakeToken("Process"),
                Parameters = null,
            },
        };
        var propertyMember = new TraitPropertyMemberTraitMemberDeclarationNode
        {
            Property = new TraitPropertyDeclarationNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                } },
                Name = MistTypeGenerator.MakeToken("Name"),
                Accessors = ImmutableArray.Create<AbstractAccessorDeclarationNode>(
                    new AbstractGetAccessorAbstractAccessorDeclarationNode(),
                    new AbstractSetAccessorAbstractAccessorDeclarationNode()),
            },
        };

        var traitDecl = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Composite"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                fieldMember, methodMember, propertyMember),
        };
        var ast = MakeTraitCompilationUnit(traitDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Composite" && s.Kind == SymbolKind.Trait);
        Assert.NotNull(symbol);

        var traitType = Assert.IsType<TraitType>(symbol.DeclaredType);
        var members = traitType.MemberScope.Members.ToList();

        Assert.Equal(3, members.Count);
        Assert.Single(members, m => m.Kind == SymbolKind.Field && m.Name == "count");
        Assert.Single(members, m => m.Kind == SymbolKind.Method && m.Name == "Process");
        Assert.Single(members, m => m.Kind == SymbolKind.Property && m.Name == "Name");
    }

    [Fact]
    public void MultipleTraits_AllRegisteredCorrectly()
    {
        var trait1 = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Printable"),
            Members = ImmutableArray<TraitMemberDeclarationNode>.Empty,
        };
        var trait2 = new TraitDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Serializable"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                        Name = MistTypeGenerator.MakeToken("Serialize"),
                        Parameters = null,
                    },
                }),
        };

        var ast = MakeTraitCompilationUnit(trait1, trait2);
        var (model, _) = BuildModelWithDiagnostics(ast);

        // Printable: 0 members
        var printableSymbol = model.AllSymbols.First(s => s.Name == "Printable" && s.Kind == SymbolKind.Trait);
        var printableType = Assert.IsType<TraitType>(printableSymbol.DeclaredType);
        Assert.Empty(printableType.MemberScope.Members);

        // Serializable: 1 method
        var serializableSymbol = model.AllSymbols.First(s => s.Name == "Serializable" && s.Kind == SymbolKind.Trait);
        var serializableType = Assert.IsType<TraitType>(serializableSymbol.DeclaredType);
        Assert.Single(serializableType.MemberScope.Members, m => m.Kind == SymbolKind.Method && m.Name == "Serialize");
    }
}
