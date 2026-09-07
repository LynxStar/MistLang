using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A pack written with the spread suffix (<c>TArgs...</c>), standing in one slot until the pack it
/// names is bound and it can become the several it stands for.
/// <para>
/// A MARKER RATHER THAN THE OPERAND ITSELF, because resolution happens once at the DECLARATION and
/// substitution happens later at each use. `type A&lt;params TArgs&gt; = F&lt;TArgs..., void&gt;`
/// resolves while TArgs is still open, so if the spread resolved to the pack itself nothing would
/// distinguish it from an ordinary reference and substitution would put one list where the elements
/// belong -- A&lt;int, string&gt; becoming F over a list holding a type parameter, which resolves
/// and emits and is silently the same type as A&lt;string, int&gt;.
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