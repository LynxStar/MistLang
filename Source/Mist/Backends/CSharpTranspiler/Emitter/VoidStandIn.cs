namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Standard.Void's CLR shape, supplied by the backend rather than emitted from Void.mist. What keeps
/// it here is not the intrinsic rule -- Void.mist declares no intrinsic -- but that the C# form needs
/// members Mist cannot spell: the equality overrides, IEquatable, and the ToString that makes a void
/// value print as "Void" are overrides of a base object Mist does not have.
/// <para>
/// StandardDeclarationEmissionTests holds this text to every member Void.mist declares, so a member
/// added there cannot go unnoticed here.
/// </para>
/// </summary>
internal static class VoidStandIn
{
    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("#nullable enable");
        writer.WriteLine("namespace Standard;");
        writer.WriteBlankLine();
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("public readonly struct Void : System.IEquatable<Void>");

        using(writer.Block())
        {
            writer.WriteLine("public static readonly Void Instance = default;");
            writer.WriteBlankLine();
            writer.WriteLine("public bool Equals(Void other) => true;");
            writer.WriteBlankLine();
            writer.WriteLine("public override bool Equals(object? obj) => obj is Void;");
            writer.WriteBlankLine();
            writer.WriteLine("public override int GetHashCode() => 0;");
            writer.WriteBlankLine();
            writer.WriteLine("public static bool operator ==(Void left, Void right) => true;");
            writer.WriteBlankLine();
            writer.WriteLine("public static bool operator !=(Void left, Void right) => false;");
            writer.WriteBlankLine();
            writer.WriteLine("public override string ToString() => \"Void\";");
            writer.WriteBlankLine();
            writer.WriteLine("public static implicit operator System.Type(Void v) => typeof(Void);");
        }
    }
}