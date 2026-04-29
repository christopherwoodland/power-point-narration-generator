using PptxNarrator.Api.Models;

namespace PptxNarrator.Api.Services.Interfaces;

public interface IUiBrandingService
{
    UiBrandingSettings Get();
    Task SaveAsync(UiBrandingSettings settings);
}
