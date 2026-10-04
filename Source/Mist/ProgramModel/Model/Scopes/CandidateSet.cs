using Mist.ProgramModel.Construction.Binding;
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
/// property or type name is answered by the nearest of its kind, and a name answered at one depth is
/// not in conflict with another kind's at a greater one. Methods do not shadow by name at all -- they
/// accumulate into one overload set spanning every depth, and only an identical signature resolves to
/// a single declaration, the one <see cref="MergeMethods"/> keeps.
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
        var survivors = new List<(LookupResult Result, int Depth)>();

        if(methods.Count > 0)
        {
            survivors.Add((new MethodGroupResult(methods, paths), DepthOf(CandidateKind.Method)));
        }

        foreach(var kind in new[] { CandidateKind.Type, CandidateKind.Field, CandidateKind.Property, CandidateKind.Other })
        {
            var nearest = NearestOfKind(kind);
            if(nearest.Count == 1)
            {
                survivors.Add((nearest[0], DepthOf(kind)));
            }
            else if(nearest.Count > 1)
            {
                return new AmbiguousResult(nearest);
            }
        }

        if(survivors.Count == 0)
        {
            return new NotFoundResult();
        }

        var closest = new List<LookupResult>();
        var nearestDepth = survivors.Min(survivor => survivor.Depth);

        foreach(var survivor in survivors.Where(survivor => survivor.Depth == nearestDepth))
        {
            closest.Add(survivor.Result);
        }

        return closest.Count == 1 ? closest[0] : new AmbiguousResult(closest);
    }

    /// <summary>
    /// Every method candidate as one overload set, and one declaration per signature. WHICH one it
    /// keeps is the language's: a conformer's implementation answers an abstract's slot, so a nearer
    /// declaration wins there; between two CLASSES nothing may hide, so the inherited declaration is
    /// the one that stands and the nearer one, which DeclarationShapeAnalyzer refuses where it is written,
    /// answers nothing. Same name and a different signature is another overload and both are kept, as is
    /// a signature naming a type that did not resolve, which has no identity to coincide with -- dropping
    /// one would answer a call with "no such member" on top of the unresolved type already reported.
    /// </summary>
    private (List<Method> Methods, Dictionary<Method, IReadOnlyList<ConstructedType>>? Paths) MergeMethods()
    {
        var standing = new Dictionary<string, (Method Method, int Depth, IReadOnlyList<ConstructedType>? Path)>(StringComparer.Ordinal);
        var order = new List<string>();
        var unresolved = 0;

        foreach(var candidate in Ordered(CandidateKind.Method))
        {
            if(candidate.Result is not MethodGroupResult group)
            {
                continue;
            }

            foreach(var method in group.Methods)
            {
                var key = OverloadSignature.Of(method, TypeSubstitution.Empty, null) ?? $"#{unresolved++}";
                var path = group.Paths?.GetValueOrDefault(method);

                if(!standing.TryGetValue(key, out var kept))
                {
                    order.Add(key);
                    standing[key] = (method, candidate.Depth, path);
                    continue;
                }

                if(candidate.Depth > kept.Depth && method.DeclaringType is not AbstractType && kept.Method.DeclaringType is not AbstractType)
                {
                    standing[key] = (method, candidate.Depth, path);
                }
            }
        }

        var merged = new List<Method>(order.Count);
        var paths = new Dictionary<Method, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);

        foreach(var key in order)
        {
            var (method, _, path) = standing[key];
            merged.Add(method);

            if(path is not null)
            {
                paths[method] = path;
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

    private int DepthOf(CandidateKind kind)
    {
        var nearest = int.MaxValue;

        foreach(var candidate in candidates.Where(candidate => KindOf(candidate.Result) == kind))
        {
            nearest = Math.Min(nearest, candidate.Depth);
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

}