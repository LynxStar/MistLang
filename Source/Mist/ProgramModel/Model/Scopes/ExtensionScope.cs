using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// An extension block: its own type parameters and the methods written inside it. No state, since an
/// extension block has none, and not a boundary, since it is not a type.
/// <para>
/// A method's own type parameters are not here. They live in the <see cref="MethodScope"/> beneath
/// this one, which is what distinguishes <c>extension&lt;T&gt;(Foo&lt;T&gt; self)</c> from a generic
/// method inside a non-generic block.
/// </para>
/// </summary>
internal sealed class ExtensionScope(Scope? parent) : Scope(parent)
{
    /// <summary>
    /// Null only between this scope's construction and the block's, which
    /// <see cref="ExtensionBlock"/>'s constructor closes before anything is resolved inside.
    /// </summary>
    public ExtensionBlock? Block { get; private set; }

    internal void AttachBlock(ExtensionBlock block)
    {
        Block = block;
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(Block is null)
        {
            return new NotFoundResult();
        }

        var candidates = new CandidateSet();

        // The receiver is INSTANCE state, which is the whole of the rule that a static extension
        // member cannot see it: MethodScope narrows an outward request to StaticOnly for a static
        // member, and Permits then answers false here. No pass judges a finished body for it.
        if(Block.Receiver is { } receiver && receiver.Name == request.Name && request.Permits(isStatic: false))
        {
            candidates.Add(new VariableResult(receiver));
        }

        // A type parameter is neither instance nor static state, so visibility does not apply.
        foreach(var typeParameter in Block.TypeParametersList)
        {
            if(typeParameter.Name == request.Name)
            {
                candidates.Add(new TypeResult(typeParameter));
            }
        }

        var group = new List<Method>();
        foreach(var method in Block.MethodsList)
        {
            if(method.Name == request.Name && request.Permits(method.Modifiers.IsStatic))
            {
                group.Add(method);
            }
        }

        if(group.Count > 0)
        {
            candidates.Add(new MethodGroupResult(group));
        }

        return candidates.Resolve();
    }
}