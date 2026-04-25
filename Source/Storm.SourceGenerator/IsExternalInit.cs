// Polyfill to enable C# record types on netstandard2.0.
// The compiler requires this type to exist when using init-only setters.

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
