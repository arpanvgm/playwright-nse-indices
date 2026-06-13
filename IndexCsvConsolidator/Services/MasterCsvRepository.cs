using CsvHelper;
using CsvHelper.Configuration;
using IndexCsvConsolidator.Models;
using System.Globalization;
using System.Text;

namespace IndexCsvConsolidator.Services;

public class MasterCsvRepository
{
    public List<MasterRecord> Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<MasterRecord>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        using var reader = new StreamReader(filePath,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MasterRecordMap>();

        return csv.GetRecords<MasterRecord>().ToList();
    }

    public void Save(string filePath, List<MasterRecord> records)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            ShouldQuote = _ => true,
        };

        using var writer = new StreamWriter(filePath, append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, config);
        csv.Context.RegisterClassMap<MasterRecordMap>();
        csv.WriteRecords(records);
    }
}

public sealed class MasterRecordMap : ClassMap<MasterRecord>
{
    public MasterRecordMap()
    {
        Map(m => m.IndexName).Index(0).Name("IndexName");
        Map(m => m.Date).Index(1).Name("Date");
        Map(m => m.Open).Index(2).Name("Open");
        Map(m => m.High).Index(3).Name("High");
        Map(m => m.Low).Index(4).Name("Low");
        Map(m => m.Close).Index(5).Name("Close");
        Map(m => m.SharesTraded).Index(6).Name("SharesTraded");
        Map(m => m.TurnoverInrCr).Index(7).Name("TurnoverInrCr");
        Map(m => m.PE).Index(8).Name("PE");
        Map(m => m.PB).Index(9).Name("PB");
        Map(m => m.DividendYield).Index(10).Name("DividendYield");
    }
}
