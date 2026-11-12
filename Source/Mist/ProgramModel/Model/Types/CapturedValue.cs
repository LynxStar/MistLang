using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A dependent type argument whose value is known only when the program runs: the third thing a slot
/// holds beside a <see cref="LiftedType"/> and an <see cref="UnknownValueType"/>. It stands for the one
/// value its <see cref="Capture"/> was given, so two of them are the same type argument only when they
/// are the same capture, however alike they were written. The name carries the position for that
/// reason: a constructed type is compared by qualified name, which interpolates its arguments' names.
/// </summary>
internal sealed class CapturedValue : MistType
{
    public CapturedValue(SlotCapture capture, IMistType domain, int position)
        : base($"{capture.Name}@{position}", new Scope(null), declaringType: null)
    {
        Capture = capture;
        Domain = domain;
    }

    public SlotCapture Capture { get; }

    public IMistType Domain { get; }

    public string WrittenName => Capture.Name;
}