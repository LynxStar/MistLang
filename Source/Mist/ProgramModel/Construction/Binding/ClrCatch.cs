using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding;

internal static class ClrCatch
{
    public static bool Is(Method? method)
    {
        return method is { Modifiers: { IsIntrinsic: true, IsStatic: true }, DeclaringBlock: { } block }
            && Declares(block, method.Name);
    }

    public static bool Declares(IMethodHost host, string name)
    {
        if(name != "Catch" || host is not ExtensionBlock
            { NamespaceName: "Standard", ResolvedReceiverType.FullyQualifiedName: "System.Exception" })
        {
            return false;
        }

        for(var scope = host.Scope; scope is not null; scope = scope.Parent)
        {
            if(scope is AssemblyScope assembly)
            {
                return assembly.Assembly.Kind == MistAssemblyKind.Standard;
            }
        }

        return false;
    }

    public static IMistType Adapt(Method method, IMistType declared, Func<IMistType, IMistType> task, TypeInterner interner)
    {
        if(!Is(method) || ResultTypes.Of(declared) is not { } result || TaskResult.Of(result.Success) is not { } completion)
        {
            return declared;
        }

        return task(interner.GetOrCreateConstructedType(result.Type.Definition, [completion, result.Error]));
    }
}