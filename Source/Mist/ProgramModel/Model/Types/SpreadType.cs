using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A pack written with the spread suffix (<c>TArgs...</c>), standing in one slot until the pack it
/// names is bound and it can become the several it stands for.
/// <para>
/// A MARKER RATHER THAN THE OPERAND ITSELF, because resolution happens once at the DECLARATION and
/// substitution happens later at each use. A declaration spreading TArgs resolves while TArgs is
/// still open, so if the spread resolved to the pack itself nothing would distinguish it from an
/// ordinary reference, and substitution would put one list where the elements belong -- which
/// resolves and emits, and silently makes every ordering of the same arguments one type.
/// </para>
/// </summary>
internal sealed class SpreadType : MistType
{
    public SpreadType(IMistType operand)
        : base($"{operand.Name}...", new Scope(null), declaringType: null)
    {
        Operand = operand;
    }

    public IMistType Operand { get; }
}