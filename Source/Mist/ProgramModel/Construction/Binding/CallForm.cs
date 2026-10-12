using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// How a call's arguments meet one candidate's substituted parameters where more than one way is
/// possible. A homogeneous <c>params</c> collection takes them in its NORMAL form, one argument that
/// already is the collection, or EXPANDED, each argument an element; and native defaults fill trailing
/// parameters the call leaves out.
/// <para>
/// <see cref="Prefer"/> orders two candidates whose parameter types the arguments cannot separate by
/// the rules C# applies to the emitted call, so the overload bound is the one that runs: the normal
/// form over the expanded one, then fewer collected arguments, then the better collection
/// (<see cref="ParamsCollection.Compare"/>), then supplying every parameter over leaving one to its
/// default. Negative means the left candidate wins.
/// </para>
/// </summary>
internal readonly record struct CallForm(bool Expanded, int Collected, int Omitted, IMistType? Collection)
{
    public static CallForm Of(IReadOnlyList<Parameter> parameters, IReadOnlyList<Expression> arguments)
    {
        var omitted = ValuePack.OmittedDefaults(parameters, arguments.Count);

        if(ValuePack.FlattenedSlot(parameters) is not { } flattened || ValuePack.ElementOf(parameters[flattened]) is null)
        {
            return new CallForm(Expanded: false, Collected: 0, omitted, Collection: null);
        }

        var collection = parameters[flattened].Type;
        var expanded = arguments.Count != parameters.Count || IsElement(arguments[flattened], collection);

        return new CallForm(expanded, Math.Max(0, arguments.Count - (parameters.Count - 1)), omitted, collection);
    }

    public static int Prefer(CallForm left, CallForm right)
    {
        if(left.Expanded != right.Expanded)
        {
            return left.Expanded ? 1 : -1;
        }

        if(left.Expanded)
        {
            if(left.Collected != right.Collected)
            {
                return left.Collected < right.Collected ? -1 : 1;
            }

            if(ParamsCollection.Compare(left.Collection!, right.Collection!) is var collection and not 0)
            {
                return collection;
            }
        }

        var leftSuppliesAll = left.Omitted == 0;
        var rightSuppliesAll = right.Omitted == 0;

        return leftSuppliesAll == rightSuppliesAll ? 0 : leftSuppliesAll ? -1 : 1;
    }

    private static bool IsElement(Expression argument, IMistType collection)
    {
        return argument switch
        {
            UnboundLambdaExpression => true,
            MethodGroupExpression or UnboundContextualExpression => false,
            _ => !ConversionRelation.IsImplicit(argument.Type, collection) && !ConstantConversion.Fits(argument, collection),
        };
    }
}