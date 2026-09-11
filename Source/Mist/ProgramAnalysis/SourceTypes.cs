using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The declarations the project's own source holds, scoped as emission is: source assemblies other
/// than Standard, whose code is the library's and would put a finding on a file nobody in the project
/// wrote. Nested types are absent from a unit's type list by design, so they are reached through
/// their declaring type.
/// </summary>
internal static class SourceTypes
{
    public static IEnumerable<CompilationUnit> UnitsOf(ProgramModelClass model)
    {
        foreach(var assembly in model.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                yield return unit;
            }
        }
    }

    public static IEnumerable<MistType> Of(ProgramModelClass model)
    {
        foreach(var unit in UnitsOf(model))
        {
            foreach(var type in unit.Types)
            {
                foreach(var declared in AndNested(type))
                {
                    yield return declared;
                }
            }
        }
    }

    public static IEnumerable<ExtensionBlock> ExtensionsOf(ProgramModelClass model)
    {
        foreach(var unit in UnitsOf(model))
        {
            foreach(var block in unit.Extensions)
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<MistType> AndNested(MistType type)
    {
        yield return type;

        foreach(var nested in type.NestedTypes)
        {
            foreach(var declared in AndNested(nested))
            {
                yield return declared;
            }
        }
    }
}