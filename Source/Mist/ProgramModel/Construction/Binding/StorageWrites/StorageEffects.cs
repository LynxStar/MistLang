using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramModel.Construction.Binding.StorageWrites;

/// <summary>
/// Reads one bound body for what it does to type-parameter-typed storage (unknown-type Requirement 9).
/// An assignment to a field, a backing slot or an intrinsic element writes the parameters its target's
/// type mentions. Every other reach into storage goes through a member and is an edge to that member's
/// own classification, substituted the way the site closes its declaring type; a callee with no body to
/// read -- projected, intrinsic, abstract, a function value -- writes whatever parameters its arguments
/// mention, which is the spec's conservative rule and not a judgement about the callee.
/// <para>
/// Walked with the analysis stage's <see cref="BodyWalker"/> rather than a descent of its own, although
/// construction otherwise never reaches into that stage: the table is the one enumeration of the model's
/// node kinds that a test holds complete, and a walker here that forgot a kind would silently miss a
/// write and admit a call that corrupts storage at runtime.
/// </para>
/// </summary>
internal sealed class StorageEffects(ProgramModelClass model)
{
    private readonly TypeInterner interner = model.TypeInterner;

    private Dictionary<Property, ExtensionBlock>? extensionProperties;

    public void Collect(object root, MistType? owner, StorageEffect into)
    {
        var written = new HashSet<Expression>(ReferenceEqualityComparer.Instance);
        var alsoRead = new HashSet<Expression>(ReferenceEqualityComparer.Instance);

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            switch(node)
            {
                case AssignmentExpression assignment:
                    _ = written.Add(assignment.Target);

                    if(assignment.IsCoalescing)
                    {
                        _ = alsoRead.Add(assignment.Target);
                    }

                    Write(assignment.Target, assignment.Value, into);
                    break;

                case MemberAccessExpression { Member: PropertyResult property } access when Reads(access, written, alsoRead):
                    Accessor(property.Property, StorageAccess.Read, ReceiverConstructed(access), property.BaseConstructions, [], into);
                    break;

                case PropertyReferenceExpression reference when Reads(reference, written, alsoRead):
                    Accessor(reference.Property, StorageAccess.Read, receiver: null, reference.BaseConstructions, [], into);
                    break;

                case IndexExpression { Indexer: { } indexer } index when Reads(index, written, alsoRead):
                    Member(indexer, StorageAccess.Read, index.Receiver.Type, [index.Index.Type], into);
                    break;

                case ExtensionPropertyAccessExpression extension when Reads(extension, written, alsoRead):
                    ExtensionAccessor(extension.Property, StorageAccess.Read, extension.Receiver.Type, [], into);
                    break;

                case InvocationExpression invocation:
                    Call(invocation, owner, into);
                    break;

                case NewExpression creation:
                    Construction(creation, into);
                    break;

                case BinaryExpression { Operator: { } declared } binary:
                    Operator(declared, [binary.Left.Type, binary.Right.Type], into);
                    break;

                case UnaryExpression { Operator: { } declared } unary:
                    Operator(declared, [unary.Operand.Type], into);
                    break;

                case ImplicitConversionExpression { Operator: { } declared } conversion:
                    Operator(declared, [conversion.Operand.Type, conversion.TargetType], into);
                    break;

                case CastExpression { Operator: { } declared } cast:
                    Operator(declared, [cast.Operand.Type, cast.TargetType], into);
                    break;

                case ForEachStatement loop:
                    Enumeration(loop, into);
                    break;
            }
        }
    }

    private static bool Reads(Expression access, HashSet<Expression> written, HashSet<Expression> alsoRead)
    {
        return !written.Contains(access) || alsoRead.Contains(access);
    }

    private void Write(Expression target, Expression value, StorageEffect into)
    {
        switch(target)
        {
            case FieldReferenceExpression or BackingFieldReferenceExpression or MemberAccessExpression { Member: FieldResult }:
                Direct(target.Type, into);
                break;

            case MemberAccessExpression { Member: PropertyResult property } access:
                Accessor(property.Property, StorageAccess.Write, ReceiverConstructed(access), property.BaseConstructions, [value.Type], into);
                break;

            case PropertyReferenceExpression reference:
                Accessor(reference.Property, StorageAccess.Write, receiver: null, reference.BaseConstructions, [value.Type], into);
                break;

            case IndexExpression { Indexer: { } indexer } index:
                Member(indexer, StorageAccess.Write, index.Receiver.Type, [index.Index.Type, value.Type], into);
                break;

            case IndexExpression element:
                Direct(element.Type, into);
                break;

            case ExtensionPropertyAccessExpression extension:
                ExtensionAccessor(extension.Property, StorageAccess.Write, extension.Receiver.Type, [value.Type], into);
                break;

            case SpreadExpression spread:
                Write(spread.Operand, value, into);
                break;
        }
    }

    private void Accessor(
        Property property,
        StorageAccess side,
        ConstructedType? receiver,
        IReadOnlyList<ConstructedType> path,
        IReadOnlyList<IMistType> foreignArguments,
        StorageEffect into)
    {
        if(property.DeclaringType is null)
        {
            Foreign(foreignArguments, into);
            return;
        }

        into.Edges.Add(new StorageEdge(property, side, TypeSubstitution.ForPath(receiver, path, interner)));
    }

    private void Member(Indexer indexer, StorageAccess side, IMistType receiverType, IReadOnlyList<IMistType> foreignArguments, StorageEffect into)
    {
        if(indexer.DeclaringType is not { } declaring || Ancestry.SubstitutionTo(receiverType, declaring, interner) is not { } substitution)
        {
            Foreign(foreignArguments, into);
            return;
        }

        into.Edges.Add(new StorageEdge(indexer, side, substitution));
    }

    private void ExtensionAccessor(Property property, StorageAccess side, IMistType receiverType, IReadOnlyList<IMistType> foreignArguments, StorageEffect into)
    {
        if(extensionProperties is null)
        {
            extensionProperties = new Dictionary<Property, ExtensionBlock>(ReferenceEqualityComparer.Instance);

            foreach(var declaring in model.Namespaces.Values.SelectMany(ns => ns.Extensions.Values).SelectMany(blocks => blocks))
            {
                foreach(var declared in declaring.PropertiesList)
                {
                    extensionProperties[declared] = declaring;
                }
            }
        }

        if(!extensionProperties.TryGetValue(property, out var block)
            || BlockClosing(block, receiverType) is not { } closing)
        {
            Foreign([receiverType, .. foreignArguments], into);
            return;
        }

        into.Edges.Add(new StorageEdge(property, side, closing));
    }

    private static TypeSubstitution? BlockClosing(ExtensionBlock block, IMistType receiverType)
    {
        if(block.TypeParametersList.Count == 0)
        {
            return TypeSubstitution.Empty;
        }

        return block.ResolvedReceiverType is { } declared
            && TypeArgumentInference.Unify(declared, receiverType, block.TypeParametersList) is { } arguments
                ? TypeSubstitution.ForParameters(block.TypeParametersList, arguments)
                : null;
    }

    private void Call(InvocationExpression invocation, MistType? owner, StorageEffect into)
    {
        var argumentTypes = invocation.Arguments.Select(argument => argument.Type).ToList();

        if(invocation.Method is not { } method || method.Body is null)
        {
            Foreign(argumentTypes, into);
            return;
        }

        var typeArguments = invocation.Receiver switch
        {
            MemberAccessExpression access => access.TypeArguments,
            MethodReferenceExpression reference => reference.TypeArguments,
            _ => [],
        };

        if(method.DeclaringBlock is not null || method.ClosedBlock is not null)
        {
            var canonical = method.SourceDeclaration ?? method;
            into.Edges.Add(new StorageEdge(canonical, StorageAccess.Read, method.BlockClosing.Extend(TypeSubstitution.ForMethod(canonical, typeArguments))));
        }
        else if(method.DeclaringType is { } declaring)
        {
            var receiverType = ReceiverTypeOf(invocation.Receiver) ?? owner;
            var substitution = receiverType is null ? null : Ancestry.SubstitutionTo(receiverType, declaring, interner);
            into.Edges.Add(new StorageEdge(method, StorageAccess.Read, (substitution ?? TypeSubstitution.Empty).Extend(TypeSubstitution.ForMethod(method, typeArguments))));
        }

        for(var i = 0; i < method.ParametersList.Count && i < invocation.Arguments.Count; i++)
        {
            if(method.ParametersList[i].Modifier is { IsRef: true } or { Direction: ByRefDirection.Ref or ByRefDirection.Out })
            {
                Direct(invocation.Arguments[i].Type, into);
            }
        }
    }

    private void Construction(NewExpression creation, StorageEffect into)
    {
        if(creation.Constructor is { } constructor)
        {
            if(constructor.Body is not null
                && constructor.DeclaringType is { } declaring
                && Ancestry.SubstitutionTo(creation.Type, declaring, interner) is { } substitution)
            {
                into.Edges.Add(new StorageEdge(constructor, StorageAccess.Read, substitution));
            }
            else
            {
                Foreign(creation.Arguments.Select(argument => argument.Type).ToList(), into);
            }
        }

        foreach(var entry in creation.Initializer)
        {
            switch(entry)
            {
                case { Member: FieldResult, TargetType: { } slot }:
                    Direct(slot, into);
                    break;

                case { Member: PropertyResult property }:
                    Accessor(property.Property, StorageAccess.Write, Ancestry.Unwrap(creation.Type) as ConstructedType, property.BaseConstructions, [entry.Value.Type], into);
                    break;

                case { Add: { } add }:
                    if(add.Body is not null && add.DeclaringType is { } adding && Ancestry.SubstitutionTo(creation.Type, adding, interner) is { } closing)
                    {
                        into.Edges.Add(new StorageEdge(add, StorageAccess.Read, closing));
                    }
                    else
                    {
                        Foreign([entry.Value.Type], into);
                    }

                    break;
            }
        }
    }

    private void Operator(Method declared, IReadOnlyList<IMistType> operandTypes, StorageEffect into)
    {
        if(declared.Body is null || declared.DeclaringType is not { } declaring)
        {
            Foreign(operandTypes, into);
            return;
        }

        foreach(var operand in operandTypes)
        {
            if(Ancestry.SubstitutionTo(operand, declaring, interner) is { } substitution)
            {
                into.Edges.Add(new StorageEdge(declared, StorageAccess.Read, substitution));
                return;
            }
        }

        into.Edges.Add(new StorageEdge(declared, StorageAccess.Read, TypeSubstitution.Empty));
    }

    private void Enumeration(ForEachStatement loop, StorageEffect into)
    {
        if(loop.GetEnumerator is not { Body: not null, DeclaringType: { } declaring } getEnumerator
            || Ancestry.SubstitutionTo(loop.Collection.Type, declaring, interner) is not { } substitution)
        {
            return;
        }

        into.Edges.Add(new StorageEdge(getEnumerator, StorageAccess.Read, substitution));
        var enumeratorType = substitution.Apply(getEnumerator.ReturnType, interner);

        if(loop.MoveNext is { Body: not null, DeclaringType: { } moving } moveNext
            && Ancestry.SubstitutionTo(enumeratorType, moving, interner) is { } movingSubstitution)
        {
            into.Edges.Add(new StorageEdge(moveNext, StorageAccess.Read, movingSubstitution));
        }

        if(loop.Current is { DeclaringType: { } reading } current
            && Ancestry.SubstitutionTo(enumeratorType, reading, interner) is { } readingSubstitution)
        {
            into.Edges.Add(new StorageEdge(current, StorageAccess.Read, readingSubstitution));
        }
    }

    private IMistType? ReceiverTypeOf(Expression callee)
    {
        if(callee is not MemberAccessExpression access)
        {
            return null;
        }

        var type = access.Receiver is TypeReferenceExpression reference ? reference.ReferencedType : access.Receiver.Type;

        return access.IsConditional ? OptionalLifting.NonVoidPortion(type, interner) : type;
    }

    private ConstructedType? ReceiverConstructed(MemberAccessExpression access)
    {
        return ReceiverTypeOf(access) is { } type ? Ancestry.Unwrap(type) as ConstructedType : null;
    }

    private static void Direct(IMistType type, StorageEffect into)
    {
        TypeParameterOccurrences.Collect(type, into.Direct);
    }

    private static void Foreign(IReadOnlyList<IMistType> argumentTypes, StorageEffect into)
    {
        foreach(var type in argumentTypes)
        {
            Direct(type, into);
        }
    }
}