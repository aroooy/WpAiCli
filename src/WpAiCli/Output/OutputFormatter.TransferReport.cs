using System;
using System.IO;
using WpAiCli.Services;

namespace WpAiCli.Output;

public static partial class OutputFormatter
{
    public static void WriteTransferReport(TransferReport report, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("--- Transfer Report ---");
        writer.WriteLine($"Posts pulled (remote -> cache): {report.PulledFromServer.Count}");
        if (report.PulledFromServer.Count > 0)
        {
            writer.WriteLine($"  IDs: {string.Join(", ", report.PulledFromServer)}");
        }

        writer.WriteLine($"Posts newly cached:             {report.NewlyCached.Count}");
        if (report.NewlyCached.Count > 0)
        {
            writer.WriteLine($"  IDs: {string.Join(", ", report.NewlyCached)}");
        }

        if (report.LocalEditsKept.Count > 0)
        {
            writer.WriteLine($"Local edits preserved:          {report.LocalEditsKept.Count} post(s)");
            writer.WriteLine($"  IDs: {string.Join(", ", report.LocalEditsKept)}");
        }

        writer.WriteLine($"Posts pushed (cache -> remote): {report.PushedToServer.Count}");
        if (report.PushedToServer.Count > 0)
        {
            writer.WriteLine($"  IDs: {string.Join(", ", report.PushedToServer)}");
        }

        writer.WriteLine($"Posts deleted from cache:       {report.DeletedFromLocal.Count}");
        if (report.DeletedFromLocal.Count > 0)
        {
            writer.WriteLine($"  IDs: {string.Join(", ", report.DeletedFromLocal)}");
        }

        writer.WriteLine($"Taxonomies pulled:              {report.PulledTaxonomies.Count}");
        if (report.PulledTaxonomies.Count > 0)
        {
            writer.WriteLine($"  Names: {string.Join(", ", report.PulledTaxonomies)}");
        }

        writer.WriteLine($"Taxonomies pushed:              {report.PushedTaxonomies.Count}");
        if (report.PushedTaxonomies.Count > 0)
        {
            writer.WriteLine($"  Names: {string.Join(", ", report.PushedTaxonomies)}");
        }

        // Media Transfer Section
        if (report.PushedMediaToServer.Count > 0 ||
            report.PulledMediaFromServer.Count > 0 ||
            report.NewlyCachedMedia.Count > 0 ||
            report.DeletedMediaFromLocal.Count > 0 ||
            report.MediaConflicts.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("--- Media ---");
            writer.WriteLine($"Pushed metadata to server: {report.PushedMediaToServer.Count} item(s)");
            writer.WriteLine($"Pulled from server:        {report.PulledMediaFromServer.Count} item(s)");
            writer.WriteLine($"Newly cached from server:  {report.NewlyCachedMedia.Count} item(s)");
            writer.WriteLine($"Deleted from local:        {report.DeletedMediaFromLocal.Count} item(s)");
            writer.WriteLine($"Conflicts/Errors:          {report.MediaConflicts.Count} item(s)");
        }

        if (report.LocalValidationErrors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"[WARNING] Local validation errors ({report.LocalValidationErrors.Count}):");
            foreach (var (postId, error) in report.LocalValidationErrors)
            {
                writer.WriteLine($"  Post {postId}: {error}");
            }
        }

        if (report.ConflictDetected.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"[ATTENTION] Conflicts detected in {report.ConflictDetected.Count} post(s):");
            writer.WriteLine($"  IDs: {string.Join(", ", report.ConflictDetected)}");
            writer.WriteLine("Resolve using `wpai resolve post <id> --strategy [local-wins|server-wins]`");
            writer.WriteLine("Example: wpai resolve post 123 --strategy [local-wins|server-wins]");
        }

        if (report.MovedPosts.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Automatically reorganized post files based on status change:");
            foreach (var moved in report.MovedPosts)
            {
                writer.WriteLine($"  {moved}");
            }
        }

        writer.WriteLine("-------------------");
    }

    public static string FormatTransferReport(TransferReport report)
    {
        using var writer = new StringWriter();
        WriteTransferReport(report, writer);
        return writer.ToString();
    }
}
