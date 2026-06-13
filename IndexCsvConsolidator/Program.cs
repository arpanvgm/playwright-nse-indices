using IndexCsvConsolidator.Models;
using IndexCsvConsolidator.Services;
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

AppSettings settings = configuration.GetSection("Settings").Get<AppSettings>()
    ?? throw new InvalidOperationException(
        "appsettings.json must contain a 'Settings' section. See README for format.");

if (!Directory.Exists(settings.InputFolder))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] Input folder not found: {settings.InputFolder}");
    Console.WriteLine("        Update InputFolder in appsettings.json and retry.");
    Console.ResetColor();
    Environment.Exit(1);
}

Directory.CreateDirectory(settings.OutputFolder);
Directory.CreateDirectory(settings.ArchiveFolder);

string logFolder = Path.Combine(AppContext.BaseDirectory, "Logs");

using LogService log = new(logFolder);

log.Info($"Input              : {settings.InputFolder}");
log.Info($"Output             : {settings.OutputFolder}");
log.Info($"Archive            : {settings.ArchiveFolder}");
log.Info($"OverwriteExisting  : {settings.OverwriteExistingValue}");
log.Info($"AllowCreateMasterIfNotExists: {settings.AllowCreateMasterIfNotExists}");
log.Info(string.Empty);

FileProcessorService processor = new(settings, log);
processor.ProcessAll();
