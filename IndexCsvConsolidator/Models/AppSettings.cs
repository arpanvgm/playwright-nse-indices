namespace IndexCsvConsolidator.Models;

public class AppSettings
{
    public string InputFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string ArchiveFolder { get; set; } = string.Empty;
    public bool OverwriteExistingValue { get; set; } = false;
    public bool AllowCreateMasterIfNotExists { get; set; } = false;
}
