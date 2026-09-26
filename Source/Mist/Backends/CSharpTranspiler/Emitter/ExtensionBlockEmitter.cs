using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits one extension block as the C# static class that carries its members.
/// <para>
/// Separate from <see cref="TypeEmitter"/> because a block is not a type: it declares none, is
/// registered by receiver rather than by name, and its members become STATIC methods on a class the
/// user never wrote. Sharing the type path would mean teaching every arm of it about a declaration
/// that has no accessibility, no base, no nested types and no conformances.
/// </para>
/// </summary>
internal static class ExtensionBlockEmitter
{
    public static void Emit(
        ExtensionBlock block, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        if(block.ResolvedReceiverType is not { } receiver)
        {
            return;
        }

        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;

        // NON-GENERIC, always. C# requires an extension method's containing class to be non-generic,
        // so a block's own type parameters cannot live here; they are hoisted onto each member below.
        writer.WriteLine($"public static class {block.CarrierName}");
        using(writer.Block())
        {
            var first = true;

            foreach(var method in block.MethodsList)
            {
                EmitMember(method, block, receiver, writer, diagnostics, ref first);
            }

            foreach(var property in block.PropertiesList)
            {
                EmitProperty(property, block, receiver, writer, diagnostics, ref first);
            }
        }
    }

    private static void EmitMember(
        Method method,
        ExtensionBlock block,
        IMistType receiver,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool first)
    {
        Separate(writer, ref first);

        var returnType = OptionalUnion.IsVoid(method.ReturnType)
            ? "void"
            : TypeNameEmitter.Emit(method.ReturnType);

        var typeParameters = CarrierTypeParameters(method, block);
        var parameters = DefaultConstraintWitness.Parameters(method.TypeParametersList);
        parameters.Add(ParameterEmitter.Emit(method.ParametersList, diagnostics));

        writer.WriteLine(
            $"public static {returnType} {method.Name}{TypeParameters(typeParameters)}"
            + $"({Parameters(parameters, method.Modifiers.IsStatic, block, receiver)})");
        ConstraintEmitter.Emit(typeParameters, writer);

        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitBlock(method.Body, method.ParametersList.Select(parameter => parameter.Name), writer, diagnostics);
            }
        }
    }

    /// <summary>
    /// A property becomes a <c>get_</c> method, because C# has no extension property. The call site
    /// has to be reclassified from an access to an invocation to match, which is the lookup half of
    /// this layer rather than the emission half.
    /// </summary>
    private static void EmitProperty(
        Property property,
        ExtensionBlock block,
        IMistType receiver,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        ref bool first)
    {
        if(property.Getter is not { } getter)
        {
            return;
        }

        Separate(writer, ref first);

        writer.WriteLine(
            $"public static {TypeNameEmitter.Emit(property.Type)} get_{property.Name}"
            + $"{TypeParameters(CarrierTypeParameters(method: null, block))}"
            + $"({Parameters([], property.Modifiers.IsStatic, block, receiver)})");

        using(writer.Block())
        {
            if(getter.Body is { } body)
            {
                BodyEmitter.EmitBlock(body, writer, diagnostics);
            }
            else if(getter.ExpressionBody is { } expression)
            {
                BodyEmitter.EmitSequenced(expression, "return ", writer, diagnostics);
            }
        }
    }

    /// <summary>
    /// The receiver, <c>this</c>-annotated, ahead of everything the member declared -- except on a
    /// static member, which is namespaced under the type rather than called on an instance and so
    /// takes no receiver at all.
    /// </summary>
    private static string Parameters(
        IReadOnlyList<string> declared,
        bool isStatic,
        ExtensionBlock block,
        IMistType receiver)
    {
        var parts = declared.Where(part => part.Length > 0).ToList();

        if(!isStatic)
        {
            parts.Insert(0, $"this {TypeNameEmitter.Emit(receiver)} {block.ReceiverParameterName}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The block's type parameters and the member's own, in that order. They cannot go on the class,
    /// so every member restates them; a member declared inside a generic block is generic in both.
    /// </summary>
    private static List<TypeParameter> CarrierTypeParameters(Method? method, ExtensionBlock block)
    {
        return [.. block.TypeParametersList, .. method?.TypeParametersList ?? []];
    }

    private static string TypeParameters(IReadOnlyList<TypeParameter> parameters)
    {
        return parameters.Count == 0 ? "" : $"<{string.Join(", ", parameters.Select(DependentCarrierNames.Parameter))}>";
    }

    /// <summary>
    /// What a call writes after the member's name: C# takes all of a carrier's type arguments or none,
    /// so a call that writes the method's own writes the block's ahead of them, in the order
    /// <see cref="CarrierTypeParameters"/> declares. Only a block declared in Mist, whose carrier this
    /// emitter wrote; a projected block keeps the order of the CLR method it came from.
    /// </summary>
    internal static (IReadOnlyList<IMistType> Arguments, IReadOnlyList<TypeParameter>? Parameters) CallTypeArguments(
        Method? method, IReadOnlyList<IMistType> written)
    {
        if(written.Count == 0 || method is not { ClosedBlock: { DeclarationNode: not null } block })
        {
            return (written, method?.TypeParametersList);
        }

        var arguments = block.TypeParametersList.ConvertAll(parameter => method.BlockClosing.Apply(parameter, interner: null));

        return ([.. arguments, .. written], CarrierTypeParameters(method, block));
    }

    private static void Separate(CSharpWriter writer, ref bool first)
    {
        if(!first)
        {
            writer.WriteBlankLine();
        }

        first = false;
    }
}