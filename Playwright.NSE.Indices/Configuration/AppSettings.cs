namespace Playwright.NSE.Indices.Configuration;

public class AppSettings
{
    /// <summary>Root folder where downloaded CSVs are saved.</summary>
    public string DownloadsPath { get; set; } = @"D:\MarketData\NseDownloads";

    /// <summary>Download history starts on 1st January of this year.</summary>
    public int StartYear { get; set; } = 1996;

    /// <summary>
    /// Last date to download (dd-MM-yyyy).
    /// Leave blank or omit to use today's date at runtime.
    /// </summary>
    public string EndDate { get; set; } = string.Empty;

    /// <summary>Number of months per download chunk. 12 = one year per request.</summary>
    public int ChunkMonths { get; set; } = 12;

    /// <summary>Polite pause between download requests, in milliseconds.</summary>
    public int DelayMs { get; set; } = 4000;
}
