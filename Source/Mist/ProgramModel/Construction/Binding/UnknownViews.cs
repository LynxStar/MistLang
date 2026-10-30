using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A member the receiver hides: its name, what the site does with it, and the type parameter whose
/// storage it writes.
/// </summary>
internal readonly record struct HiddenMember(string Name, string Operation, TypeParameter Parameter);

/// <summary>
/// What a receiver closed with <c>unknown</c> may still reach (unknown-type Requirement 10). A member
/// is hidden when the storage it writes is typed by a parameter the receiver binds to <c>unknown</c>:
/// the value stored would be the top type, where the runtime object holds the concrete argument it was
/// built with. Which members write which parameters is <see cref="StorageWriteSummaries"/>; a receiver
/// inside the declaring body is open, binds nothing to <c>unknown</c>, and hides nothing.
/// </summary>
internal static class UnknownViews
{
    public static bool IsClosedWithUnknown(IMistType receiverType)
    {
        return Ancestry.Unwrap(receiverType) is ConstructedType constructed && TypeParameterOccurrences.BearsUnknown(constructed);
    }

    /// <summary>
    /// The first written parameter the receiver binds to an argument bearing <c>unknown</c>, or null.
    /// </summary>
    public static TypeParameter? HiddenBy(
        IEnumerable<TypeParameter> writes, TypeSubstitution substitution, TypeInterner? interner)
    {
        foreach(var written in writes)
        {
            if(TypeParameterOccurrences.BearsUnknown(substitution.Apply(written, interner)))
            {
                return written;
            }
        }

        return null;
    }

    /// <summary>
    /// The group without its hidden overloads, or null when nothing survives, with the first hidden
    /// member for the diagnostic. Readers of the same name stay selectable, so a hidden writer costs the
    /// program only itself.
    /// </summary>
    public static MethodGroupResult? Filter(
        MethodGroupResult group, ConstructedType receiver, BuilderContext context, out HiddenMember? hidden)
    {
        hidden = null;
        var interner = context.ProgramModel.TypeInterner;
        var kept = new List<Method>(group.Methods.Count);

        foreach(var method in group.Methods)
        {
            var path = group.Paths is { } paths && paths.TryGetValue(method, out var reached) ? reached : null;

            if(HiddenMethod(method, TypeSubstitution.ForPath(receiver, path, interner), context) is { } found)
            {
                hidden ??= found;
                continue;
            }

            kept.Add(method);
        }

        if(kept.Count == 0)
        {
            return null;
        }

        return kept.Count == group.Methods.Count ? group : new MethodGroupResult(kept, group.Paths);
    }

    /// <summary>
    /// Extension methods are closed over their block's parameters by the receiver rather than reached
    /// through its construction, so the block's closing is the substitution to read.
    /// </summary>
    public static MethodGroupResult? FilterExtensions(MethodGroupResult group, BuilderContext context, out HiddenMember? hidden)
    {
        hidden = null;
        var kept = new List<Method>(group.Methods.Count);

        foreach(var method in group.Methods)
        {
            if(HiddenMethod(method, method.BlockClosing, context) is { } found)
            {
                hidden ??= found;
                continue;
            }

            kept.Add(method);
        }

        if(kept.Count == 0)
        {
            return null;
        }

        return kept.Count == group.Methods.Count ? group : new MethodGroupResult(kept, group.Paths);
    }

    private static HiddenMember? HiddenMethod(Method method, TypeSubstitution substitution, BuilderContext context)
    {
        if(!context.StorageWrites.TryWrites(method, StorageAccess.Read, context, out var writes))
        {
            return null;
        }

        return HiddenBy(writes, substitution, context.ProgramModel.TypeInterner) is { } parameter
            ? new HiddenMember(method.Name, "called", parameter)
            : null;
    }

    /// <summary>
    /// A field or property reached on the receiver, judged for the direction the site uses it in. A
    /// field read never writes; a property's getter and setter are classified apart.
    /// </summary>
    public static HiddenMember? Hidden(LookupResult found, StorageAccess access, ConstructedType receiver, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;

        return found switch
        {
            FieldResult field when access.HasFlag(StorageAccess.Write)
                => Hidden(field.Field, field.Field.Name, StorageAccess.Write, TypeSubstitution.ForMember(receiver, found, interner), context),
            PropertyResult property
                => Hidden(property.Property, property.Property.Name, access, TypeSubstitution.ForMember(receiver, found, interner), context),
            _ => null,
        };
    }

    public static HiddenMember? Hidden(Indexer indexer, StorageAccess access, TypeSubstitution substitution, BuilderContext context)
    {
        return Hidden(indexer, "this[]", access, substitution, context);
    }

    /// <summary>
    /// An intrinsic element written through a construction closed with <c>unknown</c>: the element is
    /// the storage, and its parameter is the one the receiver binds to the top type.
    /// </summary>
    public static HiddenMember? HiddenElement(IMistType receiverType)
    {
        if(Ancestry.Unwrap(receiverType) is not ConstructedType { Definition: IHasTypeParameters definition } constructed)
        {
            return null;
        }

        foreach(var parameter in definition.TypeParametersList)
        {
            if(!parameter.IsDependent
                && parameter.Ordinal < constructed.TypeArguments.Count
                && TypeParameterOccurrences.BearsUnknown(constructed.TypeArguments[parameter.Ordinal]))
            {
                return new HiddenMember("this[]", "assigned", parameter);
            }
        }

        return null;
    }

    private static HiddenMember? Hidden(object member, string name, StorageAccess access, TypeSubstitution substitution, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;

        if(access.HasFlag(StorageAccess.Write)
            && context.StorageWrites.TryWrites(member, StorageAccess.Write, context, out var written)
            && HiddenBy(written, substitution, interner) is { } assigned)
        {
            return new HiddenMember(name, "assigned", assigned);
        }

        if(access.HasFlag(StorageAccess.Read)
            && context.StorageWrites.TryWrites(member, StorageAccess.Read, context, out var reading)
            && HiddenBy(reading, substitution, interner) is { } read)
        {
            return new HiddenMember(name, "read", read);
        }

        return null;
    }

    public static void Report(HiddenMember hidden, IMistType receiverType, SyntaxNode node, BuilderContext context)
    {
        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.UnknownViewHidesWriter, node, null, DiagnosticSnapshot.Type(receiverType), new NameValue(hidden.Name), new TextValue(hidden.Operation), new NameValue(hidden.Parameter.Name));
    }
}