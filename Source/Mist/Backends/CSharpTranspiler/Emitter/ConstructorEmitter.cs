using Mist.Backends.CSharpTranspiler.Lowering;
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

        using var constructionScope = constructor.Modifiers.IsStatic ? ConstructorWitnesses.NativeScope() : null;
        var modifiers = ResolveModifiers(constructor, declaringType);
        var parameters = ParameterEmitter.Emit(constructor.ParametersList, diagnostics);
        var concrete = declaringType as ConcreteType;
        var witnesses = constructor.Modifiers.IsStatic || concrete is null ? [] : DefaultWitnesses.Stored(concrete);
        var constructionWitnesses = constructor.Modifiers.IsStatic || concrete is null ? [] : ConstructorWitnesses.Stored(concrete);
        var parts = witnesses.Select(DefaultConstraintWitness.Parameter).Concat(ConstructorWitnesses.Parameters(constructionWitnesses)).ToList();

        if(parameters.Length > 0)
        {
            parts.Add(parameters);
        }

        if((witnesses.Count > 0 || constructionWitnesses.Count > 0) && concrete is not null)
        {
            var supplied = DefaultWitnesses.CompatibilityArguments(concrete, factory: false);
            supplied.AddRange(constructor.ParametersList.Select(parameter => ArgumentKeywords.For(parameter) + parameter.Name));
            writer.WriteLine($"{modifiers}{nameOverride ?? TypeEmitter.CleanName(declaringType)}({parameters}) : this({string.Join(", ", supplied)})");
            using(writer.Block())
            {
            }

            writer.WriteBlankLine();
        }

        // The name comes from the declaring type rather than the constructor, so it matches
        // whatever the type was emitted as -- bare, because a C# constructor carries no type
        // parameters even when its type declares them. See TypeEmitter.CleanName.
        AttributeEmitter.EmitLines(constructor.DeclaredAttributesList, writer, diagnostics);
        writer.Write($"{modifiers}{nameOverride ?? TypeEmitter.CleanName(declaringType)}({string.Join(", ", parts)})");

        var loweredToCall = declaringType is StructType && structInitNames is not null;
        var hosted = new List<StatementHost.Hosted>();

        if(!loweredToCall && constructor.ChainKind != ConstructorChainKind.None)
        {
            var target = constructor.ChainKind == ConstructorChainKind.This ? "this" : "base";
            writer.Append($" : {target}(");
            var targetType = constructor.ChainKind == ConstructorChainKind.This ? concrete : concrete?.BaseType;
            var targetDeclaration = targetType is null ? null : ProgramModel.Construction.Defaultability.DeclarationOf(targetType);
            var forwarded = targetDeclaration is null ? [] : DefaultWitnesses.ConstructionArguments(targetDeclaration, targetType!);

            if(forwarded is { Count: > 0 })
            {
                writer.Append(string.Join(", ", forwarded));

                if(constructor.ChainArgumentsList.Count > 0)
                {
                    writer.Append(", ");
                }
            }

            // Through the call path whenever nothing has to be hoisted, so a flat argument list into
            // a value pack is packed exactly as a `new` of the same constructor packs it.
            var hoists = constructor.ChainArgumentsList.Exists(argument => BodyEmitter.NeedsStatements(argument, discarded: false));

            if(!hoists)
            {
                BodyEmitter.EmitArguments(constructor.ChainArgumentsList, constructor.ChainedConstructor?.ParametersList,
                    constructor.ChainParameterTypes, writer, diagnostics, constructor.ChainedConstructor);
            }

            for(var i = 0; hoists && i < constructor.ChainArgumentsList.Count; i++)
            {
                if(i > 0)
                {
                    writer.Append(", ");
                }

                var argument = constructor.ChainArgumentsList[i];
                var parameter = constructor.ChainedConstructor is { } chained && i < chained.ParametersList.Count
                    ? chained.ParametersList[i]
                    : null;
                var closed = constructor.ChainParameterTypes is { } types && i < types.Count ? types[i] : null;

                if(!BodyEmitter.NeedsStatements(argument, discarded: false))
                {
                    if(parameter is not null)
                    {
                        writer.Append(ArgumentKeywords.For(parameter));
                    }

                    BodyEmitter.EmitArgument(argument, parameter, closed, writer, diagnostics, constructor.ChainedConstructor);
                }
                else if(StatementHost.Emit(
                    argument,
                    $"__ChainArgument{argument.Node.Span.Start}",
                    argument.Type,
                    constructor.ParametersList,
                    writer,
                    diagnostics) is { } host)
                {
                    hosted.Add(host);
                }
            }

            writer.Append(")");
        }

        if(!loweredToCall && constructor.ChainKind == ConstructorChainKind.None && !constructor.Modifiers.IsStatic
            && concrete?.BaseType is { } baseType
            && ProgramModel.Construction.Defaultability.DeclarationOf(baseType) is { } baseDeclaration
            && DefaultWitnesses.ConstructionArguments(baseDeclaration, baseType) is { Count: > 0 } baseWitnesses)
        {
            writer.Append($" : base({string.Join(", ", baseWitnesses)})");
        }

        writer.AppendLine();

        using(writer.Block())
        {
            if(concrete is not null && !constructor.Modifiers.IsStatic && constructor.ChainKind != ConstructorChainKind.This)
            {
                DefaultWitnesses.EmitAssignments(concrete, writer);
                DefaultWitnesses.EmitInitializers(concrete, writer, diagnostics);
            }

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
                BodyEmitter.EmitMethodBody(constructor, constructor.Body, writer, diagnostics, owner: declaringType);
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