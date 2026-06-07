namespace Playwright.NSE.Indices.Downloading;

public static class DateChunker
{
    /// <summary>
    /// Splits the date range from startDate to endDate into chunks
    /// of chunkMonths months each.
    /// </summary>
    public static List<(DateTime From, DateTime To)> Build(
        DateTime startDate,
        DateTime endDate,
        int chunkMonths)
    {
        var chunks = new List<(DateTime From, DateTime To)>();
        var cursor = startDate;

        while (cursor <= endDate)
        {
            var chunkEnd = cursor.AddMonths(chunkMonths).AddDays(-1);
            if (chunkEnd > endDate) chunkEnd = endDate;
            chunks.Add((cursor, chunkEnd));
            cursor = chunkEnd.AddDays(1);
        }

        return chunks;
    }
}
