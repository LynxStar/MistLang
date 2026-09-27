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
}