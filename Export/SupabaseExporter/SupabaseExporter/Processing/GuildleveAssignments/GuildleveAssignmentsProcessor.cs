using Lumina.Extensions;
using Microsoft.EntityFrameworkCore;

namespace SupabaseExporter.Processing.GuildleveAssignments;

public class GuildleveAssignmentsProcessor : IDisposable
{
    private readonly Dictionary<uint, (uint ENpcBaseId, uint LevelId)> ENpcCache = [];

    public void Dispose()
    {
        ENpcCache.Clear();
        GC.Collect();
    }

    public async Task ProcessAllData(DatabaseContext context)
    {
        Logger.Information("Processing leve data");

        await ExportHandler.WriteDataJson("LeveIssuers.json", async writer =>
        {
            uint? currentIssuerId = null;
            byte? currentCategoryId = null;
            byte? currentTypeIndex = null;

            writer.WriteStartObject();

            var stream = context.GuildleveAssignments
                .OrderBy(m => m.RowId)
                .ThenBy(m => m.CategoryRowId)
                .ThenBy(m => m.CategoryIndex)
                .AsNoTracking()
                .AsAsyncEnumerable();

            void WriteEnd(int depth)
            {
                if (currentTypeIndex != null)
                {
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                if (depth > 1 && currentCategoryId != null)
                {
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                if (depth > 2 && currentIssuerId != null)
                {
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
            }

            await foreach (var row in stream)
            {
                if (currentIssuerId != row.RowId)
                {
                    WriteEnd(3);

                    currentIssuerId = row.RowId;
                    currentCategoryId = null;
                    currentTypeIndex = null;

                    writer.WritePropertyName(row.RowId.ToString());
                    writer.WriteStartObject();

                    writer.WriteNumber("GuildleveAssignmentId"u8, row.RowId);

                    if (TryFindENpcByDataId(row.RowId, out var enpcBaseId, out var levelId))
                    {
                        writer.WriteNumber("ENpcBaseId"u8, enpcBaseId);
                        writer.WriteNumber("LevelId"u8, levelId);
                    }

                    writer.WriteStartObject("Categories"u8);
                }

                if (currentCategoryId != row.CategoryRowId)
                {
                    WriteEnd(2);

                    currentCategoryId = row.CategoryRowId;
                    currentTypeIndex = null;

                    writer.WritePropertyName(row.CategoryRowId.ToString());
                    writer.WriteStartObject();

                    writer.WriteNumber("CategoryId"u8, row.CategoryRowId);

                    writer.WriteStartObject("Types"u8);
                }

                if (currentTypeIndex != row.CategoryIndex)
                {
                    WriteEnd(1);

                    currentTypeIndex = row.CategoryIndex;

                    writer.WritePropertyName(row.CategoryIndex.ToString());
                    writer.WriteStartObject();

                    writer.WriteNumber("CategoryIndex"u8, row.CategoryIndex);

                    writer.WriteStartArray("LeveIds"u8);
                }

                foreach (var leveId in row.LeveIds)
                {
                    writer.WriteNumberValue(leveId);
                }
            }

            WriteEnd(3);

            writer.WriteEndObject();

            await writer.FlushAsync();
        });

        Logger.Information("Done exporting data ...");
    }

    private bool TryFindENpcByDataId(uint dataId, out uint enpcId, out uint levelId)
    {
        enpcId = 0;
        levelId = 0;

        if (ENpcCache.TryGetValue(dataId, out var tuple))
        {
            (enpcId, levelId) = tuple;
            return true;
        }

        if (Sheets.ENpcBaseSheet.TryGetFirst(row => row.ENpcData.Any(rowRef => rowRef.RowId == dataId), out var enpcBaseRow) &&
            Sheets.LevelSheet.TryGetFirst(row => row.Type == 8 && row.Object.RowId == enpcBaseRow.RowId, out var levelRow))
        {
            ENpcCache.Add(dataId, (enpcId, levelId) = (enpcBaseRow.RowId, levelRow.RowId));
            return true;
        }

        return false;
    }
}
