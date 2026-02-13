using System;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Dto;

namespace KaizokuBackend.Extensions;

public static class ProviderSummaryExtensions
{
    public static SmallProviderDto ToSmallProviderDto(this ProviderSummaryBase provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new SmallProviderDto
        {
            Provider = provider.Provider,
            Scanlator = provider.Scanlator,
            Language = provider.Language,
            IsStorage = provider.IsStorage,
            Title = provider.Title,
            ThumbnailUrl = provider.ThumbnailUrl,
            Status = provider.Status,
            Url = provider.Url
        };
    }
}
