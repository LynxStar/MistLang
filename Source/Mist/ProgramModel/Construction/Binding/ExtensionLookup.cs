using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What an extension block offers a receiver, consulted only when the receiver's own members do not
/// answer. Own members always win, which needs no rule here: this runs on the miss path.
/// <para>
/// VISIBILITY IS A PROPERTY OF THE CALL SITE, not of the program. A block is reachable only from a
/// file that is in its namespace or imports it, so the same receiver answers differently in two
/// files and the scope chain of the USE is what decides.
/// </para>
/// </summary>
internal static class ExtensionLookup
{
    public static LookupResult Find(
        IMistType receiver, string memberName, MemberVisibility visibility, Scope callSite, ProgramModelClass programModel)
    {
        if(receiver is ErrorType or NotProjectedType)
        {
            return new NotFoundResult();
        }

        var applicable = new List<ExtensionBlock>();

        foreach(var namespaceName in VisibleNamespaces(callSite))
        {
            if(!programModel.Namespaces.TryGetValue(namespaceName, out var ns))
            {
                continue;
            }

            foreach(var (blockReceiver, blocks) in ns.Extensions)
            {
                if(!Accepts(blockReceiver, receiver))
                {
                    continue;
                }

                foreach(var block in blocks)
                {
                    // NAME FIRST, RANK SECOND. Ranking the blocks and then looking for the member
                    // inside the winners loses every universal member the moment a concrete block
                    // exists for the same receiver: the concrete one is more specific, wins the
                    // tier, and declares none of the names the universal one does.
                    if(Declares(block, memberName, visibility))
                    {
                        applicable.Add(block);
                    }
                }
            }
        }

        if(applicable.Count == 0)
        {
            return new NotFoundResult();
        }

        var winners = MostSpecific(applicable);
        var methods = new List<Method>();

        foreach(var block in winners)
        {
            foreach(var method in block.MethodsList)
            {
                if(method.Name == memberName && Permits(visibility, method.Modifiers.IsStatic))
                {
                    methods.Add(method);
                }
            }
        }

        if(methods.Count > 0)
        {
            return new MethodGroupResult(methods);
        }

        // A PROPERTY IS FOUND AND THEN DELIBERATELY NOT ANSWERED, so the access reports unresolved
        // rather than half-resolving. C# has no extension property: the site has to be reclassified
        // into an invocation of the carrier's generated getter, and nothing does that yet. Returning
        // the property here instead would type the expression from a member no emitted call reaches.
        return new NotFoundResult();
    }

    /// <summary>
    /// The namespaces a use site can see: every namespace it is written inside, everything its file
    /// or its project imports, and the global one. Walked outward from the use rather than read off
    /// the enclosing type, so a block declared beside a caller is reachable without an import.
    /// </summary>
    private static HashSet<string> VisibleNamespaces(Scope callSite)
    {
        var visible = new HashSet<string>(StringComparer.Ordinal) { "" };

        for(var scope = callSite; scope is not null; scope = scope.Parent)
        {
            if(scope is NamespaceScope namespaceScope)
            {
                _ = visible.Add(namespaceScope.NamespaceName);
            }

            if(scope is ImportingScope importing)
            {
                foreach(var import in importing.Imports)
                {
                    _ = visible.Add(import);
                }
            }
        }

        return visible;
    }

    /// <summary>
    /// Whether a block declared for <paramref name="blockReceiver"/> applies to an actual receiver.
    /// <c>unknown</c> accepts everything, which is what makes a universal extension universal, and
    /// is also why it must lose every ranking it takes part in.
    /// </summary>
    private static bool Accepts(IMistType blockReceiver, IMistType receiver)
    {
        return blockReceiver is UnknownType
            || ConversionRelation.IsIdentity(receiver, blockReceiver)
            || ConversionRelation.IsWithin(receiver, blockReceiver);
    }

    private static bool Permits(MemberVisibility visibility, bool isStatic)
    {
        return visibility switch
        {
            MemberVisibility.InstanceAndStatic => true,
            MemberVisibility.StaticOnly => isStatic,
            MemberVisibility.InstanceOnly => !isStatic,
            _ => false,
        };
    }

    private static bool Declares(ExtensionBlock block, string memberName, MemberVisibility visibility)
    {
        foreach(var method in block.MethodsList)
        {
            if(method.Name == memberName && Permits(visibility, method.Modifiers.IsStatic))
            {
                return true;
            }
        }

        foreach(var property in block.PropertiesList)
        {
            if(property.Name == memberName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The blocks no other block beats, pairwise. A block loses when another's receiver is strictly
    /// contained in its own -- <c>Dog</c> beats <c>Animal</c>, and anything beats <c>unknown</c>.
    /// <para>
    /// Pairwise rather than a two-tier concrete-versus-universal split, because the split gets a
    /// base and a derived receiver wrong in a way that does not report: both survive, the binder
    /// records one overload and C# resolves the other, and the disagreement only surfaces if their
    /// return types differ.
    /// </para>
    /// </summary>
    private static List<ExtensionBlock> MostSpecific(List<ExtensionBlock> blocks)
    {
        var winners = new List<ExtensionBlock>();

        foreach(var block in blocks)
        {
            var beaten = false;

            foreach(var other in blocks)
            {
                if(!ReferenceEquals(block, other) && Beats(other, block))
                {
                    beaten = true;
                    break;
                }
            }

            if(!beaten)
            {
                winners.Add(block);
            }
        }

        return winners;
    }

    private static bool Beats(ExtensionBlock candidate, ExtensionBlock incumbent)
    {
        if(candidate.ResolvedReceiverType is not { } mine || incumbent.ResolvedReceiverType is not { } theirs)
        {
            return false;
        }

        if(mine is UnknownType)
        {
            return false;
        }

        if(theirs is UnknownType)
        {
            return true;
        }

        return ConversionRelation.IsWithin(mine, theirs) && !ConversionRelation.IsWithin(theirs, mine);
    }
}