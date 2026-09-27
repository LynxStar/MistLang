using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class AssignabilityTypes
{
    private readonly Dictionary<string, IMistType> types = new(StringComparer.Ordinal);

    public AssignabilityTypes()
    {
        foreach(var name in new[] { "Int32", "Int64", "Int16", "UInt32", "UInt64", "UInt16", "Byte", "SByte", "Single", "Double", "Decimal", "Char", "String", "Boolean", "Void", "Regex" })
        {
            types.Add(name, Model.Type(name));
        }

        types.Add("unknown", UnknownType.Instance);
        types.Add("error", ErrorType.Instance);
        types.Add("Int32?", Model.Interner.GetOrCreateUnionType([this["Int32"], this["Void"]]));
        types.Add("Byte?", Model.Interner.GetOrCreateUnionType([this["Byte"], this["Void"]]));
        types.Add("SByte?", Model.Interner.GetOrCreateUnionType([this["SByte"], this["Void"]]));
        types.Add("UInt64?", Model.Interner.GetOrCreateUnionType([this["UInt64"], this["Void"]]));
        types.Add("ByteOrString", Model.Interner.GetOrCreateUnionType([this["Byte"], this["String"]]));
        types.Add("IntOrString", Model.Interner.GetOrCreateUnionType([this["Int32"], this["String"]]));
        types.Add("StringOrBool", Model.Interner.GetOrCreateUnionType([this["String"], this["Boolean"]]));
        types.Add("IntStringBool", Model.Interner.GetOrCreateUnionType([this["Int32"], this["String"], this["Boolean"]]));
        types.Add("SmallAdult", HandBuiltModel.Refined(this["Byte"], "SmallAdult"));
        types.Add("AdultAge", HandBuiltModel.Refined(this["Int32"], "AdultAge"));
        types.Add("DrinkingAge", HandBuiltModel.Refined(this["Int32"], "DrinkingAge"));
        types.Add("RefinedByte", HandBuiltModel.Refined(this["Byte"]));
        var declared = HandBuiltModel.Refined(HandBuiltModel.Class("K"), "Ne");
        types.Add("SubstitutedByte", new RefinedType(declared, this["Byte"]));
        types.Add("Ne<Int32>", new RefinedType(declared, this["Int32"]));
        types.Add("Ne<String>", new RefinedType(declared, this["String"]));
        types.Add("Ne<Int64>", new RefinedType(declared, this["Int64"]));
        types.Add("SmallOrName", Model.Interner.GetOrCreateUnionType([this["SmallAdult"], this["String"]]));
        types.Add("SmallAdult?", Model.Interner.GetOrCreateUnionType([this["SmallAdult"], this["Void"]]));
        types.Add("AdultAge?", Model.Interner.GetOrCreateUnionType([this["AdultAge"], this["Void"]]));

        var animal = HandBuiltModel.Class("Animal");
        types.Add("Animal", animal);
        types.Add("Dog", HandBuiltModel.Class("Dog", animal));
        var box = HandBuiltModel.Class("Box<>");
        var boxParameter = new TypeParameter("T", 0, box.Scope, box);
        box.TypeParametersList.Add(boxParameter);
        var abstraction = HandBuiltModel.Class("IBox<>");
        abstraction.TypeParametersList.Add(new TypeParameter("T", 0, abstraction.Scope, abstraction));
        box.BaseAbstractTypes.Add(Model.Interner.GetOrCreateConstructedType(abstraction, [boxParameter]));
        types.Add("Box<Int32>", Model.Interner.GetOrCreateConstructedType(box, [this["Int32"]]));
        types.Add("Box<String>", Model.Interner.GetOrCreateConstructedType(box, [this["String"]]));
        types.Add("IBox<Int32>", Model.Interner.GetOrCreateConstructedType(abstraction, [this["Int32"]]));
        types.Add("Color", HandBuiltModel.Enum("Color", this["Byte"]));
        types.Add("Priority", HandBuiltModel.Enum("Priority", this["Int32"]));
        types.Add("Flag", HandBuiltModel.Enum("Flag", this["Int32"]));
        types.Add("Priority?", Model.Interner.GetOrCreateUnionType([this["Priority"], this["Void"]]));

        var tally = HandBuiltModel.Class("Tally");
        _ = HandBuiltModel.Conversion(tally, tally, this["Int32"]);
        _ = HandBuiltModel.Conversion(tally, tally, this["Int64"]);
        types.Add("Tally", tally);
        var gauge = HandBuiltModel.Class("Gauge");
        _ = HandBuiltModel.Conversion(gauge, this["UInt32"], gauge);
        _ = HandBuiltModel.Conversion(gauge, this["Int16"], gauge);
        types.Add("Gauge", gauge);
        var counter = HandBuiltModel.Class("Counter");
        _ = HandBuiltModel.Conversion(counter, counter, this["Int32"]);
        types.Add("Counter", counter);
        var level = HandBuiltModel.Class("Level");
        _ = HandBuiltModel.Conversion(level, this["Int32"], level);
        types.Add("Level", level);

        var reading = HandBuiltModel.Class("Reading");
        var meters = HandBuiltModel.Class("MetersClass");
        var feet = HandBuiltModel.Class("FeetClass");
        _ = HandBuiltModel.Conversion(reading, reading, meters);
        _ = HandBuiltModel.Conversion(feet, meters, feet);
        types.Add("Reading", reading);
        types.Add("FeetClass", feet);
        types.Add("Meters", Clone("Meters", (ConcreteType)this["Double"]));
        types.Add("Feet", Clone("Feet", (ConcreteType)this["Double"]));
        var point = HandBuiltModel.Struct("Point");
        types.Add("Point", point);
        types.Add("Position", Clone("Position", point));
        _ = HandBuiltModel.Conversion((ConcreteType)this["Regex"], this["String"], this["Regex"], isImplicit: false, isIntrinsic: true);

        var tuple = Model.Interner.GetOrCreateTupleType([this["Int32"], this["Int32"]]);
        types.Add("Tuple", tuple);
        types.Add("NamedTuple", new NamedTupleType(tuple, ["px", "py"]));
        types.Add("RenamedTuple", new NamedTupleType(tuple, ["qa", "qb"]));
        types.Add("Function", Model.Interner.GetOrCreateFunctionType([this["Int32"]], this["Int32"]));
        var array = Model.Type("Array<,>");
        var unknownLength = Model.Interner.GetOrCreateUnknownValue(this["Int32"]);
        types.Add("Array", Model.Interner.GetOrCreateConstructedType(array, [this["Int32"], unknownLength]));
        types.Add("SizedArray", Model.Interner.GetOrCreateConstructedType(array, [this["Int32"], Model.Interner.LiftLiteral(this["Int32"], 3)]));
        types.Add("Task<Int32>", Model.Interner.GetOrCreateConstructedType(Model.Type("Task<>"), [this["Int32"]]));
        types.Add("Anonymous", Model.Interner.GetOrCreateAnonymousType(["A"], [this["Int32"]]));
    }

    public HandBuiltModel Model { get; } = new();

    public IMistType this[string name] => types[name];

    private static StructType Clone(string name, ConcreteType source)
    {
        var clone = HandBuiltModel.Struct(name);
        clone.CloneSource = source;
        clone.CloneSourceType = source;
        clone.CastOperatorsList.Add(new CastOperator(source, new Parameter("value", clone), true, default) { CopiesClone = true });
        clone.CastOperatorsList.Add(new CastOperator(clone, new Parameter("value", source), false, default) { CopiesClone = true });
        return clone;
    }
}