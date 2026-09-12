using System.Text;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The matches found for a name within one unit of lookup -- a scope, or a type together with its
/// bases -- before deciding between them. Collecting first is what lets a genuine conflict be
/// reported instead of resolving to whichever store happened to be probed first.
/// <para>
/// Candidates carry the depth they were found at: 0 for the type itself, 1 for its immediate bases,
/// and so on. Depth decides shadowing, and the two kinds of member shadow differently. A field,
/// property or type name is shadowed wholesale by a nearer one of the same kind, so a derived
/// declaration hides the base's and that is not a conflict. Methods do not shadow by name at all --
/// they accumulate into one overload set spanning every depth, and only an identical signature is
/// hidden by a nearer one.
/// </para>
/// </summary>
internal sealed class CandidateSet
{
    private readonly List<Candidate> candidates = [];

    private readonly record struct Candidate(LookupResult Result, int Depth);

    private enum CandidateKind
    {
        Type,
        Method,
        Field,
        Property,
        Other,
    }

    public bool IsEmpty => candidates.Count == 0;

    /// <summary>
    /// Records a match found on the type itself.
    /// </summary>
    public void Add(LookupResult candidate)
    {
        Add(candidate, depth: 0);
    }

    /// <summary>
    /// Records a match, and how far up the base chain it was found.
    /// </summary>
    public void Add(LookupResult candidate, int depth)
    {
        candidates.Add(new Candidate(candidate, depth));
    }

    /// <summary>
    /// Nested types and type parameters from a scope's own table. Neither is instance or static
    /// state, so member visibility does not apply.
    /// </summary>
    public void AddTypeNames(Dictionary<string, IMistType> table, LookupRequest request)
    {
        if(table.TryGetValue(request.Name, out var type))
        {
            Add(new TypeResult(type));
        }
    }

    /// <summary>
    /// The overloads a scope's own method table offers that the request admits. Excluding the rest,
    /// rather than matching and rejecting them, is what lets the lookup continue outward.
    /// </summary>
    public void AddMethods(Dictionary<string, List<Method>> table, LookupRequest request)
    {
        if(!table.TryGetValue(request.Name, out var methods))
        {
            return;
        }

        var visible = new List<Method>(methods.Count);
        foreach(var method in methods)
        {
            if(request.Permits(method.Modifiers.IsStatic))
            {
                visible.Add(method);
            }
        }

        if(visible.Count > 0)
        {
            Add(new MethodGroupResult(visible));
        }
    }

    /// <summary>
    /// One surviving kind wins, several are ambiguous, none is a miss. Ambiguity is per unit, not
    /// global: an inner scope's single answer beats an outer scope's conflict, because the outer
    /// scope is never asked.
    /// </summary>
    public LookupResult Resolve()
    {
        if(candidates.Count == 0)
        {
            return new NotFoundResult();
        }

        var (methods, paths) = MergeMethods();
        var survivors = new List<LookupResult>();

        if(methods.Count > 0)
        {
            survivors.Add(new MethodGroupResult(methods, paths));
        }

        foreach(var kind in new[] { CandidateKind.Type, CandidateKind.Field, CandidateKind.Property, CandidateKind.Other })
        {
            var nearest = NearestOfKind(kind);
            if(nearest.Count == 1)
            {
                survivors.Add(nearest[0]);
            }
            else if(nearest.Count > 1)
            {
                return new AmbiguousResult(nearest);
            }
        }

        return survivors.Count switch
        {
            1 => survivors[0],
            0 => new NotFoundResult(),
            _ => new AmbiguousResult(survivors),
        };
    }

    /// <summary>
    /// Every method candidate as one overload set, nearest first, dropping any whose signature a
    /// nearer declaration already provides. That drop is the whole of method shadowing: same name and
    /// signature at a greater depth is overridden, same name and a different signature is another
    /// overload.
    /// </summary>
    private (List<Method> Methods, Dictionary<Method, IReadOnlyList<ConstructedType>>? Paths) MergeMethods()
    {
        var merged = new List<Method>();
        var paths = new Dictionary<Method, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

        foreach(var candidate in Ordered(CandidateKind.Method))
        {
            if(candidate.Result is not MethodGroupResult group)
            {
                continue;
            }

            foreach(var method in group.Methods)
            {
                if(!seenSignatures.Add(SignatureKey(method)))
                {
                    continue;
                }

                merged.Add(method);

                if(group.Paths is { } reached && reached.TryGetValue(method, out var path))
                {
                    paths[method] = path;
                }
            }
        }

        return (merged, paths.Count == 0 ? null : paths);
    }

    /// <summary>
    /// The candidates of one kind at the shallowest depth any of them was found at. More than one
    /// means a single type declared the same name twice.
    /// </summary>
    private List<LookupResult> NearestOfKind(CandidateKind kind)
    {
        var nearestDepth = int.MaxValue;
        foreach(var candidate in candidates)
        {
            if(KindOf(candidate.Result) == kind && candidate.Depth < nearestDepth)
            {
                nearestDepth = candidate.Depth;
            }
        }

        var nearest = new List<LookupResult>();
        foreach(var candidate in candidates)
        {
            if(KindOf(candidate.Result) == kind && candidate.Depth == nearestDepth)
            {
                nearest.Add(candidate.Result);
            }
        }

        return nearest;
    }

    private List<Candidate> Ordered(CandidateKind kind)
    {
        var ofKind = new List<Candidate>();
        foreach(var candidate in candidates)
        {
            if(KindOf(candidate.Result) == kind)
            {
                ofKind.Add(candidate);
            }
        }

        ofKind.Sort((left, right) => left.Depth.CompareTo(right.Depth));
        return ofKind;
    }

    private static CandidateKind KindOf(LookupResult result)
    {
        return result switch
        {
            TypeResult => CandidateKind.Type,
            MethodGroupResult => CandidateKind.Method,
            FieldResult => CandidateKind.Field,
            PropertyResult => CandidateKind.Property,
            _ => CandidateKind.Other,
        };
    }

    /// <summary>
    /// Identifies an overload for shadowing purposes: generic arity, then each parameter's passing
    /// mode and type. The name is not part of it because every candidate here already matched the
    /// same name.
    /// <para>
    /// PASSING MODE BELONGS IN THE KEY. Both Mist and C# let a by-ref and a by-value parameter of the
    /// same type overload one another, so keying on types alone collapsed <c>M(int)</c> and
    /// <c>M(ref int)</c> into one entry and silently discarded whichever was ordered second. Overload
    /// resolution then had a single candidate, returned it without consulting applicability, and every
    /// call to either printed the survivor's answer with no diagnostic.
    /// </para>
    /// </summary>
    private static string SignatureKey(Method method)
    {
        var key = new StringBuilder();
        _ = key.Append(method.TypeParametersList.Count).Append('(');

        for(var i = 0; i < method.ParametersList.Count; i++)
        {
            if(i > 0)
            {
                _ = key.Append(',');
            }

            var parameter = method.ParametersList[i];
            _ = key.Append(parameter.Modifier.IsRef ? "ref " : "").Append(parameter.Type.FullyQualifiedName);
        }

        return key.Append(')').ToString();
    }
}