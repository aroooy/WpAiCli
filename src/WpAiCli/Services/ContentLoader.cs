namespace WpAiCli.Services;

public static class ContentLoader
{
    public static string? ReadContent(string? inlineContent, FileInfo? contentFile)
    {
        if (contentFile is not null)
        {
            if (!contentFile.Exists)
            {
                throw new FileNotFoundException($"Content file not found: {contentFile.FullName}");
            }

            return File.ReadAllText(contentFile.FullName);
        }

        return inlineContent;
    }
}
