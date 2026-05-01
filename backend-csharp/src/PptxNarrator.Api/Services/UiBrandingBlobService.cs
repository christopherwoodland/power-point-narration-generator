using Azure.Core;
using Azure.Storage.Blobs;
using System.Text.Json;
using PptxNarrator.Api.Models;
using PptxNarrator.Api.Services.Interfaces;

namespace PptxNarrator.Api.Services;

/// <summary>
/// Persists system-wide UI branding settings to Azure Blob Storage,
/// authenticated via <see cref="TokenCredential"/> (Managed Identity in ACA,
/// AzureCliCredential locally). No storage account keys are used.
/// </summary>
public sealed class UiBrandingBlobService : IUiBrandingService
{
    private const string BlobName = "ui-branding.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private UiBrandingSettings _current;

    public UiBrandingBlobService(string storageAccountName, string containerName, TokenCredential credential)
    {
        var serviceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        _container = new BlobServiceClient(serviceUri, credential).GetBlobContainerClient(containerName);
        _current = LoadAsync().GetAwaiter().GetResult();
    }

    public UiBrandingSettings Get() => _current;

    public async Task SaveAsync(UiBrandingSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _current = settings;
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream(bytes);
            await _container.GetBlobClient(BlobName).UploadAsync(stream, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UiBrandingSettings> LoadAsync()
    {
        try
        {
            await _container.CreateIfNotExistsAsync();
            var blob = _container.GetBlobClient(BlobName);
            if (await blob.ExistsAsync())
            {
                var result = await blob.DownloadContentAsync();
                return JsonSerializer.Deserialize<UiBrandingSettings>(result.Value.Content.ToArray()) ?? new();
            }
        }
        catch { /* fall back to defaults on any error */ }
        return new();
    }
}
