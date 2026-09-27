using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class ReadonlyAnalyzer
{
    private sealed record Frame(
        object? Member, Origins Origins, MemberModifiers? Modifiers, bool IsReadonly, bool Privileged, bool BackingFieldExempt, bool InReadonlyBlock)
    {
        public static Frame For(SourceRoot root)
        {
            var origins = new Origins(root.Root, root.Member, ScopeKind.Readonly);
            var modifiers = ModifiersOf(root);

            return root.Member switch
            {
                Constructor constructor => new Frame(constructor, origins, modifiers, JudgedHere(modifiers), true, false, false),
                Method method => new Frame(method, origins, modifiers, JudgedHere(modifiers), false, false, false),
                Property property => new Frame(
                    property, origins, modifiers, JudgedHere(modifiers), false, IsSetterBody(property.Setter, root.Root), false),
                Indexer indexer => new Frame(indexer, origins, modifiers, JudgedHere(modifiers), false, false, false),
                _ => new Frame(root.Member, origins, modifiers, false, false, false, false),
            };
        }

        private static MemberModifiers? ModifiersOf(SourceRoot root)
        {
            return root.Member switch
            {
                Method method => method.Modifiers,
                Property property => ReadonlyInference.Declared(PurityInference.AccessorOf(property.Getter, property.Setter, root.Root), property.Modifiers),
                Indexer indexer => ReadonlyInference.Declared(PurityInference.AccessorOf(indexer.Getter, indexer.Setter, root.Root), indexer.Modifiers),
                _ => null,
            };
        }

        private static bool JudgedHere(MemberModifiers? modifiers)
        {
            return modifiers is { IsReadonly: true, IsPure: false, IsLocal: false };
        }

        public bool PurityRefusesWrite(bool own)
        {
            return Modifiers switch
            {
                { IsPure: true } => !own,
                { IsLocal: true, IsReadonly: true } => true,
                _ => false,
            };
        }

        public Frame InsideReadonlyBlock()
        {
            var judgedByPurity = Modifiers is { IsPure: true } or { IsLocal: true, IsReadonly: true };

            return this with { IsReadonly = IsReadonly || !judgedByPurity, Privileged = false, InReadonlyBlock = true };
        }

        public bool SuspendedPrivilege(Field field)
        {
            return Member is Constructor { DeclaringType: { } built } && ReferenceEquals(field.DeclaringType, built);
        }

        public bool Owns(Expression target)
        {
            return ReadonlyInference.HolderOf(Origins, target) is Origin.Own;
        }

        public bool CallsLocalOnOwn(InvocationExpression invocation, Method callee, PurityInference inference)
        {
            return ThisTiers.ReceiverOf(invocation) is { } receiver && LocalOn(receiver, callee, inference);
        }

        public bool LocalOn(Expression receiver, Method callee, PurityInference inference)
        {
            return !callee.Modifiers.IsStatic && Origins.Of(receiver) is Origin.Own && inference.Of(callee).Local;
        }

        public bool LocalOnOwn(PropertyAccessor? getter, MemberModifiers owner, Expression at, PurityInference inference)
        {
            return getter is not null
                && !owner.IsStatic
                && Owns(at)
                && inference.Of(getter).Local;
        }

        public bool Constructs(Field field, Expression target)
        {
            if(Member is not Constructor { DeclaringType: { } built } constructor || !ReferenceEquals(field.DeclaringType, built))
            {
                return false;
            }

            if(field.Modifiers.IsStatic)
            {
                return constructor.Modifiers.IsStatic;
            }

            return !constructor.Modifiers.IsStatic && target is FieldReferenceExpression or MemberAccessExpression { Receiver: ThisExpression };
        }

        public bool MaySet(Property property, Expression target, PurityInference inference)
        {
            return Privileged
                && Member is Constructor { DeclaringType: { } built }
                && ReferenceEquals(property.DeclaringType, built)
                && target is PropertyReferenceExpression or MemberAccessExpression { Receiver: ThisExpression }
                && (property.Modifiers.IsReadonly || inference.IsReadonlyGetter(property));
        }

        public MistType? ScopeType()
        {
            return Member switch
            {
                Method method => method.DeclaringType,
                Property property => property.DeclaringType,
                Indexer indexer => indexer.DeclaringType,
                _ => null,
            };
        }

        public string MemberName()
        {
            return Member switch
            {
                Method method => method.Name,
                Property property => property.Name,
                Indexer => "this[]",
                _ => "?",
            };
        }

        public string Describe()
        {
            return Member switch
            {
                Constructor => "constructor",
                Method method => $"method '{method.Name}'",
                Property property => $"property '{property.Name}'",
                Indexer => "indexer",
                _ => "context",
            };
        }

        private static bool IsSetterBody(PropertyAccessor? setter, object root)
        {
            return setter is not null
                && (ReferenceEquals(setter.Body, root) || ReferenceEquals(setter.ExpressionBody, root));
        }
    }
}