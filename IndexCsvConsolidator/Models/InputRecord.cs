namespace IndexCsvConsolidator.Models;

public class InputRecord
{
    public string IndexName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Open { get; set; } = string.Empty;
    public string High { get; set; } = string.Empty;
    public string Low { get; set; } = string.Empty;
    public string Close { get; set; } = string.Empty;
    public string SharesTraded { get; set; } = string.Empty;
    public string TurnoverInrCr { get; set; } = string.Empty;
    public string PE { get; set; } = string.Empty;
    public string PB { get; set; } = string.Empty;
    public string DividendYield { get; set; } = string.Empty;
}
