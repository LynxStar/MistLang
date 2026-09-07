using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Fills in <see cref="TypeParameter.Domain"/> -- the type whose values fill the slot, which is
/// <c>Standard.Type</c> for an ordinary parameter and the annotated value type for a dependent one.
/// <para>
/// A separate pass because a parameter is CREATED before Standard is registered and cannot resolve
/// anything then, while every later consumer wants to ask which domain rather than merely whether
/// there is one. What crosses that gap is a well-known descriptor or a name, never the annotation's
/// syntax; see <see cref="TypeParameter.DomainPrimitive"/>.
/// </para>
/// </summary>
internal sealed class TypeParameterDomainResolver
{
    private readonly ScopeResolver resolver;

    public TypeParameterDomainResolver(ScopeResolver resolver)
    {
        this.resolver = resolver;
    }

    public void Resolve(IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach(var typeParameter in typeParameters)
        {
            if(Resolve(typeParameter) is MistType domain)
            {
                typeParameter.Domain = domain;
            }
        }
    }

    private IMistType? Resolve(TypeParameter typeParameter)
    {
        // A pack ranges over Type[], which is the whole of why `params TArgs` is the dependent
        // parameter `Type[]:TArgs` rather than a kind of its own. A fixed-size domain pins the
        // arity here, the same way a sized array's length does.
        if(typeParameter.PackKind != TypePackKind.None)
        {
            return NotError(
                resolver.ConstructArray(
                    resolver.LookupWellKnown(WellKnownTypes.Type), typeParameter.PackLength));
        }

        if(typeParameter.DomainPrimitive is { } primitive)
        {
            return NotError(resolver.LookupWellKnown(primitive));
        }

        if(typeParameter.DomainTypeName is { } name)
        {
            return typeParameter.Scope.Lookup(new LookupRequest(name, MemberVisibility.None, null))
                is TypeResult found
                    ? NotError(found.Type)
                    : null;
        }

        return NotError(resolver.LookupWellKnown(WellKnownTypes.Type));
    }

    /// <summary>
    /// Leaves an unresolvable domain UNSET rather than recording <see cref="ErrorType"/>, so that
    /// "this domain could not be resolved" stays the same answer a consumer already handles today
    /// and no caller gains an error type it was not written to expect.
    /// </summary>
    private static IMistType? NotError(IMistType type)
    {
        return type is ErrorType ? null : type;
    }
}