using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Token = Storm.Runtime.Token;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal enum MemberKind
{
    Field,
    Property,
    Method,
    Constructor,
    Type,
    Namespace,
    Other,
}

internal sealed record CodeMember(MemberKind Kind, string Name, SourceSpan NameSpan, string? TypeText);

internal static class CodeBlockMembers
{
    private const string Prefix = "class __CodeBlock\n{\n";

    public static ImmutableArray<CodeMember> Read(CodeBlock block, ILexer lexer, IParser parser, DiagnosticOperation operation)
    {
        var source = new SourceText(block.Body.Text, Prefix + block.Body.Text + "\n}");
        var shift = block.Body.Span.Start - Prefix.Length;
        var bag = new DiagnosticBag(operation);
        var tokens = lexer.Lex(source, bag);
        var parsed = bag.HasErrors ? null : parser.Parse(tokens, bag);
        foreach(var diagnostic in parsed?.Diagnostics ?? [])
        {
            bag.Add(diagnostic);
        }

        if(parsed is null || bag.HasErrors
            || parsed.Root is not CompilationUnitNode { Items: [TypeItemTopLevelItemNode { TypeDeclaration: BodyTypeTypeDeclarationNode body }] })
        {
            return Namespaces(tokens.DefaultChannelTokens, shift);
        }

        return [.. body.TypeBody.Members.Select(member => Member(member, source, shift)).OfType<CodeMember>()];
    }

    private static CodeMember? Member(MemberDeclarationNode member, SourceText source, int shift)
    {
        return member switch
        {
            ConstructorMemberMemberDeclarationNode { Constructor.Name: var name } => Named(MemberKind.Constructor, name, shift),
            MethodMemberMemberDeclarationNode { Method.Name: var name } => Named(MemberKind.Method, name, shift),
            FieldMemberMemberDeclarationNode { Field: var field } => Named(MemberKind.Field, field.Name, shift, TypeText(field.Type, source)),
            PropertyMemberMemberDeclarationNode { Property: BlockPropertyPropertyDeclarationNode property }
                => Named(MemberKind.Property, property.Name, shift, TypeText(property.Type, source)),
            PropertyMemberMemberDeclarationNode { Property: ExpressionBodiedPropertyPropertyDeclarationNode property }
                => Named(MemberKind.Property, property.Name, shift, TypeText(property.Type, source)),
            NestedBodyTypeMemberMemberDeclarationNode { NestedBody.Name: var name } => Named(MemberKind.Type, name, shift),
            NestedEnumMemberMemberDeclarationNode { NestedEnum.Name: var name } => Named(MemberKind.Type, name, shift),
            NestedNewtypeMemberMemberDeclarationNode { NestedNewtype.Name: var name } => Named(MemberKind.Type, name, shift),
            NestedTypeAliasMemberMemberDeclarationNode { NestedTypeAlias.Name: var name } => Named(MemberKind.Type, name, shift),
            AssociatedTypeMemberMemberDeclarationNode { AssociatedType.Name: var name } => Named(MemberKind.Type, name, shift),
            _ => null,
        };
    }

    private static CodeMember Named(MemberKind kind, Token name, int shift, string? typeText = null)
    {
        return new CodeMember(kind, name.Lexeme, new SourceSpan(name.Span.Start + shift, name.Span.Length), typeText);
    }

    private static string TypeText(TypeReferenceNode type, SourceText source)
    {
        return source.Contents.Substring(type.Span.Start, type.Span.Length).Trim();
    }

    private static ImmutableArray<CodeMember> Namespaces(ImmutableArray<Token> tokens, int shift)
    {
        var members = ImmutableArray.CreateBuilder<CodeMember>();
        var depth = 0;
        for(var index = 0; index < tokens.Length; index++)
        {
            var lexeme = tokens[index].Lexeme;
            depth += lexeme switch
            {
                "{" => 1,
                "}" => -1,
                _ => 0,
            };
            if(depth == 1 && lexeme == "namespace" && index + 1 < tokens.Length)
            {
                members.Add(Named(MemberKind.Namespace, tokens[index + 1], shift));
            }
        }

        return members.ToImmutable();
    }
}