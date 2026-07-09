namespace Gua.Testing;

public sealed class GuaAssertionScope : IDisposable
{
    private readonly GuaAssertionOptions _previous;

    private GuaAssertionScope(GuaAssertionOptions options)
    {
        _previous = GuaAssertions.Options;
        GuaAssertions.Options = options;
    }

    public static GuaAssertionScope Use(GuaAssertionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new GuaAssertionScope(options);
    }

    public static GuaAssertionScope UseXUnit(Action<string> fail)
    {
        return UseNamedFramework("xUnit", fail);
    }

    public static GuaAssertionScope UseNUnit(Action<string> fail)
    {
        return UseNamedFramework("NUnit", fail);
    }

    public static GuaAssertionScope UseMSTest(Action<string> fail)
    {
        return UseNamedFramework("MSTest", fail);
    }

    public void Dispose()
    {
        GuaAssertions.Options = _previous;
    }

    private static GuaAssertionScope UseNamedFramework(string frameworkName, Action<string> fail)
    {
        ArgumentNullException.ThrowIfNull(fail);
        return Use(new GuaAssertionOptions
        {
            Fail = message => fail($"Gua assertion failed for {frameworkName}: {message}"),
        });
    }
}
