using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

internal static class ComputedPropertyDecision
{
    public static bool IsComputed(Property property)
    {
        return property is { Setter: null, Getter.IsAuto: false };
    }

    public static bool RefusesField(Property property)
    {
        return property.UsesBackingField && IsComputed(property);
    }
}