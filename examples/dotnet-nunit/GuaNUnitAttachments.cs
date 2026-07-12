using Gua.Testing;
using NUnit.Framework;

internal static class GuaNUnitAttachments
{
    public static void Add(GuaDiagnosticFile file) =>
        TestContext.AddTestAttachment(file.Path, $"Gua diagnostic ({file.MediaType})");
}
