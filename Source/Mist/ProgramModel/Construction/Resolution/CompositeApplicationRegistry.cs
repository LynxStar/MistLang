using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Every union, tuple and array type the source wrote, with the types it was written from. An alias
/// resolves its right-hand side once and every use of the alias reaches the result, so a rule that
/// judges what a composite may hold judges each written one here, once, where it was written. A generic
/// alias closed at a use is recorded there too, but only with what its parameters were closed by -- a
/// type argument it hands on included -- since the rest was recorded where the alias was written.
/// </summary>
internal sealed class CompositeApplicationRegistry
{
    private readonly List<CompositeApplication> applications = [];

    public IReadOnlyList<CompositeApplication> Applications => applications;

    public void Record(CompositeKind kind, IReadOnlyList<IMistType> parts, SyntaxNode node, Scope? boundIn = null)
    {
        applications.Add(new CompositeApplication(kind, parts, node, boundIn));
    }

    public void RecordClosing(
        IMistType open, IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> arguments, SyntaxNode node, Scope? boundIn)
    {
        switch(open)
        {
            case UnionType union:
                RecordClosed(CompositeKind.Union, union.Constituents, parameters, arguments, node, boundIn);
                Recurse(union.Constituents, parameters, arguments, node, boundIn);
                break;

            case NamedTupleType named:
                RecordClosing(named.Underlying, parameters, arguments, node, boundIn);
                break;

            case TupleType tuple:
                RecordClosed(CompositeKind.Tuple, tuple.ElementTypes, parameters, arguments, node, boundIn);
                Recurse(tuple.ElementTypes, parameters, arguments, node, boundIn);
                break;

            case ConstructedType { TypeArguments.Count: > 0 } constructed:
                var array = constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
                RecordClosed(
                    array ? CompositeKind.Array : CompositeKind.TypeArgument, array ? [constructed.TypeArguments[0]] : constructed.TypeArguments,
                    parameters, arguments, node, boundIn);
                Recurse(constructed.TypeArguments, parameters, arguments, node, boundIn);
                break;

            case FunctionType function:
                Recurse([.. function.ParameterTypes, function.ReturnType], parameters, arguments, node, boundIn);
                break;
        }
    }

    private void Recurse(
        IReadOnlyList<IMistType> parts, IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> arguments, SyntaxNode node, Scope? boundIn)
    {
        foreach(var part in parts)
        {
            RecordClosing(part, parameters, arguments, node, boundIn);
        }
    }

    private void RecordClosed(
        CompositeKind kind, IReadOnlyList<IMistType> parts, IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> arguments,
        SyntaxNode node, Scope? boundIn)
    {
        var closed = parts
            .Select(part => part is TypeParameter parameter && Ordinal(parameters, parameter) is var index and >= 0 && index < arguments.Count ? arguments[index] : null)
            .OfType<IMistType>()
            .ToList();

        if(closed.Count > 0)
        {
            Record(kind, closed, node, boundIn);
        }
    }

    private static int Ordinal(IReadOnlyList<TypeParameter> parameters, TypeParameter parameter)
    {
        for(var i = 0; i < parameters.Count; i++)
        {
            if(ReferenceEquals(parameters[i], parameter))
            {
                return i;
            }
        }

        return -1;
    }
}

internal enum CompositeKind
{
    Union,
    Tuple,
    Array,
    TypeArgument,
}

internal sealed record CompositeApplication(CompositeKind Kind, IReadOnlyList<IMistType> Parts, SyntaxNode Node, Scope? BoundIn = null);