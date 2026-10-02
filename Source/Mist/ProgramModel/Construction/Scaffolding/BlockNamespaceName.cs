using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;

namespace Mist.ProgramModel.Construction.Scaffolding;

internal static class BlockNamespaceName
{
    public static string Build(BlockNamespaceNamespaceDeclarationNode declaration, string parentNamespace)
    {
        var declaredName = QualifiedNameHelper.Build(declaration.Name);
        return string.IsNullOrEmpty(parentNamespace) ? declaredName : $"{parentNamespace}.{declaredName}";
    }

    /// <summary>
    /// The declared names of a file's outermost block namespaces, in declaration order. A block inside
    /// a block is left to its parent: naming the outer one already names what to change.
    /// </summary>
    public static ImmutableArray<string> DeclaredIn(CompilationUnitNode ast)
    {
        var names = ImmutableArray.CreateBuilder<string>();

        foreach(var declaration in ast.Items.OfType<NamespaceItemTopLevelItemNode>().Select(item => item.NamespaceDeclaration))
        {
            switch(declaration)
            {
                case BlockNamespaceNamespaceDeclarationNode block:
                    names.Add(QualifiedNameHelper.Build(block.Name));
                    break;

                case FileScopedNamespaceNamespaceDeclarationNode fileScoped:
                    names.AddRange(fileScoped.Body
                        .OfType<NamespaceNamespaceItemNamespaceBodyItemNode>()
                        .Select(nested => nested.NamespaceDeclaration)
                        .OfType<BlockNamespaceNamespaceDeclarationNode>()
                        .Select(nested => QualifiedNameHelper.Build(nested.Name)));
                    break;
            }
        }

        return names.ToImmutable();
    }
}