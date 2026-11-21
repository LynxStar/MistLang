using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed partial class ScopeResolver
{
    internal static int GetTypeArgumentArity(TypeArgumentListNode? typeArgList)
    {
        if(typeArgList is null)
        {
            return 0;
        }

        return 1 + typeArgList.TypeArguments.Length;
    }

    internal static string BuildArityName(string baseName, int arity)
    {
        var commas = new string(',', arity - 1);
        return $"{baseName}<{commas}>";
    }

    public IReadOnlyList<IMistType> ResolveTypeArguments(TypeArgumentListNode typeArgList, Scope scope)
    {
        var args = new List<IMistType>();

        ResolveTypeArgument(typeArgList.TypeArgument, scope, args);

        foreach(var arg in typeArgList.TypeArguments)
        {
            ResolveTypeArgument(arg, scope, args);
        }

        return args;
    }

    private void ResolveTypeArgument(TypeArgumentNode arg, Scope scope, List<IMistType> args)
    {
        switch(arg)
        {
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
                args.Add(Resolve(typeRefArg.Type, scope));
                break;

            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
                args.Add(LiftIntegerLiteral(intLit, intLit.Literal.Lexeme, negated: false));
                break;

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                args.Add(LiftIntegerLiteral(negLit, negLit.Literal.Lexeme, negated: true));
                break;

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                args.Add(LiftIntegerLiteral(hexLit, hexLit.Literal.Lexeme, negated: false));
                break;

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                args.Add(LiftIntegerLiteral(binLit, binLit.Literal.Lexeme, negated: false));
                break;

            case TrueLiteralTypeArgumentTypeArgumentNode:
                args.Add(LiftBool(true));
                break;

            case FalseLiteralTypeArgumentTypeArgumentNode:
                args.Add(LiftBool(false));
                break;

            case CharLiteralTypeArgumentTypeArgumentNode charLit:
                args.Add(LiftCharLiteral(charLit.Literal.Lexeme));
                break;

            // Brackets supply a pack DIRECTLY, which is what lets a list carry more than one: the
            // argument count then matches the slot count, so nothing has to work out where one pack
            // ends and the next begins.
            case BracketedPackTypeArgumentTypeArgumentNode bracketed:
                args.Add(ResolveBracketedPack(bracketed, scope));
                break;

            default:
                ReportResolutionFailure(ProgramModelDiagnostics.UnknownTypeRefNode, arg, null, new NameValue(arg.GetType().Name));
                args.Add(ErrorType.Instance);
                break;
        }
    }

    /// <summary>
    /// A bracketed pack argument, lifted to the same comptime <c>Type[]</c> value the binding rule
    /// produces from a flat list -- so the bracketed and flat spellings are one type rather than two
    /// that happen to agree.
    /// </summary>
    private IMistType ResolveBracketedPack(
        BracketedPackTypeArgumentTypeArgumentNode bracketed, Scope scope)
    {
        var elements = new List<IMistType>();

        if(bracketed.PackElements is { } list)
        {
            elements.Add(Resolve(list.TypeReference, scope));

            foreach(var element in list.TypeReferences)
            {
                elements.Add(Resolve(element, scope));
            }
        }

        return context.ProgramModel.TypeInterner.LiftPack(
            ConstructArray(LookupWellKnown(WellKnownTypes.Type), length: null),
            new TypePackValue(elements));
    }

    /// <summary>
    /// Lifts an integral type argument as an <c>int</c>. Every integral value the
    /// TypeArgument grammar admits sits in the int domain — dependent parameters are
    /// constrained by refinements over int, such as <c>ZeroOrPositiveInt</c> — so the
    /// lifted type does not vary with the literal's spelling.
    /// </summary>
    public IMistType LiftInt(long value)
    {
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Int32), value);
    }

    private IMistType LiftBool(bool value)
    {
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Boolean), value);
    }

    private IMistType LiftIntegerLiteral(TypeArgumentNode node, string lexeme, bool negated)
    {
        var magnitude = IntegralConstant.Of(IntegerLiterals.Read(lexeme)?.Value);
        var value = negated ? -magnitude : magnitude;

        if(value is { } signed && signed >= long.MinValue && signed <= long.MaxValue)
        {
            return LiftInt((long)signed);
        }

        if(value is { } unsigned && unsigned > long.MaxValue && unsigned <= ulong.MaxValue)
        {
            return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Int32), (ulong)unsigned);
        }

        var widest = negated ? WellKnownTypes.Int64 : WellKnownTypes.UInt64;
        ReportResolutionFailure(
            ProgramModelDiagnostics.NumericLiteralOutOfRange, node, null, new TextValue(negated ? $"-{lexeme}" : lexeme), new TypeValue(widest.FullyQualifiedName, widest.Keyword ?? widest.Name));
        return ErrorType.Instance;
    }

    private IMistType LiftCharLiteral(string lexeme)
    {
        var charValue = CharacterLiterals.Decode(lexeme);
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Char), charValue);
    }
}