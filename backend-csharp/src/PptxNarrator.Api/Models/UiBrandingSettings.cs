namespace PptxNarrator.Api.Models;

/// <summary>
/// System-wide UI branding settings persisted to disk (ui-branding.json).
/// Returned via GET /api/config and mutable via POST /api/admin/settings.
/// </summary>
public class UiBrandingSettings
{
    public string AppName { get; set; } = "GAO Text to Speech";
    public string LogoUrl { get; set; } = "";
    public string PrimaryColor { get; set; } = "#004d2f";
    public string PrimaryColorDark { get; set; } = "#003320";
    public string PrimaryColorLight { get; set; } = "#e6f4ee";
    public string AccentColor { get; set; } = "#007a4d";
    public List<string> EnabledVoices { get; set; } = [];
}
