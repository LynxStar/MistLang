using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal static class NewtypeConversions
{
    public static void Synthesize(ConcreteType clone)
    {
        if(clone.CloneSource is not { } source)
        {
            return;
        }

        if(!IsCopyable(source))
        {
            SynthesizeClonable(clone, source);
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

        clone.CastOperatorsList.Add(Copy(clone, source, clone, isImplicit: true));
        clone.CastOperatorsList.Add(Copy(source, clone, clone, isImplicit: false));
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

    private static void SynthesizeClonable(ConcreteType clone, ConcreteType source)
    {
        if(CloneMethod(source) is not { } sourceClone
            || clone.MethodsList.Find(method => ReferenceEquals(method.DeclarationNode, sourceClone.DeclarationNode)) is not { } renamedClone)
        {
            return;
        }

        clone.CastOperatorsList.Add(Through(renamedClone, source, clone));
        clone.CastOperatorsList.Add(Through(sourceClone, clone, clone));
    }

    private static Method? CloneMethod(ConcreteType source)
    {
        var clonable = source.BaseAbstractTypes.Any(abstractBase => abstractBase is ConstructedType { TypeArguments: [var argument] } constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.IClonable.FullyQualifiedName
            && ConversionRelation.IsIdentity(argument, source));

        return clonable
            ? source.MethodsList.Find(method => method is { Name: "Clone", ParametersList.Count: 0, Body: not null, Modifiers.IsStatic: false })
            : null;
    }

    private static CastOperator Through(Method cloneMethod, ConcreteType receiver, ConcreteType clone)
    {
        var result = (ConcreteType)cloneMethod.DeclaringType!;
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true };

        return new CastOperator(result, new Parameter("value", receiver), isImplicit: false, modifiers)
        {
            Body = new Block(cloneMethod.Body!.Node),
            Scope = new MethodScope(new ReceiverScope(result.Scope, receiver)),
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