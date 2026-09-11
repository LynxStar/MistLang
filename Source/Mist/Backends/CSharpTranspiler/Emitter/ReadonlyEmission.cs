using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Where an inferred readonly can be spelled in C#, which is narrower than where Mist infers one: on a
/// struct's INSTANCE members, and not on a readonly struct's, whose members C# already treats as
/// readonly. A class has no position for it at all. What it buys is Roslyn checking the claim and the
/// defensive copy of `this` going away; what it costs if the claim is wrong is a silent copy, which is
/// why a Drop is excluded -- the emitter appends field drops after the author's body, so its emitted
/// form writes storage the model never showed <see cref="ReadonlyInference"/>.
/// <para>
/// WHICH POSITION depends on the accessors: C# takes `readonly` on an accessor only where the member
/// has both (CS8658), and on the member itself otherwise -- so a getter-only property carries it on the
/// property and a get/set pair carries it on the getter, never on both (CS8661).
/// </para>
/// </summary>
internal sealed class ReadonlyEmission
{
    private readonly bool membersTakeReadonly;
    private readonly ConcreteType? owner;
    private readonly ReadonlyInference inference = new();

    private ReadonlyEmission(bool membersTakeReadonly, ConcreteType? owner)
    {
        this.membersTakeReadonly = membersTakeReadonly;
        this.owner = owner;
    }

    public static ReadonlyEmission For(MemberType type)
    {
        return new ReadonlyEmission(type is StructType && !type.IsReadonly, type as ConcreteType);
    }

    public bool OnMethod(Method method)
    {
        return membersTakeReadonly
            && !method.Modifiers.IsStatic
            && method is not Constructor
            && (owner is null || DropInsertion.FieldsOwedBy(method, owner).Count == 0)
            && inference.IsReadonly(method);
    }

    public bool OnProperty(Property property)
    {
        return membersTakeReadonly
            && !property.Modifiers.IsStatic
            && (property.Modifiers.IsReadonly
                || (property.Setter is null && Inferrable(property.Getter) && inference.IsReadonlyGetter(property)));
    }

    public bool OnGetterOf(Property property)
    {
        return membersTakeReadonly
            && !property.Modifiers.IsStatic
            && !property.Modifiers.IsReadonly
            && property.Setter is not null
            && Inferrable(property.Getter)
            && inference.IsReadonlyGetter(property);
    }

    public bool OnIndexer(Indexer indexer)
    {
        return membersTakeReadonly
            && (indexer.Modifiers.IsReadonly
                || (indexer.Setter is null && Inferrable(indexer.Getter) && inference.IsReadonlyGetter(indexer)));
    }

    public bool OnGetterOf(Indexer indexer)
    {
        return membersTakeReadonly
            && !indexer.Modifiers.IsReadonly
            && indexer.Setter is not null
            && Inferrable(indexer.Getter)
            && inference.IsReadonlyGetter(indexer);
    }

    private static bool Inferrable(PropertyAccessor? getter)
    {
        return !AccessorEmitter.IsBodiless(getter);
    }
}