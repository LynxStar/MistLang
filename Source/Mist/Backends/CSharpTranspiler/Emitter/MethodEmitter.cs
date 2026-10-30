using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(
        Method method,
        bool emitReadonly,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        MemberType? owner = null)
    {
        using var emission = EmissionDiagnostics.Enter(diagnostics, method.DeclarationNode);
        if(IntrinsicCloneEmitter.Forwards(method.Modifiers, method.DeclaringType))
        {
            IntrinsicCloneEmitter.EmitMethod(method, writer, diagnostics);
            return;
        }

        var modifiers = MemberModifierEmitter.Emit(method.Modifiers, emitReadonly)
            + (HidesObjectToString(method) ? "new " : "")
            + (AsyncPromotion.EmitsAsync(method) ? "async " : "");

        AttributeEmitter.EmitLines(method.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{EmitSignature(method, diagnostics)}");
        ConstraintEmitter.Emit(Declared(method), writer, liftSlots: true);
        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitMethodBody(method, method.Body, writer, diagnostics, owner: owner);
            }
        }
    }

    private static bool HidesObjectToString(Method method)
    {
        return method is
        {
            Name: "ToString", Modifiers.IsStatic: false, ParametersList.Count: 0, TypeParametersList.Count: 0,
            DeclaringType: ConcreteType { Assembly.HasMistSource: true }, DeclaringBlock: null,
        };
    }

    /// <summary>
    /// Refuses the one <c>ref</c> spelling <see cref="EmitSignature"/> cannot resolve. Kept apart from
    /// the signature because that is also spelled for an interface member and for a restated default,
    /// so the report belongs at the sites that OWN a declaration -- restating someone else's must not
    /// report it twice.
    /// </summary>
    internal static void ReportReadonlyRefReturn(
        Method method, Storm.Runtime.SyntaxNode? locationNode, DiagnosticBag diagnostics)
    {
        if(method.Modifiers.IsRef && method.Modifiers.IsReadonly && locationNode is not null)
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.ReadonlyRefReturn, locationNode, null, DiagnosticSnapshot.Symbol(method, method.Name));
        }
    }

    /// <summary>
    /// Everything of a method declaration but its modifiers and its body, shared with the
    /// interface member path in <see cref="TypeEmitter"/> so the two cannot disagree about
    /// what a method's signature contains.
    /// </summary>
    internal static string EmitSignature(Method method, DiagnosticBag diagnostics, string? qualifiedName = null)
    {
        using var emission = EmissionDiagnostics.Enter(diagnostics, method.DeclarationNode);
        var returnType = EmitReturnType(method);
        // A specialization declares the pack's expansion in the pack's place and carries the width
        // in its name, exactly as a specialized TYPE does -- and for the same reason: the widths
        // differ in what they declare, so they cannot be one C# method.
        var declared = Declared(method);
        var name = IdentifierSpelling.Of(method.Name);

        if(Monomorph.InScope() is { } monomorph && monomorph.Owns(method.TypeParametersList))
        {
            name = MonomorphSet.SpecializedName(method.Name, monomorph.Widths);
        }
        else if(UnionSpecialization.CurrentMethod() is { } instance && ReferenceEquals(instance.Method, method))
        {
            declared = [];
            name = UnionSpecialization.Name(method.Name, instance.Arguments);
        }
        else if(UnionSpecialization.Current() is { } enclosing && UnionSpecialization.CollidingName(method, enclosing) is { } renamed)
        {
            name = renamed;
        }

        var spelled = declared.Select(DependentCarrierNames.Parameter)
            .Concat(SlotLift.Synthesized(declared).Select(slot => slot.Name))
            .ToList();
        var typeParams = spelled.Count > 0 ? $"<{string.Join(", ", spelled)}>" : "";
        var parts = DefaultWitnesses.StaticParameters(method).Select(DefaultConstraintWitness.Parameter).ToList();
        parts.AddRange(DefaultConstraintWitness.Parameters(declared));
        parts.AddRange(method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType owner }
            ? ConstructorWitnesses.Parameters(ConstructorWitnesses.Stored(owner)) : []);
        parts.AddRange(ConstructorWitnesses.Parameters(ConstructorWitnesses.Of(declared)));
        parts.AddRange(ContextKeyWitnesses.Parameters(method));
        var parameters = ParameterEmitter.Emit(method.ParametersList, diagnostics);

        if(parameters.Length > 0)
        {
            parts.Add(parameters);
        }

        return $"{returnType} {qualifiedName ?? name}{typeParams}({string.Join(", ", parts)})";
    }

    internal static List<string> ConstraintLines(Method method)
    {
        return ConstraintEmitter.Lines(Declared(method), liftSlots: true);
    }

    private static List<TypeParameter> Declared(Method method)
    {
        return Monomorph.InScope() is { } monomorph && monomorph.Owns(method.TypeParametersList)
            ? [.. monomorph.Parameters(method.TypeParametersList)]
            : method.TypeParametersList;
    }

    internal static string ForwardingTypeArguments(Method slot, Method? answer = null)
    {
        var declared = Declared(slot);
        var parameters = declared.Select(DependentCarrierNames.Parameter)
            .Concat(SlotLift.Synthesized(answer is null ? declared : Declared(answer)).Select(slot => slot.Name)).ToList();

        return parameters.Count > 0 ? $"<{string.Join(", ", parameters)}>" : "";
    }

    /// <summary>
    /// What an explicit implementation of <paramref name="slot"/> passes on to <paramref name="answer"/>:
    /// each hidden channel the ANSWER requires, supplied from the slot's parameter of the same identity
    /// -- a default witness by position, a constructor witness by its signature under
    /// <paramref name="closing"/>, a context key by the key -- and synthesized where Mist admitted the
    /// answer without one (a struct promise entails <c>new()</c>); then the declared arguments. An answer
    /// requiring less than its slot leaves the surplus unforwarded, which is the whole point of bridging.
    /// </summary>
    internal static string ForwardingArguments(Method slot, Method? answer = null, TypeSubstitution? closing = null)
    {
        var declared = Declared(slot);
        var answering = answer is null ? declared : Declared(answer);
        closing ??= TypeSubstitution.ForParameters(declared, [.. answering]);
        var arguments = new List<string>();

        for(var i = 0; i < declared.Count && i < answering.Count; i++)
        {
            if(DefaultConstraintWitness.Constrains(answering[i]))
            {
                arguments.Add(DefaultConstraintWitness.Name(declared[i]));
            }
        }

        var promised = ConstructorWitnesses.Of(declared);

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForParameters(answering, [.. declared])))
        {
            foreach(var required in ConstructorWitnesses.Of(answering))
            {
                var position = answering.IndexOf(required.Parameter);
                var signature = Spelled(required.Signature, TypeSubstitution.Empty);
                var supplied = promised.FirstOrDefault(witness => position >= 0 && ReferenceEquals(witness.Parameter, declared[position])
                    && Spelled(witness.Signature, closing) == signature);

                arguments.Add(supplied is null ? ConstructorWitnesses.DefaultArguments([required])[0] : ConstructorWitnesses.Name(supplied));
            }

            var keys = ContextKeyWitnesses.Keys(slot).Select(key => closing.Apply(key, interner: null)).ToList();
            var ordinals = ContextKeyWitnesses.Ordinals(slot);

            foreach(var key in ContextKeyWitnesses.Keys(answer ?? slot))
            {
                var index = keys.FindIndex(promisedKey => ContextKeys.Same(promisedKey, key));
                arguments.Add(index >= 0 ? ContextKeyWitnesses.Name(ordinals[index]) : ContextKeyWitnesses.Spell(key));
            }
        }

        arguments.AddRange(slot.ParametersList.Select(parameter => $"{ArgumentKeywords.For(parameter)}{IdentifierSpelling.Of(parameter.Name)}"));
        return string.Join(", ", arguments);
    }

    private static string Spelled(IReadOnlyList<IMistType> signature, TypeSubstitution substitution)
    {
        return string.Join(",", signature.Select(type => TypeNameEmitter.Emit(substitution.Apply(type, interner: null))));
    }

    /// <summary>
    /// A return type is where Void takes the native C# form rather than Standard.Void, and the
    /// call-site lowering in BodyEmitter reconstructs the unit value wherever a caller
    /// consumes it -- exact, because the type has one inhabitant. Only the bare ref, never
    /// <c>ref readonly</c>: on a method <c>readonly</c> is ALREADY the struct modifier that
    /// MemberModifierEmitter writes, so the pair has two readings and ReadonlyRefReturn refuses it
    /// rather than either being picked here. A promoted method's emitted return type is a task the MODEL
    /// never mentions: Mist declared it sync, and this is only how C# is made to wait. See
    /// <see cref="AsyncPromotion"/>. Shared with the local function path, which spells its return the
    /// same way for the same reasons.
    /// </summary>
    internal static string EmitReturnType(Method method)
    {
        var emitted = AsyncPromotion.IsPromoted(method)
            ? AsyncPromotion.WrapReturn(method.ReturnType)
            : method.ReturnType
            ;

        return OptionalUnion.IsVoid(emitted)
            ? "void"
            : $"{RefKeywords.For(method.Modifiers.IsRef, isReadonly: false)}{TypeNameEmitter.Emit(emitted)}";
    }
}