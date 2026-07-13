namespace Gua.Core
{
    internal static class Guard
    {
        internal static void NotNull(object? value, string parameterName)
        {
            if (value is null) throw new ArgumentNullException(parameterName);
        }

        internal static void NotZero(ulong value, string parameterName)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit
    {
    }
}
#endif
