using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A declared cast operator as one type sees it. A generic type's operator is written in terms of its own
/// type parameters, so the parameter a call's argument must reach and the result it produces are the
/// declaration's, substituted through the arguments of the constructed type the operator was found on.
/// </summary>
internal sealed record ConversionFunction(CastOperator Operator, IMistType Parameter, IMistType Result);