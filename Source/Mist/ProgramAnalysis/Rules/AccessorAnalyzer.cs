using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The accessor a use calls -- the getter to read, the setter to write, both for <c>??=</c> -- must exist,
/// must be accessible where it is called, and if it is <c>init</c> must be called in construction: the
/// properties spec's Requirements 13.1-13.2, 13.5 and 11.4-11.5. An indexer is judged as its index selected
/// it, never chosen again by accessor, because C# selects first and then refuses -- a sibling that has the
/// accessor does not rescue the one the index picked. Construction is an object initializer entry, or
/// <c>this</c> in an instance constructor or an init accessor of the type or a derivation. A get-only
/// auto-property stays assignable in its own type's constructor body alone, where the write lands on the
/// backing field. A lambda or local function in either body is a body of its own and gets neither
/// privilege. Whether the member and its type are accessible is <see cref="AccessibilityAnalyzer"/>'s to say,
/// so nothing more is said here about an accessor of either out of reach.
/// </summary>
internal sealed class AccessorAnalyzer : IProgramAnalyzer
{
    public string Id => "operator-and-positional-checks";

    public void Analyze(AnalysisContext context)
    {
        var nested = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    nested.UnionWith(Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement function:
                    nested.UnionWith(Captures.Inside(function));
                    break;
            }
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case IndexExpression { Indexer: { } indexer } index:
                    Judge(context, Use.Of(context, index), Slot.Of(indexer), Construction.None);
                    break;

                case IndexExpression { Slice: { } slice } sliced:
                    JudgeSlice(context, new Use(sliced, sliced.Node, [sliced], AccessDirection.Read), slice);
                    break;

                case ListPattern { Slice: { } slice } list:
                    JudgeSlice(context, new Use(list, list.Node, [], AccessDirection.Read), slice);
                    break;

                case MemberAccessExpression { Member: PropertyResult { Property: var property } } access:
                    Judge(
                        context, Use.Of(context, access), Slot.Of(property),
                        access.Receiver is ThisExpression ? ConstructionAt(context, access, property, nested) : Construction.None);
                    break;

                case PropertyReferenceExpression reference:
                    Judge(context, Use.Of(context, reference), Slot.Of(reference.Property), ConstructionAt(context, reference, reference.Property, nested));
                    break;

                case ObjectInitializerAssignment { IsTargetFailed: false, Member: PropertyResult { Property: var property } } entry:
                    Judge(context, new Use(entry, entry.Node, [], AccessDirection.Write), Slot.Of(property), Construction.Init);
                    break;

                case PropertySubpattern { IsMemberFailed: false, Member: PropertyResult { Property: var property } } subpattern:
                    Judge(context, new Use(subpattern, subpattern.Node, [], AccessDirection.Read), Slot.Of(property), Construction.None);
                    break;
            }
        }
    }

    private static void Judge(AnalysisContext context, Use use, Slot slot, Construction construction)
    {
        var owner = context.OwnerOf(use.Site);
        var assembly = context.AssemblyOf(use.Site);
        if(use.Direction.HasFlag(AccessDirection.Read))
        {
            if(AccessorDecision.Read(slot.Getter, owner, assembly, slot.Accessibility, slot.Declaring) is { } descriptor)
            {
                Report(context, use, slot, descriptor, slot.Getter, "get");
            }
        }

        if(use.Direction.HasFlag(AccessDirection.Write)
            && AccessorDecision.Write(slot.Setter, construction.HasFlag(Construction.BackingField), construction.HasFlag(Construction.Init),
                owner, assembly, slot.Accessibility, slot.Declaring) is { } written)
        {
            Report(context, use, slot, written, slot.Setter, slot.Setter is { IsInit: true } ? "init" : "set");
        }
    }

    private static void JudgeSlice(AnalysisContext context, Use use, CountedSlice slice)
    {
        Judge(context, use, Slot.Of(slice.Count), Construction.None);

        if(slice.Indexer is { } indexer)
        {
            Judge(context, use, Slot.Of(indexer), Construction.None);
        }
    }

    private static void Report(AnalysisContext context, Use use, Slot slot, DiagnosticDescriptor descriptor, PropertyAccessor? accessor, string keyword)
    {
        if(descriptor == ProgramAnalysisDiagnostics.InaccessibleAccessor)
        {
            context.Report(descriptor, use.At, use.About,
                keyword, slot.Name, accessor!.Accessibility.ToString().ToLowerInvariant(), AccessDomains.Where(accessor.Accessibility));
        }
        else
        {
            context.Report(descriptor, use.At, use.About, slot.Name);
        }
    }

    private static Construction ConstructionAt(AnalysisContext context, Expression access, Property property, HashSet<object> nested)
    {
        if(nested.Contains(access))
        {
            return Construction.None;
        }

        var member = context.MemberOf(access);
        var construction = Construction.None;

        if(property is { Getter.IsAuto: true, Setter: null }
            && member is Constructor { DeclaringType: { } built } constructor
            && ReferenceEquals(built, property.DeclaringType)
            && constructor.Modifiers.IsStatic == property.Modifiers.IsStatic)
        {
            construction |= Construction.BackingField;
        }

        if(member is Constructor { Modifiers.IsStatic: false } || InInitAccessor(context, access, member))
        {
            construction |= Construction.Init;
        }

        return construction;
    }

    private static bool InInitAccessor(AnalysisContext context, Expression access, object? member)
    {
        return member is Property { Setter: { IsInit: true } setter }
            && context.RootOf(access) is { } root
            && (ReferenceEquals(root, setter.Body) || ReferenceEquals(root, setter.ExpressionBody));
    }

    [Flags]
    private enum Construction
    {
        None = 0,
        BackingField = 1,
        Init = 2,
    }

    private readonly record struct Use(object Site, SyntaxNode At, IReadOnlyList<Expression> About, AccessDirection Direction)
    {
        public static Use Of(AnalysisContext context, Expression access)
        {
            return new Use(access, access.Node, [access], context.DirectionOf(access));
        }
    }

    private readonly record struct Slot(
        MistType? Declaring, string Name, Accessibility Accessibility, PropertyAccessor? Getter, PropertyAccessor? Setter)
    {
        public static Slot Of(Property property)
        {
            return new Slot(
                property.DeclaringType, Named(property.DeclaringType, property.Name), property.Modifiers.Accessibility,
                property.Getter, property.Setter);
        }

        public static Slot Of(Indexer indexer)
        {
            return new Slot(
                indexer.DeclaringType, Named(indexer.DeclaringType, "this[]"), indexer.Modifiers.Accessibility,
                indexer.Getter, indexer.Setter);
        }

        private static string Named(IMistType? declaring, string member)
        {
            return declaring is null ? member : $"{TypeDisplay.Mist(declaring)}.{member}";
        }
    }
}