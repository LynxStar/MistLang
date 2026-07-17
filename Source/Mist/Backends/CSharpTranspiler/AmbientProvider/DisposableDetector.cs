using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Detects IDisposable implementation on projected types by checking their
/// resolved interface list and sets the drop flags on <see cref="ConcreteType"/>.
/// </summary>
internal static class DisposableDetector
{
    private const string DisposableFQN = "System.IDisposable";

    /// <summary>
    /// Checks whether the projected type implements IDisposable (via its resolved
    /// base abstract types) and sets <see cref="ConcreteType.HasDrop"/> and
    /// <see cref="ConcreteType.IsDisposableDrop"/> accordingly.
    /// </summary>
    public static void Apply(ConcreteType concreteType)
    {
        if(!ImplementsIDisposable(concreteType))
        {
            return;
        }

        concreteType.HasDrop = true;
        concreteType.IsDisposableDrop = true;
    }

    /// <summary>
    /// Determines whether the projected type implements System.IDisposable
    /// by checking the resolved base abstract types (interfaces).
    /// </summary>
    private static bool ImplementsIDisposable(ConcreteType concreteType)
    {
        foreach(var baseAbstract in concreteType.BaseAbstractTypes)
        {
            if(baseAbstract.FullyQualifiedName == DisposableFQN)
            {
                return true;
            }
        }

        return false;
    }
}