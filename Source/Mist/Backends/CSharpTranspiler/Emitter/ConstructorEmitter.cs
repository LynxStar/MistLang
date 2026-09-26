using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a constructor declaration: modifiers, name, parameters, chain, and body.
/// </summary>
internal static class ConstructorEmitter
{
    /// <param name="structInitNames">
    /// The per-ancestor initializer names when the declaring type is a struct, which turns a chain
    /// clause C# cannot express into a call at the top of the body. Both chain kinds are lowered,
    /// not just <c>base</c>, so a struct constructor's shape does not depend on which it used.
    /// Null for a class, which keeps its real chain clause.
    /// </param>
    /// <param name="nameOverride">
    /// The emitted constructor name when the constructor is written into a type other than the
    /// one that declared it -- a newtype duplicating its source's members emits them under its
    /// own name. Null emits under the declaring type's name.
    /// </param>
    public static void Emit(
        Constructor constructor,
        MemberType declaringType,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<StructType, string>? structInitNames = null,
        string? nameOverride = null)
    {
        if(IntrinsicCloneEmitter.Forwards(constructor.Modifiers, declaringType))
        {
            IntrinsicCloneEmitter.EmitConstructor(constructor, writer, diagnostics);
            return;
        }

        var modifiers = ResolveModifiers(constructor, declaringType);
        var parameters = ParameterEmitter.Emit(constructor.ParametersList, diagnostics);

        // The name comes from the declaring type rather than the constructor, so it matches
        // whatever the type was emitted as -- bare, because a C# constructor carries no type
        // parameters even when its type declares them. See TypeEmitter.CleanName.
        AttributeEmitter.EmitLines(constructor.DeclaredAttributesList, writer, diagnostics);
        writer.Write($"{modifiers}{nameOverride ?? TypeEmitter.CleanName(declaringType)}({parameters})");

        var loweredToCall = declaringType is StructType && structInitNames is not null;
        var hosted = new List<StatementHost.Hosted>();

        if(!loweredToCall && constructor.ChainKind != ConstructorChainKind.None)
        {
            var target = constructor.ChainKind == ConstructorChainKind.This ? "this" : "base";
            writer.Append($" : {target}(");
            EmitList.Separated(constructor.ChainArgumentsList, ", ", writer, argument =>
            {
                if(StatementHost.Emit(
                    argument,
                    $"__ChainArgument{argument.Node.Span.Start}",
                    argument.Type,
                    constructor.ParametersList,
                    writer,
                    diagnostics) is { } host)
                {
                    hosted.Add(host);
                }
            });
            writer.Append(")");
        }

        writer.AppendLine();

        using(writer.Block())
        {
            if(loweredToCall)
            {
                StructInitEmitter.EmitChainCall(
                    constructor, (StructType)declaringType, structInitNames!, writer, diagnostics);
            }

            if(constructor.Body is not null)
            {
                // Through the same entry a method's body takes: a constructor carries `with` clauses
                // and owned parameters like any other callable, and emitting its block directly is
                // what left both silently undone.
                BodyEmitter.EmitMethodBody(constructor, constructor.Body, writer, diagnostics);
            }
        }

        StatementHost.EmitMethods(hosted, writer, diagnostics);
    }

    private static string ResolveModifiers(Constructor constructor, MemberType declaringType)
    {

        if(constructor.Modifiers.IsStatic)
        {
            return "static ";
        }


        var accessibility = declaringType is StructType && constructor.ParametersList.Count == 0
            ? Accessibility.Public
            : constructor.Modifiers.Accessibility
            ;


        return MemberModifierEmitter.Emit(
            new MemberModifiers { Accessibility = accessibility }, emitReadonly: false);
    }
}