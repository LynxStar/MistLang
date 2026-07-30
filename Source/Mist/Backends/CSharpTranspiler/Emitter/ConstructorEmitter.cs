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
    public static void Emit(Constructor constructor, MemberType declaringType, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = ResolveModifiers(constructor, declaringType);
        var parameters = ParameterEmitter.Emit(constructor.ParametersList);

        // The name comes from the declaring type rather than the constructor, so it matches
        // whatever the type was emitted as.
        writer.Write($"{modifiers}{declaringType.Name}({parameters})");

        if(constructor.ChainKind != ConstructorChainKind.None)
        {
            var target = constructor.ChainKind == ConstructorChainKind.This ? "this" : "base";
            writer.Append($" : {target}(");
            EmitList.Separated(
                constructor.ChainArgumentsList, ", ", writer, argument => BodyEmitter.EmitExpression(argument, writer, diagnostics));
            writer.Append(")");
        }

        writer.AppendLine();

        using(writer.Block())
        {
            if(constructor.Body is not null)
            {
                BodyEmitter.EmitBlock(constructor.Body, writer, diagnostics);
            }
        }
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

        
        return MemberModifierEmitter.Emit(new MemberModifiers { Accessibility = accessibility });
    }
}