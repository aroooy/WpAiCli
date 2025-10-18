namespace WpAiCli.Help;

public static class HelpPrinter
{
    private static readonly string[] CandidateFiles =
    {
        "README.md",
        "readme.md",
        "HOWTO.md",
        "howto.md",
        "MANUAL.md",
        "manual.md"
    };

    public static bool TryPrintDocumentation(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var baseDirectory = AppContext.BaseDirectory;
        var printed = false;

        foreach (var fileName in CandidateFiles)
        {
            var fullPath = Path.Combine(baseDirectory, fileName);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            writer.WriteLine($"===== {fileName} =====");
            writer.WriteLine(File.ReadAllText(fullPath));
            writer.WriteLine();
            printed = true;
        }

        return printed;
    }
}
