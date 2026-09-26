namespace ContosoDashboard.Shared;

/// <summary>
/// Display helpers shared by the document pages.
/// </summary>
public static class DocumentFormatting
{
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    public static string GetCategoryColor(string category) => category switch
    {
        "Project Documents" => "primary",
        "Team Resources" => "info",
        "Personal Files" => "secondary",
        "Reports" => "success",
        "Presentations" => "warning",
        _ => "dark"
    };
}
