// Feature: inferred-namespaces, Property 12: Non-Existent Namespace Using Diagnostic
// For any using declaration that names a namespace not defined in the compilation
// (and not provided by the ambient type resolver), the semantic model builder shall
// produce a diagnostic.

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that using declarations referencing non-existent
/// namespaces produce a diagnostic.
/// **Validates: Requirements 7.2**
/// </summary>
public sealed class UndefinedNamespaceDiagnosticPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record NonExistentNamespace(string Value)
    {
        public override string ToString() => Value;
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly char[] IdentifierStartChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private static readonly char[] IdentifierChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static Arbitrary<NonExistentNamespace> ArbitraryNonExistentNamespace()
    {
        // Generate random namespace names that won't exist in the compilation.
        // Use a prefix to ensure they don't collide with "TestProject" (the inferred namespace).
        var gen = from first in Gen.Elements(IdentifierStartChars)
                  from restLen in Gen.Choose(2, 10)
                  from rest in Gen.ArrayOf(Gen.Elements(IdentifierChars), restLen)
                  from hasDot in Gen.Elements(true, false)
                  from suffix in hasDot
                      ? (from sf in Gen.Elements(IdentifierStartChars)
                         from suffixLen in Gen.Choose(1, 5)
                         from sr in Gen.ArrayOf(Gen.Elements(IdentifierChars), suffixLen)
                         select "." + sf + new string(sr))
                      : Gen.Constant("")
                  select new NonExistentNamespace("Fake" + first + new string(rest) + suffix);
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

    private static ClassDeclarationNode MakeClass(string name)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static UsingDeclarationNode MakeUsingDecl(string nsName, int start = 6)
    {
        var firstSegment = nsName.Contains('.') ? nsName[..nsName.IndexOf('.')] : nsName;
        return new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(firstSegment, start: start),
                Span = new Storm.Runtime.SourceSpan(start, nsName.Length),
            },
        };
    }

    // ─── Property 12: Non-existent namespace using produces diagnostic ──

    /// <summary>
    /// When a using declaration references a namespace that doesn't exist in the
    /// compilation, a MIST_UNDEFINED_NAMESPACE diagnostic should be produced.
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedNamespaceDiagnosticPropertyTests) }, MaxTest = 100)]
    public bool NonExistentNamespace_ProducesDiagnostic(NonExistentNamespace fakeNs)
    {
        // using FakeNs; class C { }
        var usingDecl = MakeUsingDecl(fakeNs.Value);
        var classDecl = MakeClass("C");

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var sourceText = new SourceText("/project/File.mist", $"using {fakeNs.Value}; class C {{ }}");
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();

        _ = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should have MIST_UNDEFINED_NAMESPACE diagnostic
        return diags.Any(d => d.Id.Value == "MIST_UNDEFINED_NAMESPACE");
    }
}