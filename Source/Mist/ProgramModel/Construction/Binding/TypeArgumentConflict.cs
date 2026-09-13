using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal sealed record TypeArgumentConflict(TypeParameter Parameter, IReadOnlyList<IMistType> Bounds);