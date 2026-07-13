namespace Gua.Testing
{
    internal static class Guard
    {
        internal static void NotNull(object? value, string parameterName)
        {
            if (value is null) throw new ArgumentNullException(parameterName);
        }

        internal static void NotNullOrWhiteSpace(string? value, string parameterName)
        {
            if (value is null) throw new ArgumentNullException(parameterName);
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        }
    }
}

#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }
}
#endif
