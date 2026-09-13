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
        if(clone is not { CloneSource: { } source, CloneSourceType: { } sourceType }
            || (clone.TypeParametersList.Count > 0 && !source.IsIntrinsic)
            || sourceType is ConstructedType { Enclosing: not null })
        {
            return;
        }

        if(!IsCopyable(source))
        {
            SynthesizeClonable(clone, source, sourceType, interner);
            return;
        }

        if(source.IsIntrinsic)
        {
            clone.FieldsList.Insert(0, new Field(
                "Value",
                source,
                OwnershipKind.Unrestricted,
                new MemberModifiers { Accessibility = Accessibility.Public, IsReadonly = true })
            {
                DeclaringType = clone,
            });
        }

        var itself = clone.TypeParametersList.Count == 0 ? clone : (IMistType)interner.GetOrCreateConstructedType(clone, clone.TypeParametersList);

        clone.CastOperatorsList.Add(Copy(itself, sourceType, clone, isImplicit: true));
        clone.CastOperatorsList.Add(Copy(sourceType, itself, clone, isImplicit: false));
    }

    public static void Bind(ConcreteType clone, BuilderContext context)
    {
        foreach(var conversion in clone.CastOperatorsList.Where(castOperator => castOperator.ClonesThrough is not null))
        {
            var withheld = context.Diagnostics.Withhold(
                () => BodyBinder.BindBody(conversion.Body!, new BodyScope(conversion.Scope), conversion.TargetType, context));

            conversion.BindingFailure = withheld.Find(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }

    public static bool IsCopyable(ConcreteType source)
    {
        return source is StructType || source.Modifiers.IsReadonly;
    }

    private static void SynthesizeClonable(ConcreteType clone, ConcreteType source, IMistType sourceType, TypeInterner interner)
    {
        if(CloneMethod(source, interner) is not { } sourceClone
            || clone.MethodsList.Find(method => ReferenceEquals(method.DeclarationNode, sourceClone.DeclarationNode)) is not { } renamedClone)
        {
            return;
        }

        var sourceScope = sourceType is ConstructedType construction ? new ConstructionScope(source.Scope, construction) : source.Scope;

        clone.CastOperatorsList.Add(Through(renamedClone, clone, clone.Scope, sourceType, clone));
        clone.CastOperatorsList.Add(Through(sourceClone, sourceType, sourceScope, clone, clone));
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

        return new CastOperator(result, new Parameter("value", receiver), isImplicit: false, modifiers)
        {
            Body = new Block(cloneMethod.Body!.Node),
            Scope = new MethodScope(new ReceiverScope(resultScope, receiver)),
            DeclaringType = clone,
            ClonesThrough = cloneMethod,
        };
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