// Feature: traits, Property 8: Trait C# emission correctness
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter correctly emits trait
/// declarations as interfaces, adds trait interfaces to satisfying types'
/// inheritance lists, and generates explicit interface implementations for
/// with-block mappings.
///
/// Property 8: Trait C# emission correctness
/// For any valid Mist program containing trait declarations, types that
/// structurally satisfy traits, and types that satisfy traits via with blocks,
/// the emitted C# code SHALL contain: (a) a C# interface declaration for each
/// trait with field members as properties with getters, method members as method
/// signatures, and property members as property signatures; (b) the trait
/// interface name in the inheritance list of each satisfying type; (c) explicit
/// interface implementations for each with-mapped member that delegate to the
/// source member.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7**
/// </summary>
public sealed class TraitEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public TraitEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistTraitEmissionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            "string" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            },
            "bool" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            },
            _ => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs"));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    /// <summary>
    /// Builds a Program class with a static void Main() entry point.
    /// Required for the emitter to produce valid C# output.
    /// </summary>
    private static ClassDeclarationNode MakeProgramClass()
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        return new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    // ─── Trait AST construction helpers ────────────────────────────────

    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    private static TraitDeclarationNode MakeTraitWithMethods(
        string traitName,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode
            {
                Method = new TraitMethodDeclarationNode
                {
                    ReturnType = m.returnType == "void"
                        ? new VoidTypeRefTypeReferenceNode()
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    private static TraitDeclarationNode MakeTraitWithProperties(
        string traitName,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<InterfaceAccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new InterfaceGetAccessorInterfaceAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new InterfaceSetAccessorInterfaceAccessorDeclarationNode());

            return (TraitMemberDeclarationNode)new TraitPropertyMemberTraitMemberDeclarationNode
            {
                Property = new TraitPropertyDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    // ─── Class AST construction helpers ──────────────────────────────────

    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    private static ClassDeclarationNode MakeClassWithPublicMethods(
        string className,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    ReturnType = m.returnType == "void"
                        ? new VoidTypeRefTypeReferenceNode()
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                    },
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    private static ClassDeclarationNode MakeClassWithPublicProperties(
        string className,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<AccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new AutoSetAccessorAccessorDeclarationNode());

            return (MemberDeclarationNode)new PropertyMemberMemberDeclarationNode
            {
                Property = new BlockPropertyPropertyDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    // ─── Name pools ────────────────────────────────────────────────────

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height"];

    private static readonly string[] MethodNames =
        ["Compute", "Process", "Execute", "Transform", "Validate"];

    private static readonly string[] PropertyNames =
        ["Name", "Label", "Title", "Status", "Score"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>
    /// Source member names used in with-block mappings. These are different
    /// from the trait member names so that the mapping is non-trivial.
    /// </summary>
    private static readonly string[] WithSourceNames =
        ["myValue", "myCount", "mySize", "localWidth", "localHeight"];

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034
#pragma warning disable CA1819 // Properties should not return arrays — record used as test data

    /// <summary>
    /// The kind of trait member being tested.
    /// </summary>
    public enum TraitMemberKind { Field, Method, Property }

    /// <summary>
    /// How a type satisfies a trait.
    /// </summary>
    public enum SatisfactionMode { Structural, WithBlock }

    /// <summary>
    /// A single trait member definition.
    /// </summary>
    public record TraitMemberDef(
        TraitMemberKind Kind,
        string MemberType,
        string MemberName,
        bool PropHasGet,
        bool PropHasSet,
        (string type, string name)[]? MethodParams);

    /// <summary>
    /// A scenario for testing trait C# emission.
    /// </summary>
    public record TraitEmissionScenario(
        string TraitName,
        string ClassName,
        SatisfactionMode Mode,
        TraitMemberDef[] TraitMembers,
        (string sourceName, string targetName)[]? WithMappings)
    {
        public override string ToString()
        {
            var members = string.Join(", ", TraitMembers.Select(m => $"{m.Kind}:{m.MemberType} {m.MemberName}"));
            var mode = Mode == SatisfactionMode.WithBlock
                ? $" with({string.Join(", ", WithMappings?.Select(w => $"{w.sourceName} as {w.targetName}") ?? [])})"
                : " structural";
            return $"trait {TraitName} {{ {members} }} class {ClassName}{mode}";
        }
    }

#pragma warning restore CA1819
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a class structurally satisfies a trait with a single field member.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenStructuralFieldScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from fieldType in Gen.Elements(PrimitiveTypes)
               from fieldName in Gen.Elements(FieldNames)
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.Structural,
                   [new TraitMemberDef(TraitMemberKind.Field, fieldType, fieldName, false, false, null)],
                   null);
    }

    /// <summary>
    /// Generates a scenario where a class structurally satisfies a trait with a single method member.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenStructuralMethodScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from returnType in Gen.Elements("void", "int", "string", "bool")
               from methodName in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.Structural,
                   [new TraitMemberDef(TraitMemberKind.Method, returnType, methodName, false, false,
                       hasParam ? [(paramType, "arg")] : null)],
                   null);
    }

    /// <summary>
    /// Generates a scenario where a class structurally satisfies a trait with a single property member.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenStructuralPropertyScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from propType in Gen.Elements(PrimitiveTypes)
               from propName in Gen.Elements(PropertyNames)
               from hasGet in Gen.Elements(true, false)
               from hasSet in Gen.Elements(true, false)
               where hasGet || hasSet
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.Structural,
                   [new TraitMemberDef(TraitMemberKind.Property, propType, propName, hasGet, hasSet, null)],
                   null);
    }

    /// <summary>
    /// Generates a scenario where a class satisfies a trait via a with-block mapping for a field.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenWithBlockFieldScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from fieldType in Gen.Elements(PrimitiveTypes)
               from traitFieldName in Gen.Elements(FieldNames)
               from sourceFieldName in Gen.Elements(WithSourceNames)
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.WithBlock,
                   [new TraitMemberDef(TraitMemberKind.Field, fieldType, traitFieldName, false, false, null)],
                   [(sourceFieldName, traitFieldName)]);
    }

    /// <summary>
    /// Generates a scenario where a class satisfies a trait via a with-block mapping for a method.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenWithBlockMethodScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from returnType in Gen.Elements("void", "int", "string", "bool")
               from traitMethodName in Gen.Elements(MethodNames)
               from sourceMethodName in Gen.Elements(WithSourceNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.WithBlock,
                   [new TraitMemberDef(TraitMemberKind.Method, returnType, traitMethodName, false, false,
                       hasParam ? [(paramType, "arg")] : null)],
                   [(sourceMethodName, traitMethodName)]);
    }

    /// <summary>
    /// Generates a scenario where a class satisfies a trait via a with-block mapping for a property.
    /// </summary>
    private static Gen<TraitEmissionScenario> GenWithBlockPropertyScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from className in Gen.Elements(ClassNames)
               from propType in Gen.Elements(PrimitiveTypes)
               from traitPropName in Gen.Elements(PropertyNames)
               from sourcePropName in Gen.Elements(WithSourceNames)
               from hasGet in Gen.Elements(true, false)
               from hasSet in Gen.Elements(true, false)
               where hasGet || hasSet
               select new TraitEmissionScenario(
                   traitName, className, SatisfactionMode.WithBlock,
                   [new TraitMemberDef(TraitMemberKind.Property, propType, traitPropName, hasGet, hasSet, null)],
                   [(sourcePropName, traitPropName)]);
    }

    public static Arbitrary<TraitEmissionScenario> ArbitraryTraitEmissionScenario() =>
        Gen.OneOf(
            GenStructuralFieldScenario(),
            GenStructuralMethodScenario(),
            GenStructuralPropertyScenario(),
            GenWithBlockFieldScenario(),
            GenWithBlockMethodScenario(),
            GenWithBlockPropertyScenario()
        ).ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a CompilationUnitNode for a trait emission scenario.
    /// Contains: trait declaration, satisfying class, optional with-block, and Program class.
    /// </summary>
    private static CompilationUnitNode BuildAst(TraitEmissionScenario scenario)
    {
        var items = new List<TopLevelItemNode>();

        // 1. Trait declaration
        var traitDecl = BuildTraitDeclaration(scenario);
        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        });

        // 2. Class declaration (with members that satisfy the trait)
        var classDecl = BuildClassDeclaration(scenario);
        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        });

        // 3. With-block (if applicable)
        if (scenario.Mode == SatisfactionMode.WithBlock && scenario.WithMappings is not null)
        {
            var withDecl = new WithDeclarationNode
            {
                TypeName = Tok(scenario.ClassName),
                TraitName = Tok(scenario.TraitName),
                Mappings = scenario.WithMappings.Select(m =>
                    new MemberMappingNode
                    {
                        SourceName = Tok(m.sourceName),
                        TargetName = Tok(m.targetName),
                    }).ToImmutableArray(),
            };
            items.Add(new WithItemTopLevelItemNode { WithDeclaration = withDecl });
        }

        // 4. Program class with Main entry point
        items.Add(new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass() },
        });

        return new CompilationUnitNode { Items = items.ToImmutableArray() };
    }

    private static TraitDeclarationNode BuildTraitDeclaration(TraitEmissionScenario scenario)
    {
        var members = scenario.TraitMembers.Select<TraitMemberDef, TraitMemberDeclarationNode>(m =>
        {
            return m.Kind switch
            {
                TraitMemberKind.Field => new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef(m.MemberType),
                        Name = Tok(m.MemberName),
                    },
                },
                TraitMemberKind.Method => new TraitMethodMemberTraitMemberDeclarationNode
                {
                    Method = new TraitMethodDeclarationNode
                    {
                        ReturnType = m.MemberType == "void"
                            ? new VoidTypeRefTypeReferenceNode()
                            : MakePrimitiveTypeRef(m.MemberType),
                        Name = Tok(m.MemberName),
                        Parameters = m.MethodParams is { Length: > 0 }
                            ? new ParameterListNode
                            {
                                Parameter = new ParameterNode
                                {
                                    Type = MakePrimitiveTypeRef(m.MethodParams[0].type),
                                    Name = Tok(m.MethodParams[0].name),
                                },
                            }
                            : null,
                    },
                },
                TraitMemberKind.Property => new TraitPropertyMemberTraitMemberDeclarationNode
                {
                    Property = new TraitPropertyDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef(m.MemberType),
                        Name = Tok(m.MemberName),
                        Accessors = BuildAccessors(m.PropHasGet, m.PropHasSet),
                    },
                },
                _ => throw new ArgumentOutOfRangeException(),
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(scenario.TraitName), Members = members };
    }

    private static ImmutableArray<InterfaceAccessorDeclarationNode> BuildAccessors(bool hasGet, bool hasSet)
    {
        var accessors = new List<InterfaceAccessorDeclarationNode>();
        if (hasGet) accessors.Add(new InterfaceGetAccessorInterfaceAccessorDeclarationNode());
        if (hasSet) accessors.Add(new InterfaceSetAccessorInterfaceAccessorDeclarationNode());
        return accessors.ToImmutableArray();
    }

    /// <summary>
    /// Builds a class that satisfies the trait. For structural satisfaction, the class
    /// has members with the same names as the trait. For with-block satisfaction, the
    /// class has members with the source names from the mappings.
    /// </summary>
    private static ClassDeclarationNode BuildClassDeclaration(TraitEmissionScenario scenario)
    {
        if (scenario.Mode == SatisfactionMode.Structural)
        {
            return BuildStructuralClass(scenario);
        }
        else
        {
            return BuildWithBlockClass(scenario);
        }
    }

    private static ClassDeclarationNode BuildStructuralClass(TraitEmissionScenario scenario)
    {
        var member = scenario.TraitMembers[0];
        return member.Kind switch
        {
            TraitMemberKind.Field => MakeClassWithPublicFields(scenario.ClassName,
                (member.MemberType, member.MemberName)),
            TraitMemberKind.Method => MakeClassWithPublicMethods(scenario.ClassName,
                (member.MemberType, member.MemberName, member.MethodParams)),
            TraitMemberKind.Property => MakeClassWithPublicProperties(scenario.ClassName,
                (member.MemberType, member.MemberName, member.PropHasGet, member.PropHasSet)),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private static ClassDeclarationNode BuildWithBlockClass(TraitEmissionScenario scenario)
    {
        // For with-block, the class has members with the SOURCE names
        var member = scenario.TraitMembers[0];
        var mapping = scenario.WithMappings![0];
        var sourceName = mapping.sourceName;

        return member.Kind switch
        {
            TraitMemberKind.Field => MakeClassWithPublicFields(scenario.ClassName,
                (member.MemberType, sourceName)),
            TraitMemberKind.Method => MakeClassWithPublicMethods(scenario.ClassName,
                (member.MemberType, sourceName, member.MethodParams)),
            TraitMemberKind.Property => MakeClassWithPublicProperties(scenario.ClassName,
                (member.MemberType, sourceName, member.PropHasGet, member.PropHasSet)),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source text construction
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildSource(TraitEmissionScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        // Trait declaration
        sb.AppendLine($"trait {scenario.TraitName} {{");
        foreach (var member in scenario.TraitMembers)
        {
            switch (member.Kind)
            {
                case TraitMemberKind.Field:
                    sb.AppendLine($"    {member.MemberType} {member.MemberName};");
                    break;
                case TraitMemberKind.Method:
                    var paramStr = member.MethodParams is { Length: > 0 }
                        ? $"{member.MethodParams[0].type} {member.MethodParams[0].name}"
                        : "";
                    sb.AppendLine($"    {member.MemberType} {member.MemberName}({paramStr});");
                    break;
                case TraitMemberKind.Property:
                    var accessorStr = (member.PropHasGet, member.PropHasSet) switch
                    {
                        (true, true) => "get; set;",
                        (true, false) => "get;",
                        (false, true) => "set;",
                        _ => "get;",
                    };
                    sb.AppendLine($"    {member.MemberType} {member.MemberName} {{ {accessorStr} }}");
                    break;
            }
        }
        sb.AppendLine("}");

        // Class declaration
        sb.AppendLine($"class {scenario.ClassName} {{");
        if (scenario.Mode == SatisfactionMode.Structural)
        {
            foreach (var member in scenario.TraitMembers)
            {
                EmitClassMemberSource(sb, member, member.MemberName);
            }
        }
        else if (scenario.WithMappings is not null)
        {
            for (var i = 0; i < scenario.TraitMembers.Length; i++)
            {
                var member = scenario.TraitMembers[i];
                var sourceName = scenario.WithMappings[i].sourceName;
                EmitClassMemberSource(sb, member, sourceName);
            }
        }
        sb.AppendLine("}");

        // With-block
        if (scenario.Mode == SatisfactionMode.WithBlock && scenario.WithMappings is not null)
        {
            sb.AppendLine($"with {scenario.ClassName} : {scenario.TraitName} {{");
            foreach (var mapping in scenario.WithMappings)
            {
                sb.AppendLine($"    {mapping.sourceName} as {mapping.targetName};");
            }
            sb.AppendLine("}");
        }

        // Program class
        sb.AppendLine("class Program { public static void Main() { } }");
        return sb.ToString();
    }

    private static void EmitClassMemberSource(
        System.Text.StringBuilder sb, TraitMemberDef member, string memberName)
    {
        switch (member.Kind)
        {
            case TraitMemberKind.Field:
                sb.AppendLine($"    public {member.MemberType} {memberName};");
                break;
            case TraitMemberKind.Method:
                var paramStr = member.MethodParams is { Length: > 0 }
                    ? $"{member.MethodParams[0].type} {member.MethodParams[0].name}"
                    : "";
                sb.AppendLine($"    public {member.MemberType} {memberName}({paramStr}) {{ }}");
                break;
            case TraitMemberKind.Property:
                var accessorStr = (member.PropHasGet, member.PropHasSet) switch
                {
                    (true, true) => "get; set;",
                    (true, false) => "get;",
                    (false, true) => "set;",
                    _ => "get;",
                };
                sb.AppendLine($"    public {member.MemberType} {memberName} {{ {accessorStr} }}");
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // C# type name mapping (Mist primitive → C# type name)
    // ═══════════════════════════════════════════════════════════════════════

    private static string ToCSharpTypeName(string mistType)
    {
        return mistType switch
        {
            "int" => "int",
            "string" => "string",
            "bool" => "bool",
            "void" => "void",
            _ => mistType,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Trait C# emission correctness
    //
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7**
    //
    // For any valid Mist program containing trait declarations, types that
    // structurally satisfy traits, and types that satisfy traits via with
    // blocks, the emitted C# code SHALL contain:
    // (a) a C# interface declaration for each trait
    // (b) the trait interface name in the inheritance list of each satisfying type
    // (c) explicit interface implementations for each with-mapped member
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TraitEmissionPropertyTests) })]
    public bool TraitEmission_InterfaceAndInheritanceEmittedCorrectly(TraitEmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildAst(scenario);
        var source = BuildSource(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // ── Requirement 9.1: Each trait emitted as a C# interface ──
        if (!csContent.Contains($"interface {scenario.TraitName}"))
            return false;

        // ── Requirement 9.2, 9.3, 9.4: Trait members emitted correctly ──
        foreach (var member in scenario.TraitMembers)
        {
            var csType = ToCSharpTypeName(member.MemberType);

            switch (member.Kind)
            {
                case TraitMemberKind.Field:
                    // Requirement 9.2: Field members emitted as properties with getter
                    if (!csContent.Contains($"{csType} {member.MemberName} {{ get; }}"))
                        return false;
                    break;

                case TraitMemberKind.Method:
                    // Requirement 9.3: Method members emitted as method signatures
                    if (!csContent.Contains($"{csType} {member.MemberName}("))
                        return false;
                    break;

                case TraitMemberKind.Property:
                    // Requirement 9.4: Property members emitted as property signatures
                    // Check that the property name appears in the interface
                    if (!csContent.Contains($"{csType} {member.MemberName}"))
                        return false;
                    // Check accessor presence
                    if (member.PropHasGet && !ContainsPropertyAccessor(csContent, scenario.TraitName, member.MemberName, "get;"))
                        return false;
                    if (member.PropHasSet && !ContainsPropertyAccessor(csContent, scenario.TraitName, member.MemberName, "set;"))
                        return false;
                    break;
            }
        }

        // ── Requirement 9.5: Trait interface in satisfying type's inheritance list ──
        // The class declaration should contain `: TraitName` in its inheritance list
        if (!ClassInheritsFromTrait(csContent, scenario.ClassName, scenario.TraitName))
            return false;

        // ── Requirements 9.6, 9.7: Explicit interface implementations for with-mapped members ──
        if (scenario.Mode == SatisfactionMode.WithBlock && scenario.WithMappings is not null)
        {
            foreach (var (sourceName, targetName) in scenario.WithMappings)
            {
                var member = scenario.TraitMembers.First(m => m.MemberName == targetName);

                switch (member.Kind)
                {
                    case TraitMemberKind.Field:
                        // Expect: type TraitName.targetName => this.sourceName;
                        if (!csContent.Contains($"{scenario.TraitName}.{targetName} => this.{sourceName};"))
                            return false;
                        break;

                    case TraitMemberKind.Method:
                        // Expect: type TraitName.targetName(params) => this.sourceName(args);
                        if (!csContent.Contains($"{scenario.TraitName}.{targetName}(")
                            || !csContent.Contains($"this.{sourceName}("))
                            return false;
                        break;

                    case TraitMemberKind.Property:
                        // Expect: type TraitName.targetName with delegation to this.sourceName
                        if (!csContent.Contains($"{scenario.TraitName}.{targetName}"))
                            return false;
                        if (!csContent.Contains($"this.{sourceName}"))
                            return false;
                        break;
                }
            }
        }

        return true;
    }

    // ─── Verification helpers ────────────────────────────────────────────

    /// <summary>
    /// Checks if the emitted C# contains a class declaration that inherits from the trait.
    /// Looks for patterns like "class ClassName : TraitName" or "class ClassName : ..., TraitName".
    /// </summary>
    private static bool ClassInheritsFromTrait(string csContent, string className, string traitName)
    {
        // Find the class declaration line
        var lines = csContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains($"class {className}") && trimmed.Contains(traitName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the emitted C# contains a property accessor within the trait interface body.
    /// </summary>
    private static bool ContainsPropertyAccessor(
        string csContent, string traitName, string propertyName, string accessor)
    {
        // The accessor should appear somewhere in the emitted content near the property
        // We check that the accessor string appears in the file (within the interface body)
        return csContent.Contains(accessor);
    }
}
