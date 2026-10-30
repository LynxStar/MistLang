using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Which storage may hold a ref struct (ref-structs Requirement 3): an instance field of another ref struct,
/// and nothing static. A property is refused in any class or struct that is not a ref struct, whether or not
/// it has storage, as Requirement 3.6 words it; a static one in a ref struct only when it has storage, since
/// a computed one stores nothing.
/// </summary>
internal static class RefStructPositions
{
    public static MistDiagnostic? Field(MemberType container, Field field)
    {
        if(!RefStructs.Is(field.Type))
        {
            return null;
        }

        if(field.Modifiers.IsStatic)
        {
            return ProgramAnalysisDiagnostics.RefStructStaticStorage;
        }

        return container is StructType { IsRef: true } ? null : ProgramAnalysisDiagnostics.RefStructField;
    }

    public static MistDiagnostic? Property(MemberType container, Property property)
    {
        if(!RefStructs.Is(property.Type))
        {
            return null;
        }

        if(container is ConcreteType and not StructType { IsRef: true })
        {
            return ProgramAnalysisDiagnostics.RefStructProperty;
        }

        return property.Modifiers.IsStatic && property.HasBackingStorage ? ProgramAnalysisDiagnostics.RefStructStaticStorage : null;
    }

    public static (string Position, string Reason) Element(CompositeKind kind)
    {
        return kind switch
        {
            CompositeKind.Tuple => ("a tuple element", "a tuple holds its elements as type arguments, which a ref struct cannot be"),
            CompositeKind.Array => ("an array element", "an array keeps its elements on the heap"),
            _ => throw new InvalidOperationException($"ICE: a {kind} has constituents, not elements."),
        };
    }
}