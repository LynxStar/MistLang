// Feature: interfaces, Property 4: Invalid interface reference diagnostic
// **Validates: Requirements 5.2, 5.3**

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
/// Property-based tests verifying invalid interface reference diagnostics.
///
/// Property 4: For any type declaration that references a name in its interface list
/// where that name is either undefined or resolves to a class or struct (not an
/// interface), the semantic model builder SHALL emit a diagnostic error. If the name
/// is undefined, the diagnostic SHALL be MIST_UNDEFINED_INTERFACE. If the name
/// resolves to a non-interface type, the diagnostic SHALL be MIST_NOT_AN_INTERFACE.
/// </summary>
public class InvalidInterfaceReferencePropertyTests
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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(string name)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods,
        string? parentInterfaceName = null)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (parentInterfaceName is not null)
        {
            var _ifaceList1 = MakeBaseList(parentInterfaceName);

            decl.InterfaceList = _ifaceList1;

            decl.Interfaces = _ifaceList1;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? interfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (interfaceName is not null)
        {
            var _ifaceList2 = MakeBaseList(interfaceName);

            decl.InterfaceList = _ifaceList2;

            decl.Interfaces = _ifaceList2;
        }

        return decl;
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Types = typeDecls.ToImmutableArray(),
        };
    }

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// The kind of type that references an invalid interface name.
    /// We focus on struct and interface since classes have a separate
    /// code path through Pass1_ResolveBaseTypes that handles undefined
    /// names with MIST_UNDEFINED_BASE_TYPE.
    /// </summary>
#pragma warning disable CA1034
    public enum ReferencingTypeKind
    {
        Struct,
        Interface,
    }

    /// <summary>
    /// The kind of invalid reference: either the name is completely
    /// undefined, or it resolves to a class or struct (not an interface).
    /// </summary>
    public enum InvalidReferenceKind
    {
        Undefined,
        ReferencesClass,
        ReferencesStruct,
    }

    /// <summary>
    /// A test scenario for an invalid interface reference.
    /// </summary>
    public record InvalidReferenceScenario(
        string ReferencingTypeName,
        ReferencingTypeKind TypeKind,
        string ReferencedName,
        InvalidReferenceKind ReferenceKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ReferencingTypeKind.Struct ? "struct" : "interface";
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

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] ClassNamePool =
        ["ClassA", "ClassB", "ClassC", "ClassD", "ClassE", "ClassF"];

    private static readonly string[] UndefinedNamePool =
        ["INoExist", "INowhere", "IGhost", "IPhantom", "IMissing", "IVoid"];

    public static Gen<InvalidReferenceScenario> GenInvalidReferenceScenario()
    {
        return from typeKind in Gen.Elements(
                   ReferencingTypeKind.Struct,
                   ReferencingTypeKind.Interface)
               from refKind in Gen.Elements(
                   InvalidReferenceKind.Undefined,
                   InvalidReferenceKind.ReferencesClass,
                   InvalidReferenceKind.ReferencesStruct)
               from structIdx in Gen.Choose(0, StructNamePool.Length - 1)
               from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from classIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               from undefinedIdx in Gen.Choose(0, UndefinedNamePool.Length - 1)
               let referencingName = typeKind == ReferencingTypeKind.Struct
                   ? StructNamePool[structIdx]
                   : InterfaceNamePool[interfaceIdx]
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

    public static Arbitrary<InvalidReferenceScenario> ArbitraryInvalidReferenceScenario() =>
        GenInvalidReferenceScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For any type declaration (struct or interface) that references
    /// a name in its interface list where that name is either undefined or resolves
    /// to a class or struct (not an interface), the semantic model builder SHALL
    /// emit a diagnostic error. If the name is undefined, the diagnostic SHALL be
    /// MIST_UNDEFINED_INTERFACE. If the name resolves to a non-interface type, the
    /// diagnostic SHALL be MIST_NOT_AN_INTERFACE.
    ///
    /// **Validates: Requirements 5.2, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InvalidInterfaceReferencePropertyTests) }, MaxTest = 100)]
    public bool InvalidInterfaceReference_EmitsCorrectDiagnostic(InvalidReferenceScenario scenario)
    {
        // Build the type declarations for the scenario
        var typeDecls = new List<TypeDeclarationNode>();

        // If the reference is to a class or struct, declare that type first
        switch (scenario.ReferenceKind)
        {
            case InvalidReferenceKind.ReferencesClass:
                typeDecls.Add(WrapClass(MakeClassDecl(scenario.ReferencedName)));
                break;
            case InvalidReferenceKind.ReferencesStruct:
                typeDecls.Add(WrapStruct(MakeStructDecl(scenario.ReferencedName)));
                break;
            // Undefined: no declaration for the referenced name
        }

        // Build the referencing type that has the invalid interface reference
        switch (scenario.TypeKind)
        {
            case ReferencingTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ReferencingTypeName, scenario.ReferencedName)));
                break;
            case ReferencingTypeKind.Interface:
                typeDecls.Add(WrapInterface(
                    MakeInterfaceDecl(
                        scenario.ReferencingTypeName,
                        ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
                        scenario.ReferencedName)));
                break;
        }

        var ast = MakeCompilationUnit(typeDecls.ToArray());
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Determine the expected diagnostic ID
        var expectedDiagnosticId = scenario.ReferenceKind == InvalidReferenceKind.Undefined
            ? "MIST_UNDEFINED_INTERFACE"
            : "MIST_NOT_AN_INTERFACE";

        // Verify the expected diagnostic was emitted
        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == expectedDiagnosticId)
            .ToList();

        if (matchingDiagnostics.Count != 1)
            return false;

        // Verify the diagnostic message references the invalid name
        var diag = matchingDiagnostics[0];
        if (!diag.Message.Contains(scenario.ReferencedName))
            return false;

        // Verify the diagnostic severity is Error
        if (diag.Severity != DiagnosticSeverity.Error)
            return false;

        // Verify the implementing type's Interfaces list does NOT contain
        // the invalid reference (it should be skipped)
        var expectedKind = scenario.TypeKind switch
        {
            ReferencingTypeKind.Struct => SymbolKind.Struct,
            ReferencingTypeKind.Interface => SymbolKind.Interface,
            _ => SymbolKind.Struct,
        };

        var referencingSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ReferencingTypeName && s.Kind == expectedKind);
        if (referencingSymbol is null)
            return false;

        IReadOnlyList<IType> resolvedInterfaces = referencingSymbol.DeclaredType switch
        {
            StructType structType => structType.Interfaces,
            InterfaceType interfaceType => interfaceType.Interfaces,
            _ => Array.Empty<IType>(),
        };

        // The invalid reference should not appear in the resolved interfaces
        if (resolvedInterfaces.Any(i => i.DisplayName == scenario.ReferencedName))
            return false;

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void StructReferencingUndefinedInterface_EmitsUndefinedInterfaceDiagnostic()
    {
        var structDecl = MakeStructDecl("Point", "INoExist");
        var ast = MakeCompilationUnit(WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_INTERFACE");
        Assert.Contains("INoExist", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.Empty(structType.Interfaces);
    }

    [Fact]
    public void InterfaceReferencingUndefinedInterface_EmitsUndefinedInterfaceDiagnostic()
    {
        var interfaceDecl = MakeInterfaceDecl("IDerived",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
            "IGhost");
        var ast = MakeCompilationUnit(WrapInterface(interfaceDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_INTERFACE");
        Assert.Contains("IGhost", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var interfaceSymbol = model.AllSymbols.First(s => s.Name == "IDerived" && s.Kind == SymbolKind.Interface);
        var interfaceType = Assert.IsType<InterfaceType>(interfaceSymbol.DeclaredType);
        Assert.Empty(interfaceType.Interfaces);
    }

    [Fact]
    public void StructReferencingClassName_EmitsNotAnInterfaceDiagnostic()
    {
        var classDecl = MakeClassDecl("MyClass");
        var structDecl = MakeStructDecl("MyStruct", "MyClass");

        var ast = MakeCompilationUnit(WrapClass(classDecl), WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_INTERFACE");
        Assert.Contains("MyClass", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var structSymbol = model.AllSymbols.First(s => s.Name == "MyStruct" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.Empty(structType.Interfaces);
    }

    [Fact]
    public void InterfaceReferencingClassName_EmitsNotAnInterfaceDiagnostic()
    {
        var classDecl = MakeClassDecl("SomeClass");
        var interfaceDecl = MakeInterfaceDecl("IChild",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
            "SomeClass");

        var ast = MakeCompilationUnit(WrapClass(classDecl), WrapInterface(interfaceDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_INTERFACE");
        Assert.Contains("SomeClass", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var interfaceSymbol = model.AllSymbols.First(s => s.Name == "IChild" && s.Kind == SymbolKind.Interface);
        var interfaceType = Assert.IsType<InterfaceType>(interfaceSymbol.DeclaredType);
        Assert.Empty(interfaceType.Interfaces);
    }

    [Fact]
    public void StructReferencingStructName_EmitsNotAnInterfaceDiagnostic()
    {
        var otherStruct = MakeStructDecl("OtherStruct");
        var structDecl = MakeStructDecl("MyStruct", "OtherStruct");

        var ast = MakeCompilationUnit(WrapStruct(otherStruct), WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_INTERFACE");
        Assert.Contains("OtherStruct", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var structSymbol = model.AllSymbols.First(s => s.Name == "MyStruct" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);
        Assert.Empty(structType.Interfaces);
    }

    [Fact]
    public void InterfaceReferencingStructName_EmitsNotAnInterfaceDiagnostic()
    {
        var structDecl = MakeStructDecl("SomeStruct");
        var interfaceDecl = MakeInterfaceDecl("IChild",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
            "SomeStruct");

        var ast = MakeCompilationUnit(WrapStruct(structDecl), WrapInterface(interfaceDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_NOT_AN_INTERFACE");
        Assert.Contains("SomeStruct", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);

        var interfaceSymbol = model.AllSymbols.First(s => s.Name == "IChild" && s.Kind == SymbolKind.Interface);
        var interfaceType = Assert.IsType<InterfaceType>(interfaceSymbol.DeclaredType);
        Assert.Empty(interfaceType.Interfaces);
    ;
    }

}
