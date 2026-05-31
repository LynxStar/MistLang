// Feature: generics-type-arguments, Property 3: Duplicate type parameter diagnostic
// **Validates: Requirements 6.5, 13.3**

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
/// Property-based tests verifying duplicate type parameter diagnostic emission.
///
/// Property 3: For any type or method declaration whose TypeParameterList contains
/// two or more identical identifier names, the semantic model builder SHALL emit a
/// MIST_DUPLICATE_TYPE_PARAMETER diagnostic error for each duplicate occurrence.
/// </summary>
public class DuplicateTypeParameterPropertyTests
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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
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

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a declaration with duplicate type parameter names.
    /// </summary>
#pragma warning disable CA1034
    public enum DuplicateDeclKind { Class, Struct, Interface, Method }

    public record DuplicateTypeParamDecl(
        DuplicateDeclKind Kind,
        string DeclName,
        ImmutableArray<string> TypeParamNames,
        int ExpectedDuplicateCount)
    {
        public override string ToString()
        {
            var kind = Kind.ToString().ToLowerInvariant();
            return $"{kind} {DeclName}<{string.Join(", ", TypeParamNames)}> (expected {ExpectedDuplicateCount} duplicate(s))";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] DeclNamePool = ["Alpha", "Beta", "Gamma", "Delta"];

    /// <summary>
    /// Generates a declaration with at least one duplicate type parameter name.
    /// Strategy: pick 2-4 type parameter slots, then ensure at least one name is repeated.
    /// </summary>
    public static Gen<DuplicateTypeParamDecl> GenDuplicateTypeParamDecl()
    {
        return from kind in Gen.Elements(
                   DuplicateDeclKind.Class,
                   DuplicateDeclKind.Struct,
                   DuplicateDeclKind.Interface,
                   DuplicateDeclKind.Method)
               from declName in Gen.Elements(DeclNamePool)
               from paramCount in Gen.Choose(2, 4)
               from duplicateName in Gen.Elements(TypeParamPool)
               from otherNames in Gen.Shuffle(TypeParamPool.Where(n => n != duplicateName).ToArray())
                   .Select(arr => arr.Take(paramCount - 2).ToArray())
               let names = BuildNamesWithDuplicate(duplicateName, otherNames, paramCount)
               let expectedDuplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Sum(g => g.Count() - 1)
               select new DuplicateTypeParamDecl(kind, declName, names, expectedDuplicates);
    }

    private static ImmutableArray<string> BuildNamesWithDuplicate(
        string duplicateName, string[] otherNames, int totalCount)
    {
        // Always include the duplicate name at least twice
        var names = new List<string> { duplicateName, duplicateName };

        // Fill remaining slots with other unique names
        foreach (var other in otherNames)
        {
            if (names.Count >= totalCount)
                break;
            names.Add(other);
        }

        // Ensure we have exactly totalCount names
        while (names.Count < totalCount)
            names.Add(duplicateName);

        return names.ToImmutableArray();
    }

    public static Arbitrary<DuplicateTypeParamDecl> ArbitraryDuplicateTypeParamDecl() =>
        GenDuplicateTypeParamDecl().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any type or method declaration whose TypeParameterList contains
    /// duplicate identifier names, the semantic model builder emits a
    /// MIST_DUPLICATE_TYPE_PARAMETER diagnostic for each duplicate occurrence.
    ///
    /// **Validates: Requirements 6.5, 13.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DuplicateTypeParameterPropertyTests) }, MaxTest = 100)]
    public bool DuplicateTypeParameters_EmitsDiagnosticForEachDuplicate(DuplicateTypeParamDecl decl)
    {
        Storm.Runtime.SyntaxNode ast;

        switch (decl.Kind)
        {
            case DuplicateDeclKind.Class:
            {
                var classDecl = new ClassDeclarationNode
                {
                    Name = MakeToken(decl.DeclName),
                    TypeParams = MakeTypeParamList(decl.TypeParamNames.ToArray()),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                };
                ast = MakeCompilationUnit(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
                break;
            }
            case DuplicateDeclKind.Struct:
            {
                var structDecl = new StructDeclarationNode
                {
                    Name = MakeToken(decl.DeclName),
                    TypeParams = MakeTypeParamList(decl.TypeParamNames.ToArray()),
                    Members = ImmutableArray<MemberDeclarationNode>.Empty,
                };
                ast = MakeCompilationUnit(
                    new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });
                break;
            }
            case DuplicateDeclKind.Interface:
            {
                var interfaceDecl = new InterfaceDeclarationNode
                {
                    Name = MakeToken(decl.DeclName),
                    TypeParams = MakeTypeParamList(decl.TypeParamNames.ToArray()),
                    Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
                };
                ast = MakeCompilationUnit(
                    new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl });
                break;
            }
            case DuplicateDeclKind.Method:
            {
                var method = new MethodDeclarationNode
                {
                    Name = MakeToken(decl.DeclName),
                    ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                    TypeParams = MakeTypeParamList(decl.TypeParamNames.ToArray()),
                    Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                };
                var classDecl = new ClassDeclarationNode
                {
                    Name = MakeToken("Host"),
                    Members = ImmutableArray.Create<MemberDeclarationNode>(
                        new MethodMemberMemberDeclarationNode { Method = method }),
                };
                ast = MakeCompilationUnit(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
                break;
            }
            default:
                throw new InvalidOperationException();
        }

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Count MIST_DUPLICATE_TYPE_PARAMETER diagnostics
        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        // The number of duplicate diagnostics should match the expected count
        return duplicateDiags.Count == decl.ExpectedDuplicateCount;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassWithDuplicateTypeParam_EmitsDiagnostic()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T", "T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        Assert.Single(duplicateDiags);
        Assert.Contains("T", duplicateDiags[0].Message);
    }

    [Fact]
    public void MethodWithDuplicateTypeParam_EmitsDiagnostic()
    {
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Transform"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            TypeParams = MakeTypeParamList(["T", "U", "T"]),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        Assert.Single(duplicateDiags);
    }

    [Fact]
    public void UniqueTypeParams_NoDuplicateDiagnostic()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        Assert.Empty(duplicateDiags);
    }

    [Fact]
    public void TripleDuplicate_EmitsTwoDiagnostics()
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Triple"),
            TypeParams = MakeTypeParamList(["T", "T", "T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var duplicateDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.DuplicateTypeParameter)
            .ToList();

        Assert.Equal(2, duplicateDiags.Count);
    }
}
