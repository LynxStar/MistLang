using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Driver;

internal static class TopLevelConstructs
{
    public static int Count(IProgramModel? model)
    {
        if(model is null)
        {
            return 0;
        }

        return model.Assemblies
            .Where(assembly => assembly.Kind is MistAssemblyKind.Self or MistAssemblyKind.Reference)
            .SelectMany(assembly => assembly.CompilationUnits)
            .SelectMany(unit => unit.Types)
            .Count(IsConstruct);
    }

    /// <summary>
    /// A newtype's clone replaces the newtype in its file as a class or struct, but was written as a
    /// newtype, so it is excluded here rather than counted by the kind it was built as.
    /// </summary>
    private static bool IsConstruct(DeclaredType type)
    {
        return type is (ClassType or StructType or AbstractType) and not ConcreteType { CloneSource: not null };
    }
}