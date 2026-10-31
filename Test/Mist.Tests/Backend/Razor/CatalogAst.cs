using System.Collections.Immutable;
using System.Text;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend.Razor;

internal sealed class CatalogAst
{
    private readonly StringBuilder text = new();

    public CompilationUnit Unit(string relativePath, params TypeBodyDeclarationNode[] declarations)
    {
        return Unit(relativePath, [.. declarations], [], null);
    }

    public CompilationUnit Unit(
        string relativePath, ImmutableArray<TypeBodyDeclarationNode> declarations,
        ImmutableArray<string> usings = default, string? declaredNamespace = null)
    {
        var types = declarations.Select(declaration => (TypeDeclarationNode)new BodyTypeTypeDeclarationNode { TypeBody = declaration }).ToImmutableArray();
        var root = CompilationUnitNodeExtensions.CreateCompilationUnit(
            usings: [.. (usings.IsDefault ? [] : usings).Select(name => new UsingDeclarationNode { Name = Name(name) })],
            ns: declaredNamespace is null ? null : new FileScopedNamespaceNamespaceDeclarationNode { Name = Name(declaredNamespace), Body = [] },
            types: types);
        return Wrap(relativePath, root);
    }

    public CompilationUnit BlockUnit(string relativePath, string outer, string inner, TypeBodyDeclarationNode declaration)
    {
        return Wrap(relativePath, new CompilationUnitNode
        {
            Items = [new NamespaceItemTopLevelItemNode
            {
                NamespaceDeclaration = new BlockNamespaceNamespaceDeclarationNode
                {
                    Name = Name(outer),
                    Body = [new NamespaceNamespaceItemNamespaceBodyItemNode
                    {
                        NamespaceDeclaration = new BlockNamespaceNamespaceDeclarationNode
                        {
                            Name = Name(inner),
                            Body = [new NamespaceTypeItemNamespaceBodyItemNode
                            {
                                TypeDeclaration = new BodyTypeTypeDeclarationNode { TypeBody = declaration },
                            }],
                        },
                    }],
                },
            }],
        });
    }

    public static TypeBodyDeclarationNode Class(
        string name, BaseTypeRefNode? baseType = null, ImmutableArray<MemberDeclarationNode> members = default,
        ImmutableArray<string> typeParameters = default, TypeKeywordNode? keyword = null)
    {
        var declaration = new TypeBodyDeclarationNode
        {
            Keyword = keyword ?? new ClassKeywordTypeKeywordNode(),
            Name = MistTypeGenerator.MakeToken(name),
            Members = members.IsDefault ? [] : members,
            TypeParams = typeParameters.IsDefaultOrEmpty ? null : new TypeParameterListNode
            {
                TypeParameter = new RegularTypeParameterTypeParameterNode { Name = MistTypeGenerator.MakeToken(typeParameters[0]) },
                TypeParameters = [.. typeParameters.Skip(1).Select(parameter => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MistTypeGenerator.MakeToken(parameter) })],
            },
        };
        if(baseType is not null)
        {
            declaration.BaseList = new BaseListNode { First = baseType, BaseTypeRefs = [] };
        }

        return declaration;
    }

    public BaseTypeRefNode Base(string name, params string[] arguments)
    {
        return new BaseTypeRefNode
        {
            Name = Name(name),
            TypeArgs = arguments.Length == 0 ? null : new TypeArgumentListNode
            {
                TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Type(arguments[0]) },
                TypeArguments = [.. arguments.Skip(1).Select(argument => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = Type(argument) })],
            },
            Span = Append(name + (arguments.Length == 0 ? "" : "<" + string.Join(", ", arguments) + ">")),
        };
    }

    public BaseTypeRefNode NestedBase(string outer, string argument, string nested, params string[] arguments)
    {
        var result = Base(nested, arguments);
        result.Name = Name(outer);
        result.Enclosing = new BaseTypeEnclosingNode
        {
            OuterArgs = new TypeArgumentListNode
            {
                TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = Type(argument) },
                TypeArguments = [],
            },
            Nested = Name(nested),
        };
        result.Span = Append(outer + "<" + argument + ">." + nested + (arguments.Length == 0 ? "" : "<" + string.Join(", ", arguments) + ">"));
        return result;
    }

    public MemberDeclarationNode Property(string name, string type)
    {
        return new PropertyMemberMemberDeclarationNode
        {
            Property = new BlockPropertyPropertyDeclarationNode
            {
                Name = MistTypeGenerator.MakeToken(name),
                Type = Type(type),
                Attributes = [new AttributeListNode { Attribute = new AttributeNode { Name = Name("Parameter") }, Attributes = [] }],
            },
        };
    }

    private CompilationUnit Wrap(string relativePath, CompilationUnitNode root)
    {
        return new CompilationUnit(new SourceText(Path.Combine("/probe", relativePath), text.ToString()), "App", "/probe") { Ast = root };
    }

    private TypeReferenceNode Type(string name)
    {
        return new TypeReferenceNode { Span = Append(name) };
    }

    private Storm.Runtime.SourceSpan Append(string value)
    {
        var span = new Storm.Runtime.SourceSpan(text.Length, value.Length);
        _ = text.Append(value).Append('\n');
        return span;
    }

    private static QualifiedIdentifierNode Name(string name)
    {
        var parts = name.Split('.');
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MistTypeGenerator.MakeToken(parts[0]),
            IDENTIFIERs = [.. parts.Skip(1).Select(part => MistTypeGenerator.MakeToken(part))],
        };
    }
}