using Mist.ProgramModel.Construction.Resolution;
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
        IMistType receiver,
        string memberName,
        MemberVisibility visibility,
        Scope callSite,
        bool allowProperties,
        ProgramModelClass programModel)
    {
        if(receiver is ErrorType or NotProjectedType)
        {
            return new NotFoundResult();
        }

        var applicable = new List<ExtensionBlock>();
        var closures = new Dictionary<ExtensionBlock, IReadOnlyList<IMistType>>();

        foreach(var namespaceName in VisibleNamespaces(callSite))
        {
            if(!programModel.Namespaces.TryGetValue(namespaceName, out var ns))
            {
                continue;
            }

            foreach(var (blockReceiver, blocks) in ns.Extensions)
            {
                foreach(var block in blocks)
                {
                    // Per BLOCK rather than per receiver key, because a generic block's own
                    // parameters are what its receiver has to be read against and two blocks filed
                    // under one key need not agree on them.
                    if(!Accepts(blockReceiver, receiver, block, programModel.TypeInterner))
                    {
                        continue;
                    }

                    // NAME FIRST, RANK SECOND. Ranking the blocks and then looking for the member
                    // inside the winners loses every universal member the moment a concrete block
                    // exists for the same receiver: the concrete one is more specific, wins the
                    // tier, and declares none of the names the universal one does.
                    if(Declares(block, memberName, visibility))
                    {
                        applicable.Add(block);

                        if(Closed(blockReceiver, receiver, block, programModel.TypeInterner) is { } arguments)
                        {
                            closures[block] = arguments;
                        }
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
                    methods.Add(closures.TryGetValue(block, out var arguments)
                        ? ExtensionSpecialization.Close(method, block, arguments, programModel.TypeInterner)
                        : method);
                }
            }
        }

        if(methods.Count > 0)
        {
            return new MethodGroupResult(methods);
        }

        // Answered only where the caller can RECLASSIFY the access into a call to the carrier's
        // getter. A caller that cannot -- a conditional access, whose short-circuit has no lowering
        // here -- gets a miss instead, so the site reports unresolved rather than being typed from a
        // member no emitted call reaches.
        if(allowProperties)
        {
            foreach(var block in winners)
            {
                foreach(var property in block.PropertiesList)
                {
                    if(property.Name == memberName)
                    {
                        return new ExtensionPropertyResult(property, block);
                    }
                }
            }
        }

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
    private static bool Accepts(
        IMistType blockReceiver, IMistType receiver, ExtensionBlock block, TypeInterner interner)
    {
        return blockReceiver is UnknownType
            || ConversionRelation.IsIdentity(receiver, blockReceiver)
            || ConversionRelation.IsWithinAsItself(receiver, blockReceiver)
            || Closed(blockReceiver, receiver, block, interner) is not null;
    }

    /// <summary>
    /// The type arguments that make a GENERIC block's receiver the actual one, or null where it has
    /// none or they cannot be read off. <c>Count()</c> on an <c>int[]</c> reaches
    /// <c>TSource[]</c>, and nothing but the receiver says what TSource is -- an extension's
    /// arguments are bound after the member is found, so they cannot help.
    /// </summary>
    private static IReadOnlyList<IMistType>? Closed(
        IMistType blockReceiver, IMistType receiver, ExtensionBlock block, TypeInterner interner)
    {
        if(block.TypeParametersList.Count == 0)
        {
            return null;
        }

        // AGAINST THE SUPERTYPES TOO, not the receiver alone. `Count()` is declared for
        // IEnumerable<TSource> and called on an int[], so what unifies is not the receiver but the
        // interface it reaches -- which is also where TSource is found.
        foreach(var candidate in Reachable(receiver, interner))
        {
            if(TypeArgumentInference.Unify(blockReceiver, candidate, block.TypeParametersList) is { } closed)
            {
                return closed;
            }
        }

        return null;
    }

    /// <summary>
    /// The receiver and everything it IS, with its own type arguments carried down: an
    /// <c>int[]</c> reaches <c>IEnumerable&lt;int&gt;</c>, not <c>IEnumerable&lt;TValue&gt;</c>.
    /// </summary>
    private static List<IMistType> Reachable(IMistType receiver, TypeInterner interner)
    {
        var found = new List<IMistType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<IMistType>();
        pending.Enqueue(receiver);

        while(pending.Count > 0)
        {
            var current = pending.Dequeue();

            if(!seen.Add(current.FullyQualifiedName))
            {
                continue;
            }

            found.Add(current);

            var constructed = current as ConstructedType;
            var substitution = TypeSubstitution.ForReceiver(constructed);

            if((constructed?.Definition ?? current) is not MemberType member)
            {
                continue;
            }

            foreach(var baseType in member.BaseAbstractTypes)
            {
                pending.Enqueue(substitution.Apply(baseType, interner));
            }

            if(member is ConcreteType { BaseType: { } concreteBase })
            {
                pending.Enqueue(substitution.Apply(concreteBase, interner));
            }
        }

        return found;
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