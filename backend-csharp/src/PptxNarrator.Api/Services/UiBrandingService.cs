using System.Text.Json;
using PptxNarrator.Api.Models;
using PptxNarrator.Api.Services.Interfaces;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Persists system-wide UI branding settings to a JSON file on disk.
/// Changes made via POST /api/admin/settings are visible to all users immediately.
/// </summary>
public class UiBrandingService : IUiBrandingService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private UiBrandingSettings _current;

    public UiBrandingService(string filePath)
    {
        _filePath = filePath;
        _current = Load(filePath);
    }

    public UiBrandingSettings Get() => _current;

    public async Task SaveAsync(UiBrandingSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _current = settings;
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static UiBrandingSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<UiBrandingSettings>(json) ?? new();
            }
        }
        catch { /* fall back to defaults on any parse error */ }
        return new();
    }
}
