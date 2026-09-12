using System.Reflection;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Whether the backend keeps each promise Standard makes with `intrinsic`. DERIVED RATHER THAN
/// TABULATED: an operator, a cast and an indexer are written as C# syntax the backing type answers; a
/// member Mist spells differently from the CLR is one of the respellings <see cref="IntrinsicCalls"/>
/// lists, or a task's Drop, which is spelled <c>await</c>; and everything else is emitted as the call it was written as, which C# resolves against the
/// CLR type the declaring type projects onto -- so that is where the name is looked up, in the same
/// metadata the emitted code compiles against. A member found nowhere is refused at its call site
/// (standard-library spec Req 5.9) instead of reaching Roslyn as a call that does not exist, which is
/// how Type.Of, Clone and Task.Drop were each found.
/// <para>
/// The type is the one the emitter writes, read from <see cref="ClrSpelling"/> by its metadata name. A
/// nested intrinsic type is the CLR nesting of the same name, or failing that whatever the CLR returns
/// where Mist returns it; an array's Enumerator is neither, being a struct the backend generates, so
/// its members are checked against that struct's surface (<see cref="ArrayEnumeration"/>).
/// </para>
/// </summary>
internal sealed class IntrinsicSupply
{
    public enum Kind
    {
        Respelled,

        Structural,

        Metadata,

        Missing,
    }

    public sealed record Entry(object Member, string Description, Kind Kind);

    private const BindingFlags Visible =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    [ThreadStatic]
    private static IntrinsicSupply? inScope;

    private readonly HashSet<object> unsupplied;

    private IntrinsicSupply(HashSet<object> unsupplied)
    {
        this.unsupplied = unsupplied;
    }

    public static IDisposable Enter(ProgramModelClass programModel, Func<string, Type?> clrType)
    {
        var missing = Audit(programModel, clrType)
            .Where(entry => entry.Kind == Kind.Missing)
            .Select(entry => entry.Member)
            .ToHashSet();

        inScope = new IntrinsicSupply(missing);
        return new Scope();
    }

    public static bool Supplies(Method member)
    {
        return inScope is not { } scope || !scope.unsupplied.Contains(member);
    }

    public static bool Supplies(Property member)
    {
        return inScope is not { } scope || !scope.unsupplied.Contains(member);
    }

    public static IReadOnlyList<Entry> Audit(ProgramModelClass programModel, Func<string, Type?> clrType)
    {
        var entries = new List<Entry>();

        foreach(var type in IntrinsicTypes(programModel))
        {
            foreach(var method in type.EnumerateDelegates().Where(method => method.Modifiers.IsIntrinsic))
            {
                entries.Add(new Entry(method, Describe(method), Classify(method, clrType)));
            }

            foreach(var property in type.PropertiesList.Where(property => property.Modifiers.IsIntrinsic))
            {
                entries.Add(new Entry(property, Describe(property), Classify(property, clrType)));
            }

            foreach(var indexer in type.IndexersList.Where(indexer => indexer.Modifiers.IsIntrinsic))
            {
                entries.Add(new Entry(indexer, $"{Declaring(indexer.DeclaringType)}.this[]", Kind.Structural));
            }
        }

        return entries;
    }

    public static string Describe(Method method)
    {
        var parameters = string.Join(", ", method.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type)));
        var name = method switch
        {
            Constructor => "new",
            CastOperator cast => $"operator {TypeDisplay.Mist(cast.TargetType)}",
            Operator op => $"operator {op.Symbol}",
            _ => method.Name,
        };

        return $"{Declaring(method.DeclaringType)}.{name}({parameters})";
    }

    public static string Describe(Property property)
    {
        return $"{Declaring(property.DeclaringType)}.{property.Name}";
    }

    private static string Declaring(MemberType? type)
    {
        if(type is null)
        {
            return "?";
        }

        var parameters = type.TypeParametersList.Count > 0
            ? $"<{string.Join(", ", type.TypeParametersList.Select(parameter => parameter.Name))}>"
            : "";
        var segment = $"{MistType.StripArity(type.Name)}{parameters}";

        if(type.DeclaringType is MemberType outer)
        {
            return $"{Declaring(outer)}.{segment}";
        }

        return string.IsNullOrEmpty(type.NamespaceName) ? segment : $"{type.NamespaceName}.{segment}";
    }

    private static IEnumerable<ConcreteType> IntrinsicTypes(ProgramModelClass programModel)
    {
        var pending = new Stack<MistType>();

        foreach(var unit in programModel.StandardAssembly?.CompilationUnits ?? [])
        {
            foreach(var type in unit.Types)
            {
                pending.Push(type);
            }
        }

        while(pending.Count > 0)
        {
            var type = pending.Pop();

            foreach(var nested in type.NestedTypes)
            {
                pending.Push(nested);
            }

            if(type is ConcreteType { IsIntrinsic: true } intrinsic)
            {
                yield return intrinsic;
            }
        }
    }

    private static Kind Classify(Method method, Func<string, Type?> clrType)
    {
        if(IntrinsicCalls.Respells(method) || TaskResult.IsDrop(method))
        {
            return Kind.Respelled;
        }

        if(method is Operator or CastOperator)
        {
            return Kind.Structural;
        }

        if(ArrayEnumeration.IsEnumerator(method.DeclaringType))
        {
            return ArrayEnumeration.Supplies(method.Name) ? Kind.Respelled : Kind.Missing;
        }

        if(Backing(method.DeclaringType, clrType) is not { } clr)
        {
            return Kind.Missing;
        }

        var count = method.ParametersList.Count;
        var found = method is Constructor
            ? clr.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Length == count)
            : clr.GetMethods(Visible).Any(candidate =>
                candidate.Name == method.Name
                && candidate.IsStatic == method.Modifiers.IsStatic
                && candidate.GetParameters().Length == count);

        return found ? Kind.Metadata : Kind.Missing;
    }

    private static Kind Classify(Property property, Func<string, Type?> clrType)
    {
        if(IntrinsicCalls.RespellsProperty(property.Name, property.DeclaringType))
        {
            return Kind.Respelled;
        }

        if(ArrayEnumeration.IsEnumerator(property.DeclaringType))
        {
            return ArrayEnumeration.Supplies(property.Name) ? Kind.Respelled : Kind.Missing;
        }

        if(Backing(property.DeclaringType, clrType) is not { } clr)
        {
            return Kind.Missing;
        }

        return clr.GetProperties(Visible).Any(candidate => candidate.Name == property.Name)
            ? Kind.Metadata
            : Kind.Missing;
    }

    private static Type? Backing(MemberType? type, Func<string, Type?> clrType)
    {
        if(type is null)
        {
            return null;
        }

        if(type.DeclaringType is MemberType outer)
        {
            return Backing(outer, clrType) is { } outerClr
                ? outerClr.GetNestedType(MistType.StripArity(type.Name), BindingFlags.Public) ?? ReturnedWhere(outer, type, outerClr)
                : null;
        }

        if(ClrSpelling.MetadataName(type) is not { } metadataName)
        {
            return null;
        }

        if(metadataName.Contains('`'))
        {
            return clrType(metadataName);
        }

        for(var arity = 0; arity <= type.TypeParametersList.Count; arity++)
        {
            if(clrType(arity == 0 ? metadataName : $"{metadataName}`{arity}") is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Type? ReturnedWhere(MemberType outer, MemberType nested, Type outerClr)
    {
        foreach(var method in outer.MethodsList)
        {
            if(method.ReturnType.FullyQualifiedName != nested.FullyQualifiedName)
            {
                continue;
            }

            var returned = outerClr.GetMethods(Visible)
                .FirstOrDefault(candidate =>
                    candidate.Name == method.Name
                    && candidate.GetParameters().Length == method.ParametersList.Count)
                ?.ReturnType;

            if(returned is not null)
            {
                return returned;
            }
        }

        return null;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            inScope = null;
        }
    }
}