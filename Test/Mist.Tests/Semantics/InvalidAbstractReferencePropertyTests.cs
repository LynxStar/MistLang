// Feature: abstract types, Property 4: Invalid abstract reference diagnostic
// **Validates: Requirements 5.2, 5.3**

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
/// Property-based tests verifying invalid abstract type reference diagnostics.
///
/// Property 4: For any type declaration that references a name in its abstract type list
/// where that name is either undefined or resolves to a class or struct (not an
/// abstract type), the semantic model builder SHALL emit a diagnostic error. If the name
/// is undefined, the diagnostic SHALL be MIST_UNDEFINED_ABSTRACT. If the name
/// resolves to a non-abstract type, the diagnostic SHALL be MIST_NOT_AN_ABSTRACT.
/// </summary>
public class InvalidAbstractReferencePropertyTests
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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
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

    private static TypeBodyDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<MethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };

        if(parentAbstractName is not null)
        {
            var abstractList1 = MakeBaseList(parentAbstractName);

            decl.BaseClause = abstractList1;

            decl.BaseList = abstractList1;
        }

        return decl;
    }

    private static TypeBodyDeclarationNode MakeClassDecl(
        string name,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static TypeBodyDeclarationNode MakeStructDecl(
        string name,
        string? abstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(abstractName is not null)
        {
            var abstractList2 = MakeBaseList(abstractName);

            decl.BaseClause = abstractList2;

            decl.BaseList = abstractList2;
        }

        return decl;
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    private static TypeDeclarationNode WrapAbstract(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    private static TypeDeclarationNode WrapClass(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    private static TypeDeclarationNode WrapStruct(TypeBodyDeclarationNode decl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = decl };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// The kind of type that references an invalid abstract name.
    /// We focus on struct and abstract since classes have a separate
    /// code path through Pass1_ResolveBaseTypes that handles undefined
    /// names with MIST_UNDEFINED_BASE_TYPE.
    /// </summary>
#pragma warning disable CA1034
    public enum ReferencingTypeKind
    {
        Struct,
        Abstract,
    }

    /// <summary>
    /// The kind of invalid reference: either the name is completely
    /// undefined, or it resolves to a class or struct (not an abstract type).
    /// </summary>
    public enum InvalidReferenceKind
    {
        Undefined,
        ReferencesClass,
        ReferencesStruct,
    }

    /// <summary>
    /// A test scenario for an invalid abstract reference.
    /// </summary>
    public record InvalidReferenceScenario(
        string ReferencingTypeName,
        ReferencingTypeKind TypeKind,
        string ReferencedName,
        InvalidReferenceKind ReferenceKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ReferencingTypeKind.Struct ? "struct" : "abstract";
            var refKind = ReferenceKind switch
            {
                InvalidReferenceKind.Undefined => "undefined",
                InvalidReferenceKind.ReferencesClass => "references class",
                InvalidReferenceKind.ReferencesStruct => "references struct",
                _ => "unknown",
            };
            return $"{kind} {ReferencingTypeName} : {ReferencedName} ({refKind})";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] StructNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] ClassNamePool =
        ["ClassA", "ClassB", "ClassC", "ClassD", "ClassE", "ClassF"];

    private static readonly string[] UndefinedNamePool =
        ["INoExist", "INowhere", "IGhost", "IPhantom", "IMissing", "IVoid"];

    public static Gen<InvalidReferenceScenario> GenInvalidReferenceScenario()
    {
        return from typeKind in Gen.Elements(
                   ReferencingTypeKind.Struct,
                   ReferencingTypeKind.Abstract)
               from refKind in Gen.Elements(
                   InvalidReferenceKind.Undefined,
                   InvalidReferenceKind.ReferencesClass,
                   InvalidReferenceKind.ReferencesStruct)
               from structIdx in Gen.Choose(0, StructNamePool.Length - 1)
               from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               from undefinedIdx in Gen.Choose(0, UndefinedNamePool.Length - 1)
               let referencingName = typeKind == ReferencingTypeKind.Struct
                   ? StructNamePool[structIdx]
                   : AbstractNamePool[abstractIdx]
               let referencedName = refKind switch
               {
                   InvalidReferenceKind.Undefined => UndefinedNamePool[undefinedIdx],
                   InvalidReferenceKind.ReferencesClass => ClassNamePool[classIdx],
                   InvalidReferenceKind.ReferencesStruct => StructNamePool[structIdx],
                   _ => UndefinedNamePool[0],
               }
               // Ensure the referencing type name and referenced name are different
               where referencingName != referencedName
               select new InvalidReferenceScenario(
                   referencingName,
                   typeKind,
                   referencedName,
                   refKind);
    }

    public static Arbitrary<InvalidReferenceScenario> ArbitraryInvalidReferenceScenario()
    {
        return GenInvalidReferenceScenario().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For any type declaration (struct or abstract type) that references
    /// a name in its abstract type list where that name is either undefined or resolves
    /// to a class or struct (not an abstract type), the semantic model builder SHALL
    /// emit a diagnostic error. If the name is undefined, the diagnostic SHALL be
    /// MIST_UNDEFINED_ABSTRACT. If the name resolves to a non-abstract type, the
    /// diagnostic SHALL be MIST_NOT_AN_ABSTRACT.
    ///
    /// **Validates: Requirements 5.2, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InvalidAbstractReferencePropertyTests) }, MaxTest = 100)]
    public bool InvalidAbstractReference_EmitsCorrectDiagnostic(InvalidReferenceScenario scenario)
    {
        // Build the type declarations for the scenario
        var typeDecls = new List<TypeDeclarationNode>();

        // If the reference is to a class or struct, declare that type first
        switch(scenario.ReferenceKind)
        {
            case InvalidReferenceKind.ReferencesClass:
                typeDecls.Add(WrapClass(MakeClassDecl(scenario.ReferencedName)));
                break;
            case InvalidReferenceKind.ReferencesStruct:
                typeDecls.Add(WrapStruct(MakeStructDecl(scenario.ReferencedName)));
                break;
                // Undefined: no declaration for the referenced name
        }

        // Build the referencing type that has the invalid abstract reference
        switch(scenario.TypeKind)
        {
            case ReferencingTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ReferencingTypeName, scenario.ReferencedName)));
                break;
            case ReferencingTypeKind.Abstract:
                typeDecls.Add(WrapAbstract(
                    MakeAbstractDecl(
                        scenario.ReferencingTypeName,
                        ImmutableArray<MethodDeclarationNode>.Empty,
                        scenario.ReferencedName)));
                break;
        }

        var ast = MakeCompilationUnit(typeDecls.ToArray());
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Determine the expected diagnostic ID
        var expectedDiagnosticId = scenario.ReferenceKind == InvalidReferenceKind.Undefined
            ? "MIST_UNDEFINED_ABSTRACT"
            : "MIST_NOT_AN_ABSTRACT";

        // Verify the expected diagnostic was emitted
        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == expectedDiagnosticId)
            .ToList();

        if(matchingDiagnostics.Count != 1)
        {
            return false;
        }

        // Verify the diagnostic message references the invalid name
        var diag = matchingDiagnostics[0];
        if(!diag.Message.Contains(scenario.ReferencedName))
        {
            return false;
        }

        // Verify the diagnostic severity is Error
        if(diag.Severity != DiagnosticSeverity.Error)
        {
            return false;
        }

        // Verify the implementing type's Abstracts list does NOT contain
        // the invalid reference (it should be skipped)
        var expectedKind = scenario.TypeKind switch
        {
            ReferencingTypeKind.Struct => SymbolKind.Struct,
            ReferencingTypeKind.Abstract => SymbolKind.Abstract,
            _ => SymbolKind.Struct,
        };

        var referencingSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ReferencingTypeName && s.Kind == expectedKind);
        if(referencingSymbol is null)
        {
            return false;
        }

        var resolvedAbstracts = referencingSymbol.DeclaredType switch
        {
            StructType structType => structType.Abstracts,
            AbstractType abstractType => abstractType.Abstracts,
            _ => Array.Empty<IType>(),
        };

        // The invalid reference should not appear in the resolved abstracts
        if(resolvedAbstracts.Any(i => i.DisplayName == scenario.ReferencedName))
        {
            return false;
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void StructReferencingUndefinedAbstract_EmitsUndefinedAbstractDiagnostic()
    {
        var structDecl = MakeStructDecl("Point", "INoExist");
        var ast = MakeCompilationUnit(WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_ABSTRACT");
        Assert.Contains("INoExist", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.Empty(structType.Abstracts);
    }

    [Fact]
    public void AbstractReferencingUndefinedAbstract_EmitsUndefinedAbstractDiagnostic()
    {
        var abstractDecl = MakeAbstractDecl("IDerived",
            ImmutableArray<MethodDeclarationNode>.Empty,
            "IGhost");
        var ast = MakeCompilationUnit(WrapAbstract(abstractDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_ABSTRACT");
        Assert.Contains("IGhost", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var abstractSymbol = model.AllSymbols.First(s => s.Name == "IDerived" && s.Kind == SymbolKind.Abstract);
        var abstractType = Assert.IsType<AbstractType>(abstractSymbol.DeclaredType);
        Assert.Empty(abstractType.Abstracts);
    }

    [Fact]
    public void StructReferencingClassName_EmitsNotAnAbstractDiagnostic()
    {
        var classDecl = MakeClassDecl("MyClass");
        var structDecl = MakeStructDecl("MyStruct", "MyClass");

        var ast = MakeCompilationUnit(WrapClass(classDecl), WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_ABSTRACT");
        Assert.Contains("MyClass", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var structSymbol = model.AllSymbols.First(s => s.Name == "MyStruct" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.Empty(structType.Abstracts);
    }

    [Fact]
    public void AbstractReferencingClassName_EmitsNotAnAbstractDiagnostic()
    {
        var classDecl = MakeClassDecl("SomeClass");
        var abstractDecl = MakeAbstractDecl("IChild",
            ImmutableArray<MethodDeclarationNode>.Empty,
            "SomeClass");

        var ast = MakeCompilationUnit(WrapClass(classDecl), WrapAbstract(abstractDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_ABSTRACT");
        Assert.Contains("SomeClass", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var abstractSymbol = model.AllSymbols.First(s => s.Name == "IChild" && s.Kind == SymbolKind.Abstract);
        var abstractType = Assert.IsType<AbstractType>(abstractSymbol.DeclaredType);
        Assert.Empty(abstractType.Abstracts);
    }

    [Fact]
    public void StructReferencingStructName_SetsBaseType()
    {
        var otherStruct = MakeStructDecl("OtherStruct");
        var structDecl = MakeStructDecl("MyStruct", "OtherStruct");

        var ast = MakeCompilationUnit(WrapStruct(otherStruct), WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // With struct inheritance, the first struct in the base list is now
        // consumed as the base type — no diagnostic should be emitted.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_NOT_AN_ABSTRACT");

        var structSymbol = model.AllSymbols.First(s => s.Name == "MyStruct" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.NotNull(structType.BaseType);
        Assert.Equal("OtherStruct", structType.BaseType!.DisplayName);
    }

    [Fact]
    public void AbstractReferencingStructName_EmitsNotAnAbstractDiagnostic()
    {
        var structDecl = MakeStructDecl("SomeStruct");
        var abstractDecl = MakeAbstractDecl("IChild",
            ImmutableArray<MethodDeclarationNode>.Empty,
            "SomeStruct");

        var ast = MakeCompilationUnit(WrapStruct(structDecl), WrapAbstract(abstractDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_ABSTRACT");
        Assert.Contains("SomeStruct", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var abstractSymbol = model.AllSymbols.First(s => s.Name == "IChild" && s.Kind == SymbolKind.Abstract);
        var abstractType = Assert.IsType<AbstractType>(abstractSymbol.DeclaredType);
        Assert.Empty(abstractType.Abstracts);
    }

}