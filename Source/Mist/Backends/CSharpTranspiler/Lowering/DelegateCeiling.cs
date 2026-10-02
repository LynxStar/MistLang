using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The sixteen inputs System.Func and System.Action stop at (variadics Requirement 16). A function of
/// more is valid Mist that THIS backend has no delegate for, so it is refused here and by no rule of the
/// frontend, in two places because there are two ways to reach one. Written out, it is found on the
/// model before anything is emitted, which is what refuses it inside a variadic declaration nothing
/// instantiates. Reached by a pack -- a Func over <c>TArgs...</c> at a width of seventeen -- it exists
/// only once a specialization fixes the width, so it is caught where the delegate is spelled and
/// reported at the use site that closed the pack to that width.
/// </summary>
internal sealed class DelegateCeiling
{
    public const int Inputs = 16;

    [ThreadStatic]
    private static DelegateCeiling? inScope;

    private readonly DiagnosticBag diagnostics;
    private readonly IReadOnlyList<TypeArgumentClosing> closings;
    private readonly Dictionary<IMistType, FunctionType?> judged = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FunctionType> written = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(SyntaxNode? At, string What)> spelled = [];
    private Declaration? emitting;

    private DelegateCeiling(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
        closings = model.ConstructorConstraintClosures.Closings;
    }

    public static IDisposable Enter(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var restored = inScope;
        var scope = new DelegateCeiling(model, diagnostics);
        inScope = scope;
        scope.ReportWritten(model);

        return new Restore(() => inScope = restored);
    }

    public static IDisposable? Emitting(SyntaxNode? node, string name)
    {
        if(inScope is not { } scope)
        {
            return null;
        }

        var outer = scope.emitting;
        scope.emitting = new Declaration(node, name);

        return new Restore(() => scope.emitting = outer);
    }

    public static void Spelled(FunctionType function, int inputs)
    {
        if(inputs <= Inputs || inScope is not { } scope || scope.written.Contains(function))
        {
            return;
        }

        var monomorph = Monomorph.InScope();
        var what = $"'{TypeDisplay.Mist(function)}'"
            + (scope.emitting is { } declaration ? $" in '{declaration.Name}'" : string.Empty)
            + (monomorph is null ? string.Empty : $", where {Widths(monomorph)},");
        var at = (monomorph is null ? null : InstantiationSite(scope.closings, monomorph.Packs, monomorph.Widths)) ?? scope.emitting?.Node;

        if(!scope.spelled.Add((at, what)))
        {
            return;
        }

        if(at is null)
        {
            scope.diagnostics.Report(BackendDiagnostics.DelegateCeilingExceeded, default, string.Empty, what, inputs, Inputs);
        }
        else
        {
            scope.diagnostics.ReportAtNode(BackendDiagnostics.DelegateCeilingExceeded, at, null, what, inputs, Inputs);
        }
    }

    public static FunctionType? Oversized(IMistType type)
    {
        return Oversized(type, new Dictionary<IMistType, FunctionType?>(ReferenceEqualityComparer.Instance));
    }

    public static SyntaxNode? InstantiationSite(
        IReadOnlyList<TypeArgumentClosing> closings, IReadOnlyList<TypeParameter> packs, IReadOnlyList<int> widths)
    {
        IEnumerable<SyntaxNode>? sites = null;

        for(var i = 0; i < packs.Count; i++)
        {
            var closed = Sites(closings, packs[i], widths[i], []).ToList();

            sites = sites is null ? closed : sites.Where(closed.Contains);
        }

        return sites?.FirstOrDefault();
    }

    private static IEnumerable<SyntaxNode> Sites(
        IReadOnlyList<TypeArgumentClosing> closings, TypeParameter pack, int width, HashSet<(TypeParameter, int)> visited)
    {
        if(width < 0 || !visited.Add((pack, width)))
        {
            yield break;
        }

        foreach(var closing in closings)
        {
            if(!ReferenceEquals(closing.Parameter, pack) || closing.Argument is not LiftedType { Value: TypePackValue value })
            {
                continue;
            }

            var spreads = value.Elements.OfType<SpreadType>().ToList();

            if(spreads.Count == 0 && value.Elements.Length == width)
            {
                yield return closing.Node;
            }
            else if(spreads is [{ Operand: TypeParameter forwarded }])
            {
                foreach(var site in Sites(closings, forwarded, width - (value.Elements.Length - 1), visited))
                {
                    yield return site;
                }
            }
        }
    }

    private static FunctionType? Oversized(IMistType type, Dictionary<IMistType, FunctionType?> judged)
    {
        if(judged.TryGetValue(type, out var known))
        {
            return known;
        }

        judged[type] = null;

        var found = type switch
        {
            FunctionType function when function.ParameterTypes.Count(parameter => parameter is not SpreadType) > Inputs => function,
            FunctionType function => First([.. function.ParameterTypes, function.ReturnType], judged),
            ConstructedType constructed => First(constructed.TypeArguments, judged),
            LiftedType { Value: TypePackValue pack } => First(pack.Elements, judged),
            NamedTupleType named => First(named.ElementTypes, judged),
            TupleType tuple => First(tuple.ElementTypes, judged),
            UnionType union => First(union.Constituents, judged),
            SpreadType spread => Oversized(spread.Operand, judged),
            RefinedType refined => Oversized(refined.BaseType, judged),
            _ => null,
        };

        judged[type] = found;

        return found;
    }

    private static FunctionType? First(IReadOnlyList<IMistType> types, Dictionary<IMistType, FunctionType?> judged)
    {
        foreach(var type in types)
        {
            if(Oversized(type, judged) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Widths(Monomorph monomorph)
    {
        return string.Join(" and ", monomorph.Packs.Select((pack, i) => $"'{pack.Name}' holds {monomorph.Widths[i]} types"));
    }

    private void ReportWritten(ProgramModelClass model)
    {
        var reported = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var type in SourceTypes.Of(model))
        {
            foreach(var (member, node, signature) in Signatures(type))
            {
                Judge(signature, node ?? (type is DeclaredType declared ? Declarations.NodeOf(declared) : null), member, reported);
            }
        }

        foreach(var block in SourceTypes.ExtensionsOf(model))
        {
            foreach(var method in block.MethodsList)
            {
                Judge([method.ReturnType, .. method.ParametersList.Select(parameter => parameter.Type)], method.DeclarationNode, method, reported);
            }

            foreach(var property in block.PropertiesList)
            {
                Judge([property.Type], property.DeclarationNode, property, reported);
            }
        }

        foreach(var root in SourceBodies.Of(model))
        {
            var member = root.Member ?? root.Root;

            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                switch(node)
                {
                    case Expression expression:
                        Judge([expression.Type], expression.Node, member, reported);
                        break;

                    case LocalDeclarationStatement local:
                        Judge([local.Local.Type], local.Initializer?.Node ?? (root.Member as Method)?.DeclarationNode, member, reported);
                        break;

                    case LocalFunctionStatement { Function: var function }:
                        Judge(
                            [function.ReturnType, .. function.ParametersList.Select(parameter => parameter.Type)],
                            function.DeclarationNode, member, reported);
                        break;
                }
            }
        }
    }

    private void Judge(IReadOnlyList<IMistType> types, SyntaxNode? at, object member, HashSet<object> reported)
    {
        if(at is null || First(types, judged) is not { } function)
        {
            return;
        }

        _ = written.Add(function);

        if(reported.Add(member))
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.DelegateCeilingExceeded, at, null,
                $"'{TypeDisplay.Mist(function)}'", function.ParameterTypes.Count(parameter => parameter is not SpreadType), Inputs);
        }
    }

    private static IEnumerable<(object Member, SyntaxNode? Node, IReadOnlyList<IMistType> Types)> Signatures(MistType type)
    {
        switch(type)
        {
            case TypeAlias { AliasedType: { } aliased }:
                yield return (type, null, [aliased]);
                break;

            case Newtype { SourceType: { } source }:
                yield return (type, null, [source]);
                break;

            case MemberType member:
                foreach(var method in member.EnumerateDelegates())
                {
                    yield return (method, method.DeclarationNode, [method.ReturnType, .. method.ParametersList.Select(parameter => parameter.Type)]);
                }

                foreach(var property in member.PropertiesList)
                {
                    yield return (property, property.DeclarationNode, [property.Type]);
                }

                foreach(var indexer in member.IndexersList)
                {
                    yield return (indexer, indexer.DeclarationNode, [indexer.Type, .. indexer.ParametersList.Select(parameter => parameter.Type)]);
                }

                foreach(var field in (member as ConcreteType)?.FieldsList ?? [])
                {
                    yield return (field, field.DeclarationNode, [field.Type]);
                }

                break;
        }
    }

    private sealed record Declaration(SyntaxNode? Node, string Name);

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose()
        {
            restore();
        }
    }
}