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
        method = method.SourceDeclaration ?? method;
        var substitution = TypeSubstitution.ForParameters(block.TypeParametersList, arguments);

        var parameters = method.ParametersList.ConvertAll(parameter =>
        {
            var closedParameter = new Parameter(parameter.Name, substitution.Apply(parameter.Type, interner), parameter.Modifier)
            {
                IsFlattened = parameter.IsFlattened,
                IsFailed = parameter.IsFailed,
                HasNativeDefault = parameter.HasNativeDefault,
            };
            closedParameter.AttributesList.AddRange(parameter.AttributesList);
            closedParameter.DeclaredAttributesList.AddRange(parameter.DeclaredAttributesList);
            return closedParameter;
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
            DeclaringBlock = block,
            SequenceLengthOperation = method.SequenceLengthOperation,
            SourceDeclaration = method,
            DeclaringType = method.DeclaringType,
            DeclarationNode = method.DeclarationNode,
            ReturnOwnership = method.ReturnOwnership,
            DeclaredThisTier = method.DeclaredThisTier,
        };

        closed.TypeParametersList.AddRange(method.TypeParametersList);
        closed.ContextBindingsList.AddRange(method.ContextBindingsList.Select(binding =>
            new ContextBinding(substitution.Apply(binding.ContextType, interner), binding.Identifier) { Node = binding.Node }));
        closed.AttributesList.AddRange(method.AttributesList);
        closed.DeclaredAttributesList.AddRange(method.DeclaredAttributesList);
        return closed;
    }
}