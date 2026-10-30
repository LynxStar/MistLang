using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.ProgramAnalysis;

public sealed class CloneAnalysisIsolationTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void SyntaxMembershipUsesIdentityInsteadOfFileOrCoordinates()
    {
        var inside = Fixture.Node(30);
        var boundary = new AnalysisBoundary([inside]);

        Assert.True(boundary.Contains(inside));
        Assert.False(boundary.Contains(Fixture.Node(30)));
        Assert.False(boundary.Contains(Fixture.Node(60)));
        Assert.False(boundary.Contains(Fixture.Node(30, "other.mist")));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void SyntaxMembershipIncludesChildrenWithoutParentLinks()
    {
        var child = new AssignmentFormExpressionNode();
        var syntax = new ParenthesizedExpressionPrimaryNode
        {
            Inner = child,
        };
        var boundary = new AnalysisBoundary([syntax]);

        Assert.True(boundary.Contains(child));
        Assert.Null(child.Parent);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void DiagnosticMembershipIncludesFileAndSyntheticOrigin()
    {
        var source = Fixture.Node(30);
        var synthetic = Fixture.Node(30);
        synthetic.Origin = new SyntheticOrigin(source.Origin!, "clone conversion");
        var boundary = new AnalysisBoundary([synthetic]);

        Assert.True(boundary.Contains(Fixture.Failure(source)));
        Assert.False(boundary.Contains(Fixture.Failure(Fixture.Node(30, "other.mist"))));
        Assert.False(boundary.Contains(Fixture.Failure(Fixture.Node(60))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void MemberAtDoesNotConfuseIdenticalCoordinatesInDifferentFiles()
    {
        var fixture = new Fixture();
        var wrong = fixture.Field("Wrong", Fixture.Node(30, "other.mist"));
        var correct = fixture.Field("Mate", Fixture.Node(30));

        Assert.Equal("Mate", CloneConversionRefusals.MemberAt([wrong, correct], Fixture.Failure(correct.Node)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void MemberAtChoosesTheInnermostNamedExpressionAroundAFinding()
    {
        var fixture = new Fixture();
        var outer = fixture.Field("Outer", Fixture.Node(10, length: 70));
        var inner = fixture.Field("Mate", Fixture.Node(30, length: 10));

        Assert.Equal("Mate", CloneConversionRefusals.MemberAt([outer, inner], Fixture.Failure(Fixture.Node(32))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void InitializerMemberCanExplainANestedFinding()
    {
        var fixture = new Fixture();
        var initializer = new ObjectInitializerAssignment(Fixture.Node(20, length: 20), "Mate", fixture.Value(), fixture.Source);

        Assert.Equal("Mate", CloneConversionRefusals.MemberAt([initializer], Fixture.Failure(Fixture.Node(25))));
    }

    [Theory]
    [InlineData("method-reference")]
    [InlineData("method-group")]
    [InlineData("function-reference")]
    [Trait("Tier", "1")]
    public void GenericMethodMembersKeepTheirNamesBeforeAndAfterFunctionConversion(string kind)
    {
        var fixture = new Fixture();
        var node = Fixture.Node(30);
        var method = new Method("CopyMate", fixture.Source, [], Fixture.Public);
        Expression member = kind switch
        {
            "method-reference" => new MethodReferenceExpression(node, method),
            "method-group" => new MethodGroupExpression(node, null, method.Name, [method], null),
            _ => new FunctionReferenceExpression(node, null, method, fixture.Model.TypeInterner.GetOrCreateFunctionType([], fixture.Source)),
        };

        Assert.Equal(method.Name, CloneConversionRefusals.MemberAt([member], Fixture.Failure(node)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void BindingOwnershipSeparatesConversionsClonesAndTheProgram()
    {
        var fixture = new Fixture();
        var first = fixture.Conversion(fixture.Value());
        var second = fixture.Conversion(fixture.Value());
        var firstPass = Fixture.BodyScope(first);
        var clonePass = new AnalysisScope([fixture.Clone], [], [], [], RestrictToScope: true);
        var programPass = new AnalysisScope([fixture.Source], [], [], []);

        Assert.True(firstPass.ContainsBinding(new BodyScope(new BodyScope(((CastOperator)first.Member!).Scope))));
        Assert.False(firstPass.ContainsBinding(((CastOperator)second.Member!).Scope));
        Assert.False(firstPass.ContainsBinding(fixture.Source.Scope));
        Assert.True(clonePass.ContainsBinding(fixture.Clone.Scope));
        Assert.False(clonePass.ContainsBinding(fixture.Source.Scope));
        Assert.False(clonePass.ContainsBinding(((CastOperator)first.Member!).Scope));
        Assert.True(programPass.ContainsBinding(fixture.Source.Scope));
        Assert.True(programPass.ContainsBinding(null));
        Assert.False(programPass.ContainsBinding(fixture.Clone.Scope));
        Assert.False(programPass.ContainsBinding(((CastOperator)first.Member!).Scope));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void UnrelatedSameFileConstraintIsReportedOnlyInTheProgramPass()
    {
        var fixture = new Fixture();
        var root = fixture.Conversion(fixture.Value());
        fixture.Close(Fixture.Node(70), fixture.Invalid, fixture.Source.Scope);

        Assert.Empty(fixture.Analyze(Fixture.BodyScope(root), new ConstraintAnalyzer()));
        Assert.Empty(fixture.Judge(root, new ConstraintAnalyzer()));
        Assert.Equal(ProgramAnalysisDiagnostics.ConstraintNotSatisfied.Number,
            Assert.Single(fixture.Analyze(new AnalysisScope([fixture.Source], [], [], []), new ConstraintAnalyzer())).Number);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void GenuineConversionConstraintStillRefusesTheCastAtItsUseSite()
    {
        var fixture = new Fixture();
        var member = fixture.Field("Mate", Fixture.Node(30));
        var root = fixture.Conversion(member);
        fixture.Close(member.Node, fixture.Invalid, ((CastOperator)root.Member!).Scope);
        var refusals = fixture.Judge(root, new ConstraintAnalyzer());
        var refused = Assert.Single(refusals).Value;

        Assert.Equal("Mate", refused.Member);
        Assert.Equal(ProgramAnalysisDiagnostics.ConstraintNotSatisfied.Number, refused.Failure.Number);
        var cast = fixture.Cast((CastOperator)root.Member!, Fixture.Node(90, "use.mist"));
        var finding = Assert.Single(fixture.Analyze(
            new AnalysisScope([], [new SourceRoot(cast, fixture.Source, fixture.Assembly, null)], [], []),
            new CastAnalyzer(), refusals));

        Assert.Equal(ProgramAnalysisDiagnostics.CloneConversionDoesNotHold.Number, finding.Number);
        Assert.Equal("use.mist", finding.Location.Path);
        Assert.Equal(91, finding.Position().Start.Column);
        Assert.Contains("at 'Mate'", finding.Message);
        Assert.Equal(refused.Failure.Ordinal, finding.Cause);
        Assert.DoesNotContain(refused.Failure.Message, finding.Message);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConversionsRebindingTheSameSyntaxHaveIndependentRegistryFindings()
    {
        var fixture = new Fixture();
        var syntax = Fixture.Node(30);
        var first = fixture.Conversion(fixture.Field("First", syntax));
        var second = fixture.Conversion(fixture.Field("Second", syntax));
        fixture.Close(syntax, fixture.Invalid, ((CastOperator)first.Member!).Scope);
        fixture.Close(syntax, fixture.Source, ((CastOperator)second.Member!).Scope);
        fixture.Close(syntax, fixture.Invalid, fixture.Source.Scope);
        fixture.Close(syntax, fixture.Invalid, fixture.Clone.Scope);

        var refusals = fixture.Judge([first, second], new ConstraintAnalyzer());

        Assert.Equal("First", Assert.Single(refusals).Value.Member);
        Assert.Contains((CastOperator)first.Member!, refusals.Keys);
        Assert.Empty(fixture.Analyze(Fixture.BodyScope(second), new ConstraintAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void BodyFailureInsideNestedExpressionsUsesTheRetainedSourceOrigin()
    {
        var fixture = new Fixture();
        var node = Fixture.Node(30);
        node.Origin = new SyntheticOrigin(node.Origin!, "retained clone body");
        var member = fixture.Field("Mate", node);
        var nested = new NewExpression(Fixture.Node(20), fixture.Clone, [member], null, [], [fixture.Clone]);
        var root = fixture.Conversion(nested, target: fixture.Clone);

        var refusal = Assert.Single(fixture.Judge(root, new AssignabilityAnalyzer())).Value;

        Assert.Equal(ProgramAnalysisDiagnostics.NoImplicitConversion.Number, refusal.Failure.Number);
        Assert.Equal("source.mist", refusal.Failure.Location.Path);
        Assert.Equal("Mate", refusal.Member);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void BindingFailureTakesPrecedenceAndDoesNotRunAnalysis()
    {
        var fixture = new Fixture();
        var member = fixture.Field("Mate", Fixture.Node(30));
        var root = fixture.Conversion(member);
        var binding = Fixture.Failure(member.Node);
        ((CastOperator)root.Member!).BindingFailure = binding;

        var refusal = Assert.Single(CloneConversionRefusals.Judge([root], _ => throw new InvalidOperationException("analysis must not run"))).Value;

        Assert.Same(binding, refusal.Failure);
        Assert.Equal("Mate", refusal.Member);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Warning, false)]
    [InlineData(DiagnosticSeverity.Error, true)]
    [Trait("Tier", "2")]
    public void WarningsAndInaccessibleMembersDoNotRefuseAConversion(DiagnosticSeverity severity, bool inaccessible)
    {
        var fixture = new Fixture();
        var member = fixture.Field("PrivateState", Fixture.Node(30));
        var root = fixture.Conversion(member);
        var failure = Fixture.Failure(member.Node,
            inaccessible ? ProgramAnalysisDiagnostics.InaccessibleMember : ProgramAnalysisDiagnostics.ConstraintNotSatisfied,
            severity);

        Assert.Empty(CloneConversionRefusals.Judge([root], _ => [failure]));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void UnrelatedErrorsAreSkippedBeforeSelectingAnApplicableError()
    {
        var fixture = new Fixture();
        var wrong = fixture.Field("Wrong", Fixture.Node(30, "other.mist"));
        var correct = fixture.Field("Mate", Fixture.Node(30));
        var root = fixture.Conversion(correct);
        var applicable = Fixture.Failure(correct.Node);

        var refusal = Assert.Single(CloneConversionRefusals.Judge([root], _ => [Fixture.Failure(wrong.Node), applicable])).Value;

        Assert.Same(applicable, refusal.Failure);
        Assert.Equal("Mate", refusal.Member);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ClonePassKeepsItsOwnRegistryFindingsAndDropsUnrelatedBindings()
    {
        var fixture = new Fixture();
        var shared = Fixture.Node(30);
        var cloneBody = new SourceRoot(fixture.Field("Mate", shared), fixture.Clone, fixture.Assembly, null);
        fixture.Close(shared, fixture.Source, fixture.Source.Scope);
        fixture.Close(shared, fixture.Invalid, fixture.Clone.Scope);
        fixture.Close(Fixture.Node(70), fixture.Invalid, fixture.Source.Scope);

        var program = fixture.Analyze(new AnalysisScope([fixture.Source], [], [], []), new ConstraintAnalyzer());
        var clone = fixture.Analyze(new AnalysisScope([fixture.Clone], [cloneBody], [], [], RestrictToScope: true), new ConstraintAnalyzer());

        Assert.Equal(71, Assert.Single(program).Position().Start.Column);
        Assert.Equal(31, Assert.Single(clone).Position().Start.Column);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void OptionalRegistryUsesTheSameBindingBoundary()
    {
        var fixture = new Fixture();
        var syntax = Fixture.Node(30);
        var root = fixture.Conversion(fixture.Field("Mate", syntax));
        var optional = fixture.Model.TypeInterner.GetOrCreateUnionType([fixture.Source, fixture.Void]);
        fixture.Model.OptionalApplications.Record(optional, syntax, fixture.Source.Scope);
        fixture.Model.OptionalApplications.Record(fixture.Source, syntax, ((CastOperator)root.Member!).Scope);

        Assert.Empty(fixture.Judge(root, new PositionAnalyzer()));
        fixture.Model.OptionalApplications.Record(optional, syntax, ((CastOperator)root.Member!).Scope);
        Assert.Equal(ProgramAnalysisDiagnostics.OptionalOfOptional.Number, Assert.Single(fixture.Judge(root, new PositionAnalyzer())).Value.Failure.Number);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void OpenStampableMethodClosingsKeepTheirApplicableFailure()
    {
        var fixture = new Fixture();
        var member = fixture.Field("Mate", Fixture.Node(30));
        var root = fixture.Conversion(member);
        var method = new Method("Gate", fixture.Source, [], Fixture.Public);
        var required = new TypeParameter("T", 0, fixture.Source.Scope, null);
        required.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        method.TypeParametersList.Add(required);
        var supplied = new TypeParameter("U", 0, fixture.Clone.Scope, null);
        var conversion = (CastOperator)root.Member!;
        fixture.Model.ConstructorConstraintClosures.RecordMethod(method, [supplied],
            TypeSubstitution.Empty.With(required, supplied), member.Node, conversion.Scope);
        fixture.Model.ConstructorConstraintClosures.RecordMethod(method, [fixture.Invalid],
            TypeSubstitution.Empty.With(required, fixture.Invalid), member.Node, fixture.Source.Scope);
        var context = fixture.Context(Fixture.BodyScope(root), new ConstraintAnalyzer(), new DiagnosticBag());

        Assert.Empty(context.Model.ConstructorConstraintClosures.Closings);
        Assert.Same(supplied, Assert.Single(context.Model.ConstructorConstraintClosures.OpenClosings).Argument);
        Assert.Same(supplied, Assert.Single(context.Model.ConstructorConstraintClosures.StampableOpenClosings).Argument);
        Assert.Equal(ProgramAnalysisDiagnostics.ConstraintNotSatisfied.Number, Assert.Single(fixture.Judge(root, new ConstraintAnalyzer())).Value.Failure.Number);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AGenericMethodUsedAsAFunctionNamesTheMemberWhoseConstraintFailed()
    {
        var fixture = new Fixture();
        var method = new Method("CopyMate", fixture.Source, [], Fixture.Public);
        var parameter = new TypeParameter("T", 0, fixture.Source.Scope, null);
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
        method.TypeParametersList.Add(parameter);
        var reference = new FunctionReferenceExpression(Fixture.Node(30), null, method,
            fixture.Model.TypeInterner.GetOrCreateFunctionType([], fixture.Source));
        var root = fixture.Conversion(reference);
        fixture.Model.ConstructorConstraintClosures.RecordMethod(method, [fixture.Invalid],
            TypeSubstitution.Empty.With(parameter, fixture.Invalid), reference.Node, ((CastOperator)root.Member!).Scope);

        Assert.Equal("CopyMate", Assert.Single(fixture.Judge(root, new ConstraintAnalyzer())).Value.Member);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ReportingBoundaryAndErrorValueSuppressionBothApply()
    {
        var fixture = new Fixture();
        var member = fixture.Field("Mate", Fixture.Node(30));
        var root = fixture.Conversion(member);
        var diagnostics = new DiagnosticBag();
        var context = fixture.Context(Fixture.BodyScope(root), new ConstraintAnalyzer(), diagnostics);
        context.Report(ProgramAnalysisDiagnostics.ConstraintNotSatisfied, Fixture.Node(70), [], DiagnosticSnapshot.Type(fixture.Source), new OptionalValue(SlotKind.Type, null), new OptionalValue(SlotKind.Name, new NameValue("class")), new NameValue("T"));
        context.Report(ProgramAnalysisDiagnostics.ConstraintNotSatisfied, member.Node,
            [new ErrorExpression(member.Node)], DiagnosticSnapshot.Type(fixture.Source), new OptionalValue(SlotKind.Type, null), new OptionalValue(SlotKind.Name, new NameValue("class")), new NameValue("T"));
        context.Report(ProgramAnalysisDiagnostics.ConstraintNotSatisfied, member.Node, [member], DiagnosticSnapshot.Type(fixture.Source), new OptionalValue(SlotKind.Type, null), new OptionalValue(SlotKind.Name, new NameValue("class")), new NameValue("T"));

        Assert.Equal(31, Assert.Single(diagnostics.ToImmutableArray()).Position().Start.Column);
    }

    // The runner alone owns partitioning whole-model registries between the program, clone and conversion passes.
    [Fact]
    [Trait("Tier", "3")]
    public void RunnerReportsUnrelatedConstraintOnceWithoutRefusingAValidConversion()
    {
        var fixture = new Fixture();
        var root = fixture.Conversion(fixture.Value(fixture.Clone));
        fixture.Clone.CastOperatorsList.Add((CastOperator)root.Member!);
        fixture.Close(Fixture.Node(70), fixture.Invalid, fixture.Source.Scope);
        var cast = fixture.Cast((CastOperator)root.Member!, Fixture.Node(90, "use.mist"));
        var method = new Method("Run", fixture.Void, [], Fixture.Public with { IsStatic = true })
        {
            DeclaringType = fixture.Source,
            Scope = new MethodScope(fixture.Source.Scope),
            Body = Fixture.Block(new ExpressionStatement(cast)),
        };
        fixture.Source.MethodsList.Add(method);
        var sourceUnit = new CompilationUnit { SourcePath = "source.mist" };
        sourceUnit.Types.AddRange([fixture.Source, fixture.Clone]);
        fixture.Assembly.CompilationUnits.Add(sourceUnit);
        fixture.Assembly.CompilationUnits.Add(new CompilationUnit { SourcePath = "use.mist" });
        fixture.Assembly.CompilationUnits.Add(new CompilationUnit { SourcePath = "newtype.mist" });
        _ = fixture.Model.RegisterType(fixture.Source);
        _ = fixture.Model.RegisterType(fixture.Clone);
        var diagnostics = new DiagnosticBag();

        ProgramAnalysisRunner.Run(fixture.Model, diagnostics);

        Assert.Equal(ProgramAnalysisDiagnostics.ConstraintNotSatisfied.Number, Assert.Single(diagnostics.ToImmutableArray()).Number);
    }

    private sealed class Fixture
    {
        public static MemberModifiers Public { get; } = new() { Accessibility = Accessibility.Public };
        private static readonly HashSet<string> SourcePaths = ["source.mist", "other.mist", "newtype.mist", "use.mist"];

        public Model Model { get; } = new();
        public MistAssembly Assembly { get; }
        public ClassType Source { get; }
        public ClassType Clone { get; }
        public ClassType Void { get; } = new("Void", "Standard", new Scope(null), null, null);
        public StructType Invalid { get; } = new("Invalid", "Probe", new Scope(null), null, null);
        private ClassType Gate { get; }

        public Fixture()
        {
            Assembly = Model.AddSelfAssembly("Probe", isLibrary: true);
            var declaration = new BodyTypeTypeDeclarationNode { Span = new Storm.Runtime.SourceSpan(0, 100), Origin = Origin("source.mist") };
            Source = new ClassType("Tiger", "Probe", new Scope(null), null, declaration) { Modifiers = Public, Assembly = Assembly };
            Clone = new ClassType("Jaguar", "Probe", new Scope(null), null, declaration)
            {
                CloneSource = Source,
                CloneSourceType = Source,
                NewtypeDeclaration = new NewtypeDeclarationNode { Span = new Storm.Runtime.SourceSpan(0, 10), Origin = Origin("newtype.mist") },
                Modifiers = Public,
                Assembly = Assembly,
            };
            Gate = new ClassType("Gate", "Probe", new Scope(null), null, null);
            var parameter = new TypeParameter("T", 0, Gate.Scope, null);
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
            Gate.TypeParametersList.Add(parameter);
        }

        public static SyntaxNode Node(int offset = 0, string path = "source.mist", int length = 1)
        {
            return new TrueLiteralExpressionPrimaryNode { Span = new Storm.Runtime.SourceSpan(offset, length), Origin = Origin(path) };
        }

        private static ParsedOrigin Origin(string path)
        {
            return new ParsedOrigin(new SourceText(path, new string(' ', 200)));
        }

        public Expression Value(IMistType? type = null)
        {
            return new LiteralExpression(Node(20), null, type ?? Source);
        }

        public FieldReferenceExpression Field(string name, SyntaxNode node)
        {
            return new FieldReferenceExpression(node, new Field(name, Source, default, Public) { DeclaringType = Source });
        }

        public static Block Block(params Statement[] statements)
        {
            return new Block(new BlockNode { Span = new Storm.Runtime.SourceSpan(10, 50), Origin = Origin("source.mist") }) { Statements = statements };
        }

        public SourceRoot Conversion(Expression value, IMistType? target = null)
        {
            var written = new Method("Clone", Clone, [], Public) { DeclaringType = Clone };
            var within = new ConversionScope(Clone.Scope);
            var conversion = new CastOperator(Clone, new Parameter("value", Source), false, Public with { IsStatic = true })
            {
                DeclaringType = Clone,
                ClonesThrough = written,
                Body = Block(new ReturnStatement(value, target ?? value.Type)),
                Scope = new MethodScope(within),
            };
            within.Conversion = conversion;
            return new SourceRoot(conversion.Body, Clone, Assembly, conversion);
        }

        public void Close(SyntaxNode node, IMistType argument, Scope scope)
        {
            Model.ConstructorConstraintClosures.RecordType(Gate, [argument], node, boundIn: scope);
        }

        public CastExpression Cast(CastOperator conversion, SyntaxNode node)
        {
            return new CastExpression(node, Value(), Clone, [new ConversionFunction(conversion, Source, Clone)], Clone);
        }

        public static AnalysisScope BodyScope(SourceRoot root)
        {
            return new AnalysisScope([], [root], [], [], RestrictToScope: true);
        }

        public AnalysisContext Context(AnalysisScope scope, IProgramAnalyzer analyzer, DiagnosticBag diagnostics,
            IReadOnlyDictionary<CastOperator, ConversionRefusal>? refusals = null)
        {
            var nodes = new List<object>();
            var roots = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);
            var assignments = new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance);
            foreach(var root in scope.Bodies)
            {
                foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
                {
                    nodes.Add(node);
                    roots[node] = root;
                    if(node is AssignmentExpression assignment)
                    {
                        assignments[assignment.Target] = assignment;
                    }
                }
            }

            return new AnalysisContext(Model, scope, nodes, roots, assignments, SourcePaths, diagnostics, analyzer.Id,
                refusals ?? CloneConversionRefusals.None);
        }

        public IReadOnlyList<Diagnostic> Analyze(AnalysisScope scope, IProgramAnalyzer analyzer,
            IReadOnlyDictionary<CastOperator, ConversionRefusal>? refusals = null)
        {
            var diagnostics = new DiagnosticBag();
            analyzer.Analyze(Context(scope, analyzer, diagnostics, refusals));
            return diagnostics.ToImmutableArray();
        }

        public IReadOnlyDictionary<CastOperator, ConversionRefusal> Judge(SourceRoot root, IProgramAnalyzer analyzer)
        {
            return Judge([root], analyzer);
        }

        public IReadOnlyDictionary<CastOperator, ConversionRefusal> Judge(IEnumerable<SourceRoot> roots, IProgramAnalyzer analyzer)
        {
            return CloneConversionRefusals.Judge(roots, root => Analyze(BodyScope(root), analyzer));
        }

        public static Diagnostic Failure(SyntaxNode node, MistDiagnostic? descriptor = null, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            var selected = descriptor ?? ProgramAnalysisDiagnostics.ConstraintNotSatisfied;
            return DiagnosticFixtures.Occurrence(selected.Number.Value, severity, PipelineStage.Analysis, "blocked", DiagnosticHelper.ResolveLocation(node).Location, "fixture");
        }
    }
}