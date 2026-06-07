using System.Collections.Generic;
using System.Linq;
using System.Text;
using RevitMCP.Models;

namespace RevitMCP.Core.Services
{
    internal class ReportService
    {
        public string BuildTextReport(IEnumerable<DwgImportResult> results)
        {
            var rows = results?.ToList() ?? new List<DwgImportResult>();
            var builder = new StringBuilder();
            builder.AppendLine("DWG Grid Registration Import Report");
            builder.AppendLine($"Total: {rows.Count}, Success: {rows.Count(r => r.Success)}, Failed: {rows.Count(r => !r.Success)}");

            foreach (var result in rows)
            {
                builder.AppendLine($"[{(result.Success ? "OK" : "FAIL")}] Level={result.LevelName}, View={result.ViewName}, ElementId={result.ElementId}, Pinned={result.Pinned}, Message={result.Message}");
            }

            return builder.ToString();
        }
    }
}
