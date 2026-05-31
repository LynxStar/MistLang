// Feature: generic-traits, Property 2: Trait type parameter registration
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

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
/// Property-based tests for trait type parameter registration.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**
///
/// For any generic trait declaration with N type parameters (N ≥ 1), the semantic
/// model builder SHALL create exactly N TypeParameterType instances with correct
/// ordinals (0 through N-1), store them on the TraitType.TypeParameters property,
/// and register each as a resolvable type in the trait's member scope. Trait member
/// signatures that reference a type parameter name SHALL resolve to the corresponding
/// TypeParameterType. For any trait whose TypeParameterList contains duplicate
/// identifier names, the semantic model builder SHALL emit a
/// MIST_DUPLICATE_TYPE_PARAMETER diagnostic.
/// </summary>
public class GenericTraitTypeParamRegistrationPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
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

    private static TraitType? FindTraitType(ISemanticModel model, string traitName)
    {
        var symbol = model.AllSymbols
            .FirstOrDefault(s => s.Kind == SymbolKind.Trait && s.Name == traitName);
        return symbol?.DeclaredType as TraitType;
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(string[] names)
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

    private static TypeReferenceNode MakeNamedTypeRef(string name)
    {
        return new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(name),
                IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
            },
        } };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
        {
            PrimitiveType = new IntTypePrimitiveTypeNode(),
        } };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a generic trait declaration scenario for property testing.
    /// </summary>
#pragma warning disable CA1034
    public record GenericTraitScenario(
        string TraitName,
        ImmutableArray<string> TypeParamNames,
        bool HasFieldUsingTypeParam,
        bool HasMethodUsingTypeParam)
    {
        public override string ToString()
        {
            var members = new List<string>();
            if (HasFieldUsingTypeParam) members.Add("field");
            if (HasMethodUsingTypeParam) members.Add("method");
            var memberStr = members.Count > 0 ? $" with {string.Join(", ", members)}" : "";
            return $"trait {TraitName}<{string.Join(", ", TypeParamNames)}>{memberStr}";
        }
    }

    public record DuplicateTypeParamTraitScenario(
        string TraitName,
        ImmutableArray<string> TypeParamNames,
        int ExpectedDuplicateCount)
    {
        public override string ToString()
        {
            return $"trait {TraitName}<{string.Join(", ", TypeParamNames)}> (expected {ExpectedDuplicateCount} duplicate(s))";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] TraitNamePool = ["Comparable", "Converter", "Mappable", "Reducible"];

    /// <summary>
    /// Generates a valid generic trait scenario with 1-4 unique type parameters.
    /// </summary>
    public static Gen<GenericTraitScenario> GenValidTraitScenario()
    {
        return from traitName in Gen.Elements(TraitNamePool)
               from paramCount in Gen.Choose(1, 4)
               from hasField in Gen.Elements(true, false)
               from hasMethod in Gen.Elements(true, false)
               let names = TypeParamPool.Take(paramCount).ToImmutableArray()
               select new GenericTraitScenario(traitName, names, hasField, hasMethod);
    }

    /// <summary>
    /// Generates a trait scenario with duplicate type parameter names.
    /// </summary>
    public static Gen<DuplicateTypeParamTraitScenario> GenDuplicateTraitScenario()
    {
        return from traitName in Gen.Elements(TraitNamePool)
               from paramCount in Gen.Choose(2, 4)
               from duplicateName in Gen.Elements(TypeParamPool)
               from otherNames in Gen.Shuffle(
                       TypeParamPool.Where(n => n != duplicateName).ToArray())
                   .Select(arr => arr.Take(paramCount - 2).ToArray())
               let names = BuildNamesWithDuplicate(duplicateName, otherNames, paramCount)
               let expectedDuplicates = names.GroupBy(n => n)
                   .Where(g => g.Count() > 1).Sum(g => g.Count() - 1)
               select new DuplicateTypeParamTraitScenario(traitName, names, expectedDuplicates);
    }

    private static ImmutableArray<string> BuildNamesWithDuplicate(
        string duplicateName, string[] otherNames, int totalCount)
    {
        var names = new List<string> { duplicateName, duplicateName };
        foreach (var other in otherNames)
        {
            if (names.Count >= totalCount)
                break;
            names.Add(other);
        }
        while (names.Count < totalCount)
            names.Add(duplicateName);
        return names.ToImmutableArray();
    }

    public static Arbitrary<GenericTraitScenario> ArbitraryGenericTraitScenario() =>
        GenValidTraitScenario().ToArbitrary();

    public static Arbitrary<DuplicateTypeParamTraitScenario> ArbitraryDuplicateTraitScenario() =>
        GenDuplicateTraitScenario().ToArbitrary();

    // ─── Helper: Build trait AST from scenario ───────────────────────────

    private static TraitDeclarationNode BuildTraitDecl(GenericTraitScenario scenario)
    {
        var members = new List<TraitMemberDeclarationNode>();

        if (scenario.HasFieldUsingTypeParam)
        {
            // Add a field using the first type parameter: T value;
            members.Add(new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakeNamedTypeRef(scenario.TypeParamNames[0]),
                    Name = Tok("value"),
                },
            });
        }

        if (scenario.HasMethodUsingTypeParam)
        {
            // Add a method using the last type parameter:
            // int Process(T/U/V/W input);
            var lastParam = scenario.TypeParamNames[^1];
            members.Add(new TraitMethodMemberTraitMemberDeclarationNode
            {
                Method = new TraitMethodDeclarationNode
                {
                    ReturnType = MakePrimitiveTypeRef(),
                    Name = Tok("Process"),
                    Parameters = new ParameterListNode
                    {
                        Parameter = new ParameterNode
                        {
                            Type = MakeNamedTypeRef(lastParam),
                            Name = Tok("input"),
                        },
                    },
                },
            });
        }

        // If no members were requested, add a simple int field so the trait isn't empty
        if (members.Count == 0)
        {
            members.Add(new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(),
                    Name = Tok("count"),
                },
            });
        }

        return new TraitDeclarationNode
        {
            Name = Tok(scenario.TraitName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = members.ToImmutableArray(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2a: For any generic trait declaration with N type parameters (N ≥ 1),
    /// the semantic model builder SHALL create exactly N TypeParameterType instances
    /// with correct ordinals (0 through N-1) and store them on TraitType.TypeParameters.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericTraitTypeParamRegistrationPropertyTests) }, MaxTest = 100)]
    public bool TypeParameters_HaveCorrectCountAndOrdinals(GenericTraitScenario scenario)
    {
        var traitDecl = BuildTraitDecl(scenario);
        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (model, _) = BuildModel(ast);

        // Find the trait type
        var traitType = FindTraitType(model, scenario.TraitName);
        if (traitType is null)
            return false;

        // Verify count matches
        if (traitType.TypeParameters.Count != scenario.TypeParamNames.Length)
            return false;

        // Verify each type parameter has correct name and ordinal
        for (var i = 0; i < scenario.TypeParamNames.Length; i++)
        {
            var tp = traitType.TypeParameters[i];
            if (tp.DisplayName != scenario.TypeParamNames[i])
                return false;
            if (tp is TypeParameterType tpt && tpt.Ordinal != i)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 2b: For any generic trait declaration, each type parameter SHALL be
    /// registered as a resolvable type in the trait's member scope.
    ///
    /// **Validates: Requirements 10.1, 10.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericTraitTypeParamRegistrationPropertyTests) }, MaxTest = 100)]
    public bool TypeParameters_AreResolvableInTraitMemberScope(GenericTraitScenario scenario)
    {
        var traitDecl = BuildTraitDecl(scenario);
        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (model, _) = BuildModel(ast);

        // Find the trait type
        var traitType = FindTraitType(model, scenario.TraitName);
        if (traitType is null)
            return false;

        // Each type parameter name should be resolvable in the trait's member scope
        foreach (var tp in traitType.TypeParameters)
        {
            var resolved = traitType.MemberScope.Lookup(tp.DisplayName);
            if (resolved is null)
                return false;
            if (resolved.DeclaredType is not TypeParameterType resolvedTp)
                return false;
            if (tp is not TypeParameterType tpCast)
                return false;
            if (resolvedTp.Name != tpCast.Name || resolvedTp.Ordinal != tpCast.Ordinal)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 2c: Trait member signatures that reference a type parameter name
    /// SHALL resolve to the corresponding TypeParameterType.
    ///
    /// **Validates: Requirements 10.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericTraitTypeParamRegistrationPropertyTests) }, MaxTest = 100)]
    public bool TypeParameterReferences_ResolveInMemberScope(GenericTraitScenario scenario)
    {
        // Only test scenarios that actually use type params in members
        if (!scenario.HasFieldUsingTypeParam && !scenario.HasMethodUsingTypeParam)
            return true; // vacuously true

        var traitDecl = BuildTraitDecl(scenario);
        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (model, _) = BuildModel(ast);

        // Find the trait type
        var traitType = FindTraitType(model, scenario.TraitName);
        if (traitType is null)
            return false;

        // Verify that the type parameter names used in members resolve correctly
        if (scenario.HasFieldUsingTypeParam)
        {
            var firstParamName = scenario.TypeParamNames[0];
            var resolved = traitType.MemberScope.Lookup(firstParamName);
            if (resolved?.DeclaredType is not TypeParameterType fieldTp)
                return false;
            if (fieldTp.Ordinal != 0)
                return false;
        }

        if (scenario.HasMethodUsingTypeParam)
        {
            var lastParamName = scenario.TypeParamNames[^1];
            var resolved = traitType.MemberScope.Lookup(lastParamName);
            if (resolved?.DeclaredType is not TypeParameterType methodTp)
                return false;
            if (methodTp.Ordinal != scenario.TypeParamNames.Length - 1)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 2d: For any trait whose TypeParameterList contains duplicate identifier
    /// names, the semantic model builder SHALL emit a MIST_DUPLICATE_TYPE_PARAMETER
    /// diagnostic.
    ///
    /// **Validates: Requirements 10.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GenericTraitTypeParamRegistrationPropertyTests) }, MaxTest = 100)]
    public bool DuplicateTypeParams_EmitDiagnostic(DuplicateTypeParamTraitScenario scenario)
    {
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok(scenario.TraitName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef(),
                        Name = Tok("dummy"),
                    },
                }),
        };

        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (_, diagnostics) = BuildModel(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        return duplicateDiags.Count == scenario.ExpectedDuplicateCount;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Targeted unit tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleTypeParam_RegisteredCorrectly()
    {
        // trait Foo<T> { T value; }
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Foo"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakeNamedTypeRef("T"),
                        Name = Tok("value"),
                    },
                }),
        };

        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (model, diagnostics) = BuildModel(ast);

        var traitType = FindTraitType(model, "Foo");
        Assert.NotNull(traitType);

        // Verify type parameters
        Assert.Single(traitType.TypeParameters);
        Assert.Equal("T", traitType.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)traitType.TypeParameters[0]).Ordinal);

        // Verify resolvable in member scope
        var resolved = traitType.MemberScope.Lookup("T");
        Assert.NotNull(resolved);
        var resolvedTp = Assert.IsType<TypeParameterType>(resolved.DeclaredType);
        Assert.Equal("T", resolvedTp.Name);
        Assert.Equal(0, resolvedTp.Ordinal);

        // No duplicate diagnostics
        Assert.Empty(diagnostics.Where(d => d.Id == DiagnosticId.DuplicateTypeParameter));
    }

    [Fact]
    public void MultipleTypeParams_RegisteredWithCorrectOrdinals()
    {
        // trait Foo<T, U> { T Convert(U input); }
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Foo"),
            TypeParams = MakeTypeParamList(["T", "U"]),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = MakeNamedTypeRef("T"),
                        Name = Tok("Convert"),
                        Parameters = new ParameterListNode
                        {
                            Parameter = new ParameterNode
                            {
                                Type = MakeNamedTypeRef("U"),
                                Name = Tok("input"),
                            },
                        },
                    },
                }),
        };

        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (model, diagnostics) = BuildModel(ast);

        var traitType = FindTraitType(model, "Foo");
        Assert.NotNull(traitType);

        // Verify type parameters
        Assert.Equal(2, traitType.TypeParameters.Count);
        Assert.Equal("T", traitType.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)traitType.TypeParameters[0]).Ordinal);
        Assert.Equal("U", traitType.TypeParameters[1].DisplayName);
        Assert.Equal(1, ((TypeParameterType)traitType.TypeParameters[1]).Ordinal);

        // Verify both are resolvable in member scope
        var resolvedT = traitType.MemberScope.Lookup("T");
        Assert.NotNull(resolvedT);
        Assert.IsType<TypeParameterType>(resolvedT.DeclaredType);

        var resolvedU = traitType.MemberScope.Lookup("U");
        Assert.NotNull(resolvedU);
        Assert.IsType<TypeParameterType>(resolvedU.DeclaredType);

        // No duplicate diagnostics
        Assert.Empty(diagnostics.Where(d => d.Id == DiagnosticId.DuplicateTypeParameter));
    }

    [Fact]
    public void DuplicateTypeParam_EmitsDiagnostic()
    {
        // trait Foo<T, T> { ... }
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Foo"),
            TypeParams = MakeTypeParamList(["T", "T"]),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef(),
                        Name = Tok("dummy"),
                    },
                }),
        };

        var ast = MakeCompilationUnit(MakeTraitItem(traitDecl));
        var (_, diagnostics) = BuildModel(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        Assert.Single(duplicateDiags);
        Assert.Contains("T", duplicateDiags[0].Message);
    }
}
