using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits an ancestor struct's constructor as a private initializer method on the struct that
/// absorbed it, so a derived constructor can run base initialization by calling it. A C# struct has
/// no base to chain to, and this is what stands in for the chain.
/// <para>
/// The initializer takes the ancestor constructor's OWN parameters, which is what lets its body and
/// its chain arguments emit verbatim: every name they use is already in scope. Being a method also
/// means an argument is evaluated once because C# argument passing says so, and that a
/// <c>return</c> in the base body ends base initialization rather than the derived constructor that
/// called it.
/// </para>
/// </summary>
internal static class StructInitEmitter
{
    /// <summary>
    /// The initializer name per ancestor, chosen once so every overload of one ancestor's
    /// constructors shares it and a call can name it without knowing which overload it will bind to.
    /// <para>
    /// A legal C# identifier, so unlike <c>.ctor</c> or <c>&lt;X&gt;k__BackingField</c> it cannot be
    /// made collision-proof by construction -- the CLR and the C# compiler both reserve names using
    /// characters no source identifier may contain, which emitting C# source rules out. Hence the
    /// check against names the struct already uses.
    /// </para>
    /// </summary>
    public static Dictionary<StructType, string> BuildNames(FlattenedStruct flattened)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach(var method in flattened.Methods)
        {
            _ = taken.Add(method.Name);
        }

        foreach(var field in flattened.Fields)
        {
            _ = taken.Add(field.Name);
        }

        foreach(var property in flattened.Properties)
        {
            _ = taken.Add(property.Name);
        }

        var names = new Dictionary<StructType, string>();
        foreach(var ancestor in flattened.Ancestors)
        {
            var baseName = $"__{TypeEmitter.CleanName(ancestor.Definition)}_ctor";
            var name = baseName;
            var suffix = 2;

            while(taken.Contains(name))
            {
                name = $"{baseName}{suffix}";
                suffix++;
            }

            _ = taken.Add(name);
            names[ancestor.Definition] = name;
        }

        return names;
    }

    /// <summary>
    /// Emits one initializer per constructor of <paramref name="ancestor"/>. All of them, rather
    /// than only the overload a particular chain targets: as C# overloads they let Roslyn resolve
    /// the call, so nothing here has to do overload resolution the model does not carry.
    /// </summary>
    public static void EmitFor(
        AbsorbedAncestor ancestor,
        IReadOnlyDictionary<StructType, string> names,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool anyEmitted)
    {
        using var inherited = TypeEmitter.Substituting(ancestor.Path);

        foreach(var constructor in ancestor.Definition.ConstructorsList)
        {
            // A static constructor initializes the type rather than an instance, so nothing chains
            // to it; a bodiless one has no initialization to run.
            if(constructor.Modifiers.IsStatic || constructor.Body is null)
            {
                continue;
            }

            TypeEmitter.SeparateMember(writer, ref anyEmitted);

            var parameters = ParameterEmitter.Emit(constructor.ParametersList, diagnostics);
            writer.WriteLine($"private void {names[ancestor.Definition]}({parameters})");

            using(writer.Block())
            {
                EmitChainCall(constructor, ancestor.Definition, names, writer, diagnostics);
                BodyEmitter.EmitBlock(constructor.Body, constructor.ParametersList.Select(parameter => parameter.Name), writer, diagnostics);
            }
        }
    }

    /// <summary>
    /// Emits the call standing in for <paramref name="constructor"/>'s own chain, if it has one.
    /// The arguments use the selected initializer's parameter types to preserve callable overloads.
    /// </summary>
    public static void EmitChainCall(
        Constructor constructor,
        StructType declaringStruct,
        IReadOnlyDictionary<StructType, string> names,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var implicitConstructor = constructor is { ChainKind: ConstructorChainKind.None, Modifiers.IsStatic: false }
            ? ImplicitChains.Of(declaringStruct) as Constructor
            : null;

        var target = constructor.ChainKind switch
        {
            // Delegating within one struct lands on that struct's own initializer: the same name,
            // resolved to a different overload by the argument list.
            ConstructorChainKind.This => declaringStruct,
            ConstructorChainKind.Base => UnwrapBaseStruct(declaringStruct.BaseType),
            ConstructorChainKind.None when implicitConstructor?.Body is not null => UnwrapBaseStruct(declaringStruct.BaseType),
            _ => null,
        };

        if(target is null)
        {
            return;
        }

        if(!names.TryGetValue(target, out var name))
        {
            if(constructor.ChainKind != ConstructorChainKind.This)
            {
                return;
            }

            name = $"this = new {TypeNameEmitter.Emit(declaringStruct)}";
        }

        if(implicitConstructor?.ParametersList is [{ IsFlattened: true } parameter])
        {
            var substitution = TypeSubstitution.ForReceiver(declaringStruct.BaseType as ConstructedType);
            var parameterType = substitution.Apply(parameter.Type, interner: null);
            writer.WriteLine($"{name}({parameter.Name}: new {TypeNameEmitter.Emit(parameterType)} {{ }});");
            return;
        }

        using(BodyEmitter.EmitHoisted(
            UnitHoisting.Plan(constructor.ChainArgumentsList, constructor.ChainedConstructor?.ParametersList), writer, diagnostics))
        {
            writer.Write($"{name}(");
            BodyEmitter.EmitArguments(constructor.ChainArgumentsList, constructor.ChainedConstructor?.ParametersList,
                constructor.ChainParameterTypes, writer, diagnostics, constructor.ChainedConstructor);
            writer.Append(");");
            writer.AppendLine();
        }
    }

    private static StructType? UnwrapBaseStruct(MistType? baseType)
    {
        return baseType switch
        {
            StructType direct => direct,
            ConstructedType { Definition: StructType definition } => definition,
            _ => null,
        };
    }
}