using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal static class NewtypeConversions
{
    public static void Synthesize(ConcreteType clone, TypeInterner interner)
    {
        if(clone is not { CloneSource: { } root, CloneSourceType: { } sourceType }
            || sourceType is ConstructedType { Enclosing: not null }
            || ((sourceType as ConstructedType)?.Definition ?? sourceType) is not ConcreteType source
            || (!source.IsIntrinsic && !IsWithinTheEmittersReach(clone, source)))
        {
            return;
        }

        var itself = clone.TypeParametersList.Count == 0 ? clone : (IMistType)interner.GetOrCreateConstructedType(clone, clone.TypeParametersList);

        if(!IsCopyable(source))
        {
            _ = SynthesizeClonable(clone, itself, source, sourceType, interner);
            return;
        }

        if(!source.IsIntrinsic && UnheldSlot(clone, source, sourceType) is { } unheld)
        {
            if(!SynthesizeClonable(clone, itself, source, sourceType, interner))
            {
                clone.UnheldCopy = unheld;
            }

            return;
        }

        if(source.IsIntrinsic)
        {
            clone.FieldsList.Insert(0, new Field(
                "Value",
                root,
                OwnershipKind.Unrestricted,
                new MemberModifiers { Accessibility = Accessibility.Public, IsReadonly = true })
            {
                DeclaringType = clone,
            });
        }

        clone.CastOperatorsList.Add(Copy(itself, sourceType, clone, isImplicit: true));
        clone.CastOperatorsList.Add(Copy(sourceType, itself, clone, isImplicit: false));
    }

    public static void Bind(ConcreteType clone, BuilderContext context)
    {
        foreach(var conversion in clone.CastOperatorsList.Where(castOperator => castOperator.ClonesThrough is not null))
        {
            var withheld = context.Diagnostics.Withhold(
                () => BodyBinder.BindBody(conversion.Body!, new BodyScope(conversion.Scope), conversion.TargetType, context));

            conversion.BindingFailure = withheld.Occurrences.FirstOrDefault(diagnostic => diagnostic.DefaultSeverity == DiagnosticSeverity.Error);
            withheld.Discard();
        }
    }

    /// <summary>
    /// The conversions the C# backend has no spelling for yet, left unsynthesized so that a cast is refused at
    /// the Mist source and a program that never converts still compiles: a clone or source declaring a pack,
    /// whose operators and accessors would need a spelling per width, and a clone inside a generic type over
    /// a source the namespace cannot name, where its accessors have to be written.
    /// </summary>
    private static bool IsWithinTheEmittersReach(ConcreteType clone, ConcreteType source)
    {
        if(clone.TypeParametersList.Exists(parameter => parameter.IsPack) || source.TypeParametersList.Exists(parameter => parameter.IsPack))
        {
            return false;
        }

        var insideAGeneric = Enclosing(clone).Skip(1).Any(enclosing => enclosing.TypeParametersList.Count > 0);

        return !insideAGeneric
            || Enclosing(source).All(enclosing => enclosing.Modifiers.Accessibility is Accessibility.Public or Accessibility.Internal);
    }

    private static IEnumerable<MemberType> Enclosing(MemberType type)
    {
        for(MistType? enclosing = type; enclosing is not null; enclosing = enclosing.DeclaringType)
        {
            if(enclosing is MemberType member)
            {
                yield return member;
            }
        }
    }

    private static UnheldCopy? UnheldSlot(ConcreteType clone, ConcreteType source, IMistType sourceType)
    {
        var substitution = TypeSubstitution.ForReceiver(sourceType as ConstructedType);

        UnheldCopy? unheld(string member, IMistType renamed, IMistType declared)
        {
            var applied = substitution.Apply(declared, interner: null);

            return renamed.FullyQualifiedName == applied.FullyQualifiedName ? null : new UnheldCopy(member, renamed, applied);
        }

        var fields = clone.FieldsList.Where(field => !field.Modifiers.IsStatic)
            .Zip(source.FieldsList.Where(field => !field.Modifiers.IsStatic))
            .Select(slot => unheld(slot.First.Name, slot.First.Type, slot.Second.Type));

        var properties = clone.PropertiesList.Where(property => !property.Modifiers.IsStatic)
            .Zip(source.PropertiesList.Where(property => !property.Modifiers.IsStatic))
            .Select(slot => unheld(slot.First.Name, slot.First.Type, slot.Second.Type));

        var bases = clone.BaseType is { } renamedBase && source.BaseType is { } declaredBase
            ? [unheld(declaredBase.Name, renamedBase, declaredBase)]
            : Array.Empty<UnheldCopy?>();

        return fields.Concat(properties).Concat(bases).FirstOrDefault(slot => slot is not null);
    }

    public static bool IsCopyable(ConcreteType source)
    {
        return source is StructType || source.Modifiers.IsReadonly;
    }

    private static bool SynthesizeClonable(ConcreteType clone, IMistType itself, ConcreteType source, IMistType sourceType, TypeInterner interner)
    {
        if(CloneMethod(source, interner) is not { } sourceClone
            || clone.MethodsList.Find(method => ReferenceEquals(method.DeclarationNode, sourceClone.DeclarationNode)) is not { } renamedClone)
        {
            return false;
        }

        var sourceScope = sourceType is ConstructedType construction ? new ConstructionScope(source.Scope, construction) : source.Scope;

        clone.CastOperatorsList.Add(Through(renamedClone, itself, clone.Scope, sourceType, clone));
        clone.CastOperatorsList.Add(Through(sourceClone, sourceType, sourceScope, itself, clone));

        return true;
    }

    private static Method? CloneMethod(ConcreteType source, TypeInterner interner)
    {
        var itself = source.TypeParametersList.Count == 0
            ? (IMistType)source
            : interner.GetOrCreateConstructedType(source, source.TypeParametersList);

        var clonable = source.BaseAbstractTypes.Any(abstractBase => abstractBase is ConstructedType { TypeArguments: [var argument] } constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.IClonable.FullyQualifiedName
            && argument.FullyQualifiedName == itself.FullyQualifiedName);

        return clonable
            ? source.MethodsList.Find(method => method is { Name: "Clone", ParametersList.Count: 0, Body: not null, Modifiers.IsStatic: false })
            : null;
    }

    private static CastOperator Through(Method cloneMethod, IMistType result, Scope resultScope, IMistType receiver, ConcreteType clone)
    {
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true };
        var within = new ConversionScope(new ReceiverScope(resultScope, receiver));

        var conversion = new CastOperator(result, new Parameter("value", receiver), isImplicit: false, modifiers)
        {
            Body = new Block(cloneMethod.Body!.Node),
            Scope = new MethodScope(within),
            DeclaringType = clone,
            ClonesThrough = cloneMethod,
        };

        within.Conversion = conversion;

        return conversion;
    }

    private static CastOperator Copy(IMistType from, IMistType to, ConcreteType clone, bool isImplicit)
    {
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true };

        return new CastOperator(to, new Parameter("value", from), isImplicit, modifiers)
        {
            DeclaringType = clone,
            CopiesClone = true,
        };
    }
}