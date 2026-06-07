using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCP.Core.Services;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        private object ImportDwgToLevels(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            string? rawDwgPath = parameters["dwgPath"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(rawDwgPath))
            {
                throw new ArgumentException("請提供 dwgPath。");
            }

            string dwgPath = rawDwgPath ?? string.Empty;
            DwgImportSettings settings = ParseDwgImportSettings(parameters["settings"] as JObject);
            var levelNames = ReadStringArray(parameters["levelNames"]);
            var levelIds = ReadLongArray(parameters["levelIds"]);

            var levelViewService = new LevelViewService();
            var importService = new DwgImportService();
            var reportService = new ReportService();
            IList<Level> levels = levelViewService.ResolveLevels(doc, levelNames, levelIds);

            if (levels.Count == 0)
            {
                throw new InvalidOperationException("目前專案沒有可用 Level。");
            }

            var results = new List<DwgImportResult>();
            foreach (Level level in levels)
            {
                using (Transaction transaction = new Transaction(doc, "DWG Grid Registration - " + level.Name))
                {
                    try
                    {
                        transaction.Start();
                        ViewPlan view = levelViewService.GetOrCreateCadFloorPlan(doc, level);
                        DwgImportResult result = importService.Import(doc, dwgPath, level, view, settings);
                        transaction.Commit();
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }

                        results.Add(new DwgImportResult
                        {
                            DwgPath = dwgPath,
                            LevelName = level.Name,
                            LevelId = Convert.ToInt64(level.Id.GetIdValue()),
                            LoadMode = settings.LoadMode.ToString(),
                            PlacementMode = settings.PlacementMode.ToString(),
                            Success = false,
                            Message = ex.Message ?? string.Empty
                        });
                    }
                }
            }

            return new
            {
                Success = results.All(result => result.Success),
                DwgPath = dwgPath,
                ResultCount = results.Count,
                SuccessCount = results.Count(result => result.Success),
                FailedCount = results.Count(result => !result.Success),
                Settings = new
                {
                    LoadMode = settings.LoadMode.ToString(),
                    PlacementMode = settings.PlacementMode.ToString(),
                    settings.ThisViewOnly,
                    settings.PinAfterLoad,
                    settings.VisibleLayersOnly,
                    Unit = settings.Unit.ToString(),
                    ColorMode = settings.ColorMode.ToString(),
                    settings.ToleranceMm
                },
                Results = results,
                Report = reportService.BuildTextReport(results)
            };
        }

        private static DwgImportSettings ParseDwgImportSettings(JObject? json)
        {
            var settings = new DwgImportSettings();
            if (json == null)
            {
                return settings;
            }

            settings.ThisViewOnly = json["thisViewOnly"]?.Value<bool>() ?? settings.ThisViewOnly;
            settings.PinAfterLoad = json["pinAfterLoad"]?.Value<bool>() ?? settings.PinAfterLoad;
            settings.VisibleLayersOnly = json["visibleLayersOnly"]?.Value<bool>() ?? settings.VisibleLayersOnly;
            settings.ToleranceMm = json["toleranceMm"]?.Value<double>() ?? settings.ToleranceMm;

            string? loadMode = json["loadMode"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(loadMode) && Enum.TryParse(loadMode, true, out DwgLoadMode parsedLoadMode))
            {
                settings.LoadMode = parsedLoadMode;
            }

            string? placementMode = json["placementMode"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(placementMode) && Enum.TryParse(placementMode, true, out DwgPlacementMode parsedPlacementMode))
            {
                settings.PlacementMode = parsedPlacementMode;
            }

            string? unit = json["unit"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(unit) && Enum.TryParse(unit, true, out ImportUnit parsedUnit))
            {
                settings.Unit = parsedUnit;
            }

            string? colorMode = json["colorMode"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(colorMode) && Enum.TryParse(colorMode, true, out ImportColorMode parsedColorMode))
            {
                settings.ColorMode = parsedColorMode;
            }

            return settings;
        }

        private static IEnumerable<string> ReadStringArray(JToken? token)
        {
            if (token == null) return Enumerable.Empty<string>();
            if (token.Type == JTokenType.Array) return token.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToList();
            string? single = token.Value<string>();
            return string.IsNullOrWhiteSpace(single) ? Enumerable.Empty<string>() : new[] { single! };
        }

        private static IEnumerable<long> ReadLongArray(JToken? token)
        {
            if (token == null) return Enumerable.Empty<long>();
            if (token.Type == JTokenType.Array) return token.Values<long>().ToList();
            return new[] { token.Value<long>() };
        }
    }
}
