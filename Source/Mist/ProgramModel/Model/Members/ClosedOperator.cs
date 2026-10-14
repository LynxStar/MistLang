using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A declared operator as an operand's type sees it, the counterpart of <see cref="ConversionFunction"/>. The
/// declaration keeps its identity and its one body; <see cref="Closing"/> carries the arguments of the
/// <see cref="Owner"/> it was found on and of every constructed base reached on the way, which is what its
/// parameters and result are read through here.
/// </summary>
internal sealed record ClosedOperator(
    Operator Declaration, IMistType Owner, TypeSubstitution Closing, IReadOnlyList<IMistType> ParameterTypes, IMistType ReturnType);