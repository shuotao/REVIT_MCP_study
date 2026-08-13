using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// set_project_units — 一次把整個專案的顯示單位切到指定系統/模式。
    /// mode=taiwan：公制底 + Air Flow=m³/h（對齊建築技術規則 §102 的通風量單位）。
    /// 之後可用 length/area/volume/airFlow 個別覆寫。全案性動作，包在單一 Transaction，可 Ctrl+Z。
    /// </summary>
    public partial class CommandExecutor
    {
        #region set_project_units

        // 友善字串 → Revit 單位 ForgeTypeId 的對照表（大小寫不敏感）
        private static readonly Dictionary<string, ForgeTypeId> _lengthUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "m", UnitTypeId.Meters }, { "meter", UnitTypeId.Meters }, { "meters", UnitTypeId.Meters },
            { "mm", UnitTypeId.Millimeters }, { "millimeter", UnitTypeId.Millimeters },
            { "cm", UnitTypeId.Centimeters },
            { "ft", UnitTypeId.Feet }, { "feet", UnitTypeId.Feet },
            { "ft-in", UnitTypeId.FeetFractionalInches }, { "feet-inches", UnitTypeId.FeetFractionalInches },
        };

        private static readonly Dictionary<string, ForgeTypeId> _areaUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "m2", UnitTypeId.SquareMeters }, { "sqm", UnitTypeId.SquareMeters }, { "m^2", UnitTypeId.SquareMeters },
            { "sf", UnitTypeId.SquareFeet }, { "ft2", UnitTypeId.SquareFeet }, { "sqft", UnitTypeId.SquareFeet },
        };

        private static readonly Dictionary<string, ForgeTypeId> _volumeUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "m3", UnitTypeId.CubicMeters }, { "cbm", UnitTypeId.CubicMeters }, { "m^3", UnitTypeId.CubicMeters },
            { "l", UnitTypeId.Liters }, { "liter", UnitTypeId.Liters },
            { "cf", UnitTypeId.CubicFeet }, { "ft3", UnitTypeId.CubicFeet },
        };

        private static readonly Dictionary<string, ForgeTypeId> _airFlowUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "m3/h", UnitTypeId.CubicMetersPerHour }, { "m3h", UnitTypeId.CubicMetersPerHour }, { "cmh", UnitTypeId.CubicMetersPerHour },
            { "l/s", UnitTypeId.LitersPerSecond }, { "lps", UnitTypeId.LitersPerSecond },
            { "cfm", UnitTypeId.CubicFeetPerMinute },
        };

        private object SetProjectUnits(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string mode = parameters["mode"]?.Value<string>()?.Trim().ToLowerInvariant();
            string system = parameters["system"]?.Value<string>()?.Trim().ToLowerInvariant();

            // 1) 決定基底系統
            UnitSystem baseSystem = UnitSystem.Metric;
            if (mode == "imperial" || system == "imperial")
                baseSystem = UnitSystem.Imperial;

            Units units = new Units(baseSystem); // 一口氣帶入該系統的全部預設單位

            var applied = new List<object>();

            // 2) 模式預設覆寫（在個別覆寫之前）
            //    taiwan：公制底，Air Flow 改 m³/h（§102 通風量單位）
            if (mode == "taiwan")
            {
                units.SetFormatOptions(SpecTypeId.AirFlow, new FormatOptions(UnitTypeId.CubicMetersPerHour));
                applied.Add(new { spec = "airFlow", unit = "m3/h", from = "mode=taiwan" });
            }

            // 3) 個別覆寫（優先權最高）
            ApplyUnitOverride(units, parameters, "length", SpecTypeId.Length, _lengthUnitMap, applied);
            ApplyUnitOverride(units, parameters, "area", SpecTypeId.Area, _areaUnitMap, applied);
            ApplyUnitOverride(units, parameters, "volume", SpecTypeId.Volume, _volumeUnitMap, applied);
            ApplyUnitOverride(units, parameters, "airFlow", SpecTypeId.AirFlow, _airFlowUnitMap, applied);

            // 4) 套用（全案性，單一 Transaction）
            using (Transaction t = new Transaction(doc, "Set Project Units"))
            {
                t.Start();
                doc.SetUnits(units);
                t.Commit();
            }

            return new
            {
                Success = true,
                Mode = mode ?? (system ?? "metric"),
                BaseSystem = baseSystem.ToString(),
                Applied = applied,
                Result = new
                {
                    Length = ReportUnit(units, SpecTypeId.Length),
                    Area = ReportUnit(units, SpecTypeId.Area),
                    Volume = ReportUnit(units, SpecTypeId.Volume),
                    AirFlow = ReportUnit(units, SpecTypeId.AirFlow),
                },
                Message = "已套用專案單位（可用 Ctrl+Z 還原）。"
            };
        }

        private static void ApplyUnitOverride(
            Units units, JObject parameters, string paramKey,
            ForgeTypeId spec, Dictionary<string, ForgeTypeId> map, List<object> applied)
        {
            string v = parameters[paramKey]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            if (!map.TryGetValue(v, out ForgeTypeId unitId))
                throw new Exception($"不支援的 {paramKey} 單位 '{v}'。可用值：{string.Join(", ", map.Keys)}");

            units.SetFormatOptions(spec, new FormatOptions(unitId));
            applied.Add(new { spec = paramKey, unit = v, from = "override" });
        }

        private static string ReportUnit(Units units, ForgeTypeId spec)
        {
            try { return units.GetFormatOptions(spec).GetUnitTypeId().TypeId; }
            catch { return null; }
        }

        #endregion
    }
}
