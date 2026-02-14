using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WpAiCli.Services;
// ※ CallToolResult や TextContent を探していた using は不要なので削除してOKです

// クラス自体をツールコンテナとしてマーク
[McpServerToolType]
public static class WordPressMcpTools
{
    // [McpServerTool]
    // 戻り値を Task<CallToolResult> ではなく Task<string> に変更します
    [McpServerTool]
    public static async Task<string> ListPosts(
        string? status,
        int? per_page,
        int? page,
        string? format,
        IServiceProvider services // DIコンテナから自動注入
    )
    {
        using var scope = services.CreateScope();
        var wpService = scope.ServiceProvider.GetRequiredService<WordPressService>();

        int actualPerPage = per_page.HasValue ? Math.Clamp(per_page.Value, 1, 100) : 10;
        int actualPage = page.HasValue ? Math.Max(page.Value, 1) : 1;

        var posts = await wpService.ListPostsAsync(status ?? "publish", actualPerPage, actualPage, CancellationToken.None);
        
        // 【修正点】CallToolResultを作らず、文字列を直接返します
        // SDKがこれを自動的に TextContent として処理します
        return JsonSerializer.Serialize(posts, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool]
    public static async Task<string> GetPost(
        int id,
        IServiceProvider services
    )
    {
        using var scope = services.CreateScope();
        var wpService = scope.ServiceProvider.GetRequiredService<WordPressService>();

        var post = await wpService.GetPostAsync(id, CancellationToken.None);

        // 【修正点】ここも文字列を直接返します
        return JsonSerializer.Serialize(post, new JsonSerializerOptions { WriteIndented = true });
    }
}