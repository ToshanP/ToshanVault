using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Services;

/// <summary>
/// Fetches the live gold spot price from public no-auth APIs and caches the
/// AUD-per-gram-24K result in the <c>gold_price_cache</c> table. Sources:
/// <list type="bullet">
///   <item><c>api.gold-api.com/price/XAU</c> — USD per troy ounce, no key.</item>
///   <item><c>api.frankfurter.app/latest?from=USD&amp;to=AUD</c> — FX rate, no key.</item>
/// </list>
/// Conversion: <c>(USD/oz) ÷ 31.1034768 g/oz × AUD/USD = AUD/g (24K)</c>.
/// Default cache TTL is 1 hour to avoid hammering the free endpoints.
/// </summary>
public sealed class GoldPriceService
{
    public const string CurrencyAud = "AUD";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    private static readonly Uri GoldUri = new("https://api.gold-api.com/price/XAU");
    private static readonly Uri FxUri   = new("https://api.frankfurter.app/latest?from=USD&to=AUD");

    private readonly GoldPriceCacheRepository _cache;
    private readonly HttpClient _http;

    public GoldPriceService(GoldPriceCacheRepository cache, HttpClient? http = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _http  = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// Returns the cached price if fresh; otherwise fetches a new one and
    /// upserts the cache. Returns <c>null</c> if both the cache is empty and
    /// the network call fails (so the UI can render "—" gracefully).
    /// </summary>
    public async Task<GoldPriceCache?> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync(CurrencyAud, ct).ConfigureAwait(false);
        if (!forceRefresh && cached is not null
            && DateTimeOffset.UtcNow - cached.FetchedAt < DefaultTtl)
        {
            return cached;
        }

        try
        {
            var fresh = await FetchAsync(ct).ConfigureAwait(false);
            await _cache.UpsertAsync(fresh, ct).ConfigureAwait(false);
            return fresh;
        }
        catch
        {
            // Network failure — fall back to whatever we had cached (if any).
            return cached;
        }
    }

    private async Task<GoldPriceCache> FetchAsync(CancellationToken ct)
    {
        // 1. USD per troy ounce of gold (XAU).
        using var goldResp = await _http.GetAsync(GoldUri, ct).ConfigureAwait(false);
        goldResp.EnsureSuccessStatusCode();
        var goldJson = await goldResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var gold = JsonSerializer.Deserialize<GoldApiResponse>(goldJson)
            ?? throw new InvalidOperationException("Empty response from gold price API.");
        if (gold.Price <= 0)
            throw new InvalidOperationException($"Invalid gold price: {gold.Price}");

        // 2. AUD per USD.
        using var fxResp = await _http.GetAsync(FxUri, ct).ConfigureAwait(false);
        fxResp.EnsureSuccessStatusCode();
        var fxJson = await fxResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var fx = JsonSerializer.Deserialize<FxApiResponse>(fxJson)
            ?? throw new InvalidOperationException("Empty FX response.");
        if (fx.Rates is null || !fx.Rates.TryGetValue("AUD", out var audPerUsd) || audPerUsd <= 0)
            throw new InvalidOperationException("FX response missing AUD rate.");

        var audPerGram24K = (gold.Price / GoldValueCalculator.GramsPerTroyOunce) * audPerUsd;

        return new GoldPriceCache
        {
            Currency        = CurrencyAud,
            PricePerGram24k = audPerGram24K,
            FetchedAt       = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Estimates the AUD value of <paramref name="grams"/> of gold at the
    /// given <paramref name="purity"/> (e.g. "22K", "18K", "24K", "Diamond").
    /// Delegates to <see cref="GoldValueCalculator"/> — preserved here for
    /// call-site convenience.
    /// </summary>
    public static double EstimateValue(double grams, string? purity, double pricePerGram24k)
        => GoldValueCalculator.EstimateValue(grams, purity, pricePerGram24k);

    /// <inheritdoc cref="GoldValueCalculator.PurityFraction(string)" />
    public static double PurityFraction(string? purity)
        => GoldValueCalculator.PurityFraction(purity);

    private sealed class GoldApiResponse
    {
        [JsonPropertyName("price")]  public double Price  { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    }

    private sealed class FxApiResponse
    {
        [JsonPropertyName("rates")] public Dictionary<string, double>? Rates { get; set; }
    }
}
