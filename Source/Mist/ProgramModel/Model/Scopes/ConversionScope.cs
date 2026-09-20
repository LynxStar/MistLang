using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The body of a conversion run through a <c>Clone</c>, so the binder can tell it is inside one: the
/// body was written for a member, and two of its spellings mean something else here -- a <c>Clone</c>
/// of the operand is the conversion itself, and a test naming the result tests for the operand
/// (newtypes spec Requirement 10.4).
/// </summary>
internal sealed class ConversionScope(Scope parent) : Scope(parent)
{
    public CastOperator? Conversion { get; set; }

    public static CastOperator? Around(Scope? scope)
    {
        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is ConversionScope { Conversion: { } conversion })
            {
                return conversion;
            }
        }

        return null;
    }
}