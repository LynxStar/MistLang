using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record MistClass(
    string FullName,
    ImmutableArray<string> BaseNames,
    ImmutableArray<string> Scope,
    ImmutableArray<ParameterFacts> Parameters,
    ImmutableArray<string> TypeParameters,
    bool IsComponentClass);

internal sealed record ComponentSource(string FullName, string? BaseTypeText, ComponentScope Scope, ImmutableArray<CodeBlock> CodeBlocks);

internal static class MistClassDeclarations
{
    private const string ComponentBase = "Microsoft.AspNetCore.Components.ComponentBase";
    private const string ComponentsNamespace = "Microsoft.AspNetCore.Components.";

    public static IEnumerable<MistClass> FromUnit(CompilationUnit unit, ImmutableArray<string> globalUsings)
    {
        if(unit.Ast is not CompilationUnitNode root)
        {
            return [];
        }

        var usings = root.Usings
            .Concat(root.Items.OfType<UsingItemTopLevelItemNode>().Select(item => item.UsingDeclaration))
            .Select(directive => QualifiedNameHelper.Build(directive.Name))
            .Concat(globalUsings.IsDefault ? [] : globalUsings)
            .ToImmutableArray();
        var fileScoped = root.Items.OfType<NamespaceItemTopLevelItemNode>()
            .Select(item => item.NamespaceDeclaration).OfType<FileScopedNamespaceNamespaceDeclarationNode>().FirstOrDefault();
        var fileNamespace = fileScoped is not null
            ? QualifiedNameHelper.Build(fileScoped.Name)
            : NamespaceBuilder.ComputeInferredNamespace(unit.SourcePath ?? unit.Source.Path, unit.NamespaceRoot, unit.ProjectFolder);

        var classes = new List<MistClass>();
        foreach(var item in root.Items)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode type:
                    Declare(type.TypeDeclaration, fileNamespace, [], unit.Source, usings, classes);
                    break;
                case NamespaceItemTopLevelItemNode { NamespaceDeclaration: FileScopedNamespaceNamespaceDeclarationNode declaration }:
                    DeclareBody(declaration.Body, fileNamespace, unit.Source, usings, classes);
                    break;
                case NamespaceItemTopLevelItemNode { NamespaceDeclaration: BlockNamespaceNamespaceDeclarationNode block }:
                    DeclareBlock(block, "", unit.Source, usings, classes);
                    break;
            }
        }

        return classes;
    }

    public static MistClass FromComponent(ComponentSource component, ILexer lexer, IParser parser, DiagnosticOperation operation)
    {
        var simpleName = component.FullName[(component.FullName.LastIndexOf('.') + 1)..];
        var parameters = component.CodeBlocks
            .SelectMany(block => ParseClassBody(simpleName, block, lexer, parser, operation))
            .ToImmutableArray();
        var baseText = component.BaseTypeText ?? ComponentBase;
        return new MistClass(component.FullName, [baseText], component.Scope.Namespaces, parameters, [], IsComponentClass: true);
    }

    private static IEnumerable<ParameterFacts> ParseClassBody(
        string className, CodeBlock block, ILexer lexer, IParser parser, DiagnosticOperation operation)
    {
        var source = new SourceText(className, $"class {className}\n{{\n{block.Body.Text}\n}}");
        var bag = new DiagnosticBag(operation);
        var parsed = parser.Parse(lexer.Lex(source, bag), bag);
        foreach(var diagnostic in parsed.Diagnostics)
        {
            bag.Add(diagnostic);
        }

        if(bag.HasErrors
            || parsed.Root is not CompilationUnitNode { Items: [TypeItemTopLevelItemNode { TypeDeclaration: BodyTypeTypeDeclarationNode body }] })
        {
            return [];
        }

        return Parameters(body.TypeBody.Members, source);
    }

    private static void DeclareBlock(
        BlockNamespaceNamespaceDeclarationNode block, string parentNamespace, SourceText source, ImmutableArray<string> usings, List<MistClass> classes)
    {
        DeclareBody(block.Body, BlockNamespaceName.Build(block, parentNamespace), source, usings, classes);
    }

    private static void DeclareBody(
        ImmutableArray<NamespaceBodyItemNode> body, string namespaceName, SourceText source, ImmutableArray<string> usings, List<MistClass> classes)
    {
        foreach(var item in body)
        {
            switch(item)
            {
                case NamespaceTypeItemNamespaceBodyItemNode type:
                    Declare(type.TypeDeclaration, namespaceName, [], source, usings, classes);
                    break;
                case NamespaceNamespaceItemNamespaceBodyItemNode { NamespaceDeclaration: BlockNamespaceNamespaceDeclarationNode block }:
                    DeclareBlock(block, namespaceName, source, usings, classes);
                    break;
            }
        }
    }

    private static void Declare(
        TypeDeclarationNode declaration, string namespaceName, ImmutableArray<string> enclosingTypeParameters,
        SourceText source, ImmutableArray<string> usings, List<MistClass> classes)
    {
        if(declaration is not BodyTypeTypeDeclarationNode { TypeBody: var body })
        {
            return;
        }

        var fullName = string.IsNullOrEmpty(namespaceName) ? body.Name.Lexeme : $"{namespaceName}.{body.Name.Lexeme}";
        var typeParameters = body.TypeParams is { } listParameters
            ? enclosingTypeParameters.AddRange(listParameters.TypeParameters.Prepend(listParameters.TypeParameter).Select(parameter => parameter.GetName().Lexeme))
            : enclosingTypeParameters;
        if(body.Keyword is ClassKeywordTypeKeywordNode)
        {
            var bases = body.BaseList is { } list
                ? list.BaseTypeRefs.Prepend(list.First).Select(entry => source.Contents.Substring(entry.Span.Start, entry.Span.Length)).ToImmutableArray()
                : [];
            var scope = Enclosing(namespaceName).Concat(usings).ToImmutableArray();
            classes.Add(new MistClass(fullName, bases, scope, [.. Parameters(body.Members, source)], typeParameters, IsComponentClass: false));
        }

        foreach(var nested in body.Members.OfType<NestedBodyTypeMemberMemberDeclarationNode>())
        {
            Declare(new BodyTypeTypeDeclarationNode { TypeBody = nested.NestedBody }, fullName, typeParameters, source, usings, classes);
        }
    }

    private static IEnumerable<string> Enclosing(string namespaceName)
    {
        for(var name = namespaceName; !string.IsNullOrEmpty(name); name = name.Contains('.') ? name[..name.LastIndexOf('.')] : "")
        {
            yield return name;
        }
    }

    private static IEnumerable<ParameterFacts> Parameters(ImmutableArray<MemberDeclarationNode> members, SourceText source)
    {
        foreach(var member in members)
        {
            if(member is not PropertyMemberMemberDeclarationNode { Property: var property })
            {
                continue;
            }

            var (attributes, type, name) = property switch
            {
                BlockPropertyPropertyDeclarationNode block => (block.Attributes, block.Type, block.Name),
                ExpressionBodiedPropertyPropertyDeclarationNode bodied => (bodied.Attributes, bodied.Type, bodied.Name),
                _ => default,
            };

            if(type is not null && attributes.SelectMany(list => list.Attributes.Prepend(list.Attribute)).Any(IsParameterAttribute))
            {
                yield return ParameterFacts.Declared(name.Lexeme, source.Contents.Substring(type.Span.Start, type.Span.Length).Trim());
            }
        }
    }

    private static bool IsParameterAttribute(AttributeNode attribute)
    {
        var name = QualifiedNameHelper.Build(attribute.Name);
        if(name.StartsWith(ComponentsNamespace, StringComparison.Ordinal))
        {
            name = name[ComponentsNamespace.Length..];
        }

        return name is "Parameter" or "ParameterAttribute" or "CascadingParameter" or "CascadingParameterAttribute";
    }
}