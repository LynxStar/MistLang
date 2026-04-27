using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Tests that the parser correctly handles rules starting with nullable
/// prefixes (ZeroOrMore, Optional). Validates the fix for the bug where
/// FIRST set computation returned empty sets for rules referenced via
/// stale IR shells, causing ZeroOrMore loops to produce empty lists.
/// </summary>
public class NullablePrefixParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    [Fact]
    public void FileScopedNamespace_WithPublicClass_BodyIsNotEmpty()
    {
        var source = "namespace HelloMist;\npublic class Program { }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Root);

        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.Namespace as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.True(ns!.Body.Length > 0, "FileScopedNamespace.Body should contain the class declaration");

        var typeDecl = ns.Body[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal("Program", typeDecl!.ClassDeclaration.Name.Lexeme);
    }

    [Fact]
    public void ClassWithModifiers_ModifiersAreParsed()
    {
        var source = "public static class MyClass { }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);
        Assert.True(cu!.Types.Length > 0);

        var typeDecl = cu.Types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal(2, typeDecl!.ClassDeclaration.Modifiers.Length);
        Assert.IsType<PublicModifierModifierNode>(typeDecl.ClassDeclaration.Modifiers[0]);
        Assert.IsType<StaticModifierModifierNode>(typeDecl.ClassDeclaration.Modifiers[1]);
    }

    [Fact]
    public void ClassWithMembers_MembersAreParsed()
    {
        var source = "public class Program { public int x; private int y; }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var typeDecl = cu!.Types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal(2, typeDecl!.ClassDeclaration.Members.Length);
    }

    [Fact]
    public void FileScopedNamespace_WithMultipleTypesIncludingModifiers_AllTypesPresent()
    {
        var source = "namespace Test;\npublic class Foo { }\nprivate struct Bar { }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.Namespace as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.Equal(2, ns!.Body.Length);

        var classDecl = ns.Body[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(classDecl);
        Assert.Equal("Foo", classDecl!.ClassDeclaration.Name.Lexeme);

        var structDecl = ns.Body[1] as StructTypeTypeDeclarationNode;
        Assert.NotNull(structDecl);
        Assert.Equal("Bar", structDecl!.StructDeclaration.Name.Lexeme);
    }

    [Fact]
    public void HelloMistProgram_ParsesNamespaceAndClass()
    {
        var source = @"using System;

namespace HelloMist;

public class Program
{
    public static void Main(string[] args)
    {
    }
}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        Assert.Single(cu!.Usings);

        var ns = cu.Namespace as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);

        Assert.True(ns!.Body.Length > 0, "Namespace body should contain the Program class");
        var typeDecl = ns.Body[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        Assert.Equal("Program", typeDecl!.ClassDeclaration.Name.Lexeme);
        Assert.True(typeDecl.ClassDeclaration.Members.Length > 0, "Class should have members");
    }

    [Fact]
    public void ConstructorWithAssignment_ParsesCorrectly()
    {
        var source = "class C { int x; C(int a) { x = a; } }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);
        Assert.True(cu!.Types.Length > 0);

        var typeDecl = cu.Types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        var classDecl = typeDecl!.ClassDeclaration;
        Assert.Equal("C", classDecl.Name.Lexeme);
        Assert.Equal(2, classDecl.Members.Length);

        // First member: field
        var field = classDecl.Members[0] as FieldMemberMemberDeclarationNode;
        Assert.NotNull(field);

        // Second member: constructor
        var ctor = classDecl.Members[1] as ConstructorMemberMemberDeclarationNode;
        Assert.NotNull(ctor);
        Assert.Equal("C", ctor!.Constructor.Name.Lexeme);
        Assert.True(ctor.Constructor.Body.Statements.Length > 0, "Constructor body should have statements");
    }

    [Fact]
    public void ConstructorWithThisAssignment_ParsesCorrectly()
    {
        var source = "class C { int x; C(int x) { this.x = x; } }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var typeDecl = cu!.Types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        var classDecl = typeDecl!.ClassDeclaration;
        Assert.Equal(2, classDecl.Members.Length);

        var ctor = classDecl.Members[1] as ConstructorMemberMemberDeclarationNode;
        Assert.NotNull(ctor);
        Assert.True(ctor!.Constructor.Body.Statements.Length > 0, "Constructor body should have this.x = x statement");
    }

    [Fact]
    public void ClassWithConstructorAndMethod_BothParsed()
    {
        var source = "class C { int x; C(int a) { x = a; } int GetX() { return x; } }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var typeDecl = cu!.Types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        var classDecl = typeDecl!.ClassDeclaration;
        Assert.Equal(3, classDecl.Members.Length);

        Assert.IsType<FieldMemberMemberDeclarationNode>(classDecl.Members[0]);
        Assert.IsType<ConstructorMemberMemberDeclarationNode>(classDecl.Members[1]);
        Assert.IsType<MethodMemberMemberDeclarationNode>(classDecl.Members[2]);
    }

    [Fact]
    public void PointMistSample_ParsesWithNonEmptyNamespaceBody()
    {
        // Full Point.mist sample with namespace, multiple fields, constructor with 2 params, and methods
        var source = @"namespace Geometry;
public class Point
{
    public int x;
    public int y;
    public Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
    public int GetX()
    {
        return this.x;
    }
    public int GetY()
    {
        return this.y;
    }
}";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);

        var ns = cu!.Namespace as FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(ns);
        Assert.True(ns!.Body.Length > 0, "Namespace body should contain the Point class");

        var typeDecl = ns.Body[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(typeDecl);
        var classDecl = typeDecl!.ClassDeclaration;
        Assert.Equal("Point", classDecl.Name.Lexeme);

        // 2 fields + 1 constructor + 2 methods = 5 members
        Assert.Equal(5, classDecl.Members.Length);
    }
}
