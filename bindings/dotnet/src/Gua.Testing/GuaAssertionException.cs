namespace Gua.Testing;

public sealed class GuaAssertionException : Exception
{
    public GuaAssertionException(string message)
        : base(message)
    {
    }
}
