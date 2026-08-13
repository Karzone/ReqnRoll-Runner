using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill so C# 9 <c>init</c> accessors and records compile against netstandard2.0.
    /// The compiler only needs the type to exist; it is never referenced at runtime.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
