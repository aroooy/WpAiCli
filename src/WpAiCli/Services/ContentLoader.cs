namespace WpAiCli.Services;

public static class ContentLoader
{
    public static string? ReadContent(string? inlineContent, FileInfo? contentFile)
    {
        if (contentFile is not null)
        {
            if (!contentFile.Exists)
            {
                throw new FileNotFoundException($"コンテンツ ファイルが見つかりません: {contentFile.FullName}");
            }

            return File.ReadAllText(contentFile.FullName);
        }

        return inlineContent;
    }
}
