using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A generic extension method with its BLOCK's type parameters closed by the receiver.
/// <para>
/// SUBSTITUTED AT LOOKUP, where the receiver is known and nothing downstream has a channel for it.
/// A method group carries the receiver's own construction so an ordinary member can substitute
/// against its declaring type -- but an extension's declaring type is the block, which the receiver
/// is not an instance of, so that channel says nothing here. Producing a closed copy instead is what
/// <see cref="SpreadProjection"/> already does for a member that exists per construction rather than
/// per declaration.
/// </para>
/// </summary>
internal static class ExtensionSpecialization
{
    public static Method Close(
        Method method,
        ExtensionBlock block,
        IReadOnlyList<IMistType> arguments,
        TypeInterner interner)
    {
        var substitution = TypeSubstitution.ForParameters(block.TypeParametersList, arguments);

        var parameters = method.ParametersList.ConvertAll(parameter =>
            new Parameter(parameter.Name, substitution.Apply(parameter.Type, interner), parameter.Modifier)
            {
                IsFlattened = parameter.IsFlattened,
            });

        var closed = new Method(
            method.Name,
            substitution.Apply(method.ReturnType, interner),
            parameters,
            method.Modifiers)
        {
            Scope = method.Scope,
            BlockClosing = substitution,
            ClosedBlock = block,
            SequenceLengthOperation = method.SequenceLengthOperation,
        };

        closed.TypeParametersList.AddRange(method.TypeParametersList);
        return closed;
    }
}