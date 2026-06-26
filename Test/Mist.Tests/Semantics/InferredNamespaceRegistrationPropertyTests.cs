// Feature: inferred-namespaces, Property 4: Type Registration Under Inferred Namespace
// For any source file with no explicit namespace declaration, all types declared in
// that file shall have their ContainingNamespace set to the computed inferred namespace
// for that file's location.

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
/// Property-based tests verifying that types in files without explicit namespace
/// declarations are registered under the computed inferred namespace.
/// </summary>
public sealed class InferredNamespaceRegistrationPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidIdentifier(string Value)
    {
        public override string ToString() => Value;
    }

    public record ValidNamespaceRoot(string Value)
    {
        public override string ToString() => Value;
    }

#pragma warning disable CA1819
    public record FolderSegments(string[] Segments)
    {
        public override string ToString() => string.Join("/", Segments);
    }
#pragma warning restore CA1819
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly char[] IdentifierStartChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private static readonly char[] IdentifierChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    private static readonly char[] FolderChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static Arbitrary<ValidIdentifier> ArbitraryValidIdentifier()
    {
        var gen = from first in Gen.Elements(IdentifierStartChars)
                  from restLen in Gen.Choose(0, 10)
                  from rest in Gen.ArrayOf(Gen.Elements(IdentifierChars), restLen)
                  select new ValidIdentifier(first + new string(rest));
        return gen.ToArbitrary();
    }

    public static Arbitrary<ValidNamespaceRoot> ArbitraryValidNamespaceRoot()
    {
        var gen = from first in Gen.Elements(IdentifierStartChars)
                  from restLen in Gen.Choose(1, 15)
                  from rest in Gen.ArrayOf(Gen.Elements(IdentifierChars), restLen)
                  select new ValidNamespaceRoot(first + new string(rest));
        return gen.ToArbitrary();
    }

    public static Arbitrary<FolderSegments> ArbitraryFolderSegments()
    {
        var segmentGen = from len in Gen.Choose(1, 10)
                         from chars in Gen.ArrayOf(Gen.Elements(FolderChars), len)
                         select new string(chars);

        var gen = from count in Gen.Choose(0, 4)
                  from segments in Gen.ArrayOf(segmentGen, count)
                  select new FolderSegments(segments);
        return gen.ToArbitrary();
    }

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

    private static ISemanticModel BuildModelForFile(
        CompilationUnitNode ast,
        string namespaceRoot,
        string projectFolder,
        string filePath)
    {
        var sourceText = new SourceText(filePath, "");
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: namespaceRoot,
            projectFolder: projectFolder,
            diagnostics);
    }

    // ─── Property 4a: Single class at various paths ─────────────────────

    /// <summary>
    /// For any valid namespace root and file path, a class declared without an explicit
    /// namespace should have ContainingNamespace equal to the computed inferred namespace.
    /// **Validates: Requirements 3.6, 3.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceRegistrationPropertyTests) }, MaxTest = 100)]
    public bool ClassWithoutNamespace_HasInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidIdentifier className, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedNs = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className.Value),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath);

        var classSymbol = model.AllSymbols.FirstOrDefault(
            s => s.Name == className.Value && s.Kind == SymbolKind.Class);
        if(classSymbol is null)
        {
            return false;
        }

        var classType = classSymbol.DeclaredType as ClassType;
        return classType is not null && classType.ContainingNamespace == expectedNs;
    }

    // ─── Property 4b: Struct without namespace ──────────────────────────

    /// <summary>
    /// For any valid namespace root and file path, a struct declared without an explicit
    /// namespace should have ContainingNamespace equal to the computed inferred namespace.
    /// **Validates: Requirements 3.6, 3.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceRegistrationPropertyTests) }, MaxTest = 100)]
    public bool StructWithoutNamespace_HasInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidIdentifier structName, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedNs = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken(structName.Value),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }).AsTopLevelItems(),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath);

        var symbol = model.AllSymbols.FirstOrDefault(
            s => s.Name == structName.Value && s.Kind == SymbolKind.Struct);
        if(symbol is null)
        {
            return false;
        }

        var structType = symbol.DeclaredType as StructType;
        return structType is not null && structType.ContainingNamespace == expectedNs;
    }

    // ─── Property 4c: Abstract type without namespace ───────────────────────

    /// <summary>
    /// For any valid namespace root and file path, an abstract type declared without an explicit
    /// namespace should have ContainingNamespace equal to the computed inferred namespace.
    /// **Validates: Requirements 3.6, 3.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceRegistrationPropertyTests) }, MaxTest = 100)]
    public bool AbstractWithoutNamespace_HasInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidIdentifier abstractName, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedNs = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken(abstractName.Value),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl }).AsTopLevelItems(),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath);

        var symbol = model.AllSymbols.FirstOrDefault(
            s => s.Name == abstractName.Value && s.Kind == SymbolKind.Abstract);
        if(symbol is null)
        {
            return false;
        }

        var abstractType = symbol.DeclaredType as AbstractType;
        return abstractType is not null && abstractType.ContainingNamespace == expectedNs;
    }

    // ─── Property 4d: Multiple types all get same inferred namespace ────

    /// <summary>
    /// For any valid namespace root and file path, all types declared in a file without
    /// explicit namespaces should share the same inferred namespace.
    /// **Validates: Requirements 3.6, 3.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceRegistrationPropertyTests) }, MaxTest = 100)]
    public bool MultipleTypesWithoutNamespace_AllShareInferredNamespace(
        ValidNamespaceRoot nsRoot, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedNs = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Alpha"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Beta"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("IGamma"),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }
                },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new StructTypeTypeDeclarationNode { StructDeclaration = structDecl }
                },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl }
                }),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath);

        var classType = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Alpha" && s.Kind == SymbolKind.Class)
            ?.DeclaredType as ClassType;
        var structType = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Beta" && s.Kind == SymbolKind.Struct)
            ?.DeclaredType as StructType;
        var abstractType = model.AllSymbols
            .FirstOrDefault(s => s.Name == "IGamma" && s.Kind == SymbolKind.Abstract)
            ?.DeclaredType as AbstractType;

        return classType?.ContainingNamespace == expectedNs
            && structType?.ContainingNamespace == expectedNs
            && abstractType?.ContainingNamespace == expectedNs;
    }
}