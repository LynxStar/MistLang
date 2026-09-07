using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

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
    /// <summary>
    /// The domains whose values can carry type identity. The line is drawn at EQUALITY, since a
    /// construction is identified by its argument: a domain whose equality is partial or
    /// approximate would make type identity so too, which is what rules the floating-point types
    /// out however reasonable they look. String is excluded by the spec without stated reason.
    /// </summary>
    private static readonly HashSet<string> CarryableDomains =
    [
        "Standard.Int32", "Standard.UInt32", "Standard.Int64", "Standard.UInt64",
        "Standard.Int16", "Standard.UInt16", "Standard.Byte", "Standard.SByte",
        "Standard.Boolean", "Standard.Char",
    ];

    private readonly BuilderContext context;
    private readonly ScopeResolver resolver;

    public TypeParameterDomainResolver(BuilderContext context, ScopeResolver resolver)
    {
        this.context = context;
        this.resolver = resolver;
    }

    /// <param name="location">
    /// Where a rejected domain is reported. Passed in rather than kept on the parameter, so a
    /// declaration's syntax reaches the diagnostic without reaching the model.
    /// </param>
    public void Resolve(IReadOnlyList<TypeParameter> typeParameters, SyntaxNode? location)
    {
        foreach(var typeParameter in typeParameters)
        {
            if(Resolve(typeParameter) is not MistType domain)
            {
                continue;
            }

            typeParameter.Domain = domain;

            // A pack is exempt because its domain is Type[] by construction rather than by
            // annotation, and what carries its identity is the element types it holds.
            if(location is not null
                && typeParameter.IsDependent
                && typeParameter.PackKind == TypePackKind.None
                && !CarryableDomains.Contains(Underlying(domain).FullyQualifiedName))
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.DisallowedDependentDomain, location, null, domain.Name);
            }
        }
    }

    /// <summary>
    /// A refinement is admitted by what it refines, so <c>ZeroOrPositiveInt</c> is as usable a
    /// domain as the <c>int</c> underneath it. An alias is transparent for the same reason, and it
    /// is the spelling the standard library actually uses -- <c>Array</c>'s length is declared
    /// against the alias, not against the refinement it names.
    /// </summary>
    private static IMistType Underlying(IMistType domain)
    {
        return domain switch
        {
            RefinedType refined => Underlying(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Underlying(aliased),
            _ => domain,
        };
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