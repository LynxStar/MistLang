using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Every declared type a name in source denotes, with the type and assembly the name is written in.
/// Resolution keeps only what a name resolved to -- an alias is expanded, a nested type is reached through
/// its qualifier -- which is coherent to build on and leaves nothing behind to judge whether the name itself
/// was accessible where it was written; this is where the name survives. A clone's members are its
/// source's text bound again, so nothing is recorded under a <see cref="CloneScope"/>: the source's own
/// binding already recorded it.
/// </summary>
internal sealed class TypeNamingRegistry
{
    private readonly List<TypeNaming> namings = [];

    public IReadOnlyList<TypeNaming> Namings => namings;

    public void Record(IMistType named, Scope scope, SyntaxNode node)
    {
        if(named is not DeclaredType declared)
        {
            return;
        }

        MistType? owner = null;
        MistAssembly? assembly = null;

        for(var current = scope; current is not null; current = current.Parent)
        {
            switch(current)
            {
                case CloneScope:
                    return;

                case TypeScope type:
                    owner ??= type.Type;
                    break;

                case AssemblyScope unit:
                    assembly = unit.Assembly;
                    break;
            }
        }

        namings.Add(new TypeNaming(declared, node, owner, assembly));
    }
}

internal sealed record TypeNaming(DeclaredType Named, SyntaxNode Node, MistType? Owner, MistAssembly? Assembly);