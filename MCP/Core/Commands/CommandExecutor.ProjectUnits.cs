using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// set_project_units — 一次把整個專案的顯示單位切到指定系統/模式。
    ///
    /// mode=taiwan          ：公制底 + Air Flow=m³/h（建築技術規則 §102 通風量單位）
    /// mode=taiwan-plumbing ：taiwan + 給排水管路側六格（建築設備編 §43/§46、給排水技術規範）
    ///
    /// 兩個 taiwan 模式都會把 Length 補上單位符號——Revit 公制預設「不」帶 Length 符號，
    /// 會讓標高與長度顯示成裸數字（673 / 690 / 35221），而管徑類卻有 "25 mm"，
    /// 同一張圖兩種寫法，對帳時分不出來。此為 2026-08-20 M1_04 實測發現。
    ///
    /// 全案性動作，包在單一 Transaction，可 Ctrl+Z。
    /// </summary>
    public partial class CommandExecutor
    {
        #region set_project_units

        // ── 友善字串 → Revit 單位 ForgeTypeId（大小寫不敏感）────────────────
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

        // ── 管路側（2026-08-20 新增，對應 M1_04 給排水實測手動設定的那幾格）──
        private static readonly Dictionary<string, ForgeTypeId> _pipeSizeUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "mm", UnitTypeId.Millimeters }, { "cm", UnitTypeId.Centimeters }, { "m", UnitTypeId.Meters },
            { "in", UnitTypeId.Inches }, { "inch", UnitTypeId.Inches },
        };

        private static readonly Dictionary<string, ForgeTypeId> _flowUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "l/min", UnitTypeId.LitersPerMinute }, { "lpm", UnitTypeId.LitersPerMinute },
            { "l/s", UnitTypeId.LitersPerSecond }, { "lps", UnitTypeId.LitersPerSecond },
            { "m3/h", UnitTypeId.CubicMetersPerHour },
            { "gpm", UnitTypeId.UsGallonsPerMinute },
        };

        private static readonly Dictionary<string, ForgeTypeId> _pipeVelocityUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "m/s", UnitTypeId.MetersPerSecond }, { "mps", UnitTypeId.MetersPerSecond },
            { "fps", UnitTypeId.FeetPerSecond },
        };

        // 台灣法規用 kgf/cm²，但 Revit 2026 的 305 個 UnitTypeId 裡「沒有」這個單位
        // （只有 KilogramsForcePerSquareMeter，差 10000 倍）。故以 mH2O 代用：
        // 1 kgf/cm² = 10.0 mH2O（整數換算）。設備編 §46「≥1.7」→ 讀作「≥17.0 mH2O」。
        private static readonly Dictionary<string, ForgeTypeId> _pipePressureUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "mh2o", UnitTypeId.MetersOfWaterColumn }, { "m-h2o", UnitTypeId.MetersOfWaterColumn }, { "mwc", UnitTypeId.MetersOfWaterColumn },
            { "mmh2o", UnitTypeId.MillimetersOfWaterColumn },
            { "kpa", UnitTypeId.Kilopascals }, { "pa", UnitTypeId.Pascals },
            { "bar", UnitTypeId.Bars },
            { "kgf/m2", UnitTypeId.KilogramsForcePerSquareMeter },
        };

        private static readonly Dictionary<string, ForgeTypeId> _pipeFrictionUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "mmh2o/m", UnitTypeId.MillimetersOfWaterColumnPerMeter }, { "mmwc/m", UnitTypeId.MillimetersOfWaterColumnPerMeter },
            { "mh2o/m", UnitTypeId.MetersOfWaterColumnPerMeter },
            { "pa/m", UnitTypeId.PascalsPerMeter },
        };

        // 台灣技術規範 3.2.2 寫「1/50」「1/100」→ 對應 OneToRatio（顯示 1 : n，變數在右）。
        // 反向的 RatioTo1 會顯示 0.01 : 1，數字對但寫法與法規相反。
        private static readonly Dictionary<string, ForgeTypeId> _pipeSlopeUnitMap = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            { "1:ratio", UnitTypeId.OneToRatio }, { "one-to-ratio", UnitTypeId.OneToRatio }, { "1:n", UnitTypeId.OneToRatio },
            { "ratio:1", UnitTypeId.RatioTo1 }, { "n:1", UnitTypeId.RatioTo1 },
            { "%", UnitTypeId.Percentage }, { "percent", UnitTypeId.Percentage },
            { "deg", UnitTypeId.SlopeDegrees }, { "degrees", UnitTypeId.SlopeDegrees },
        };

        private object SetProjectUnits(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string mode = parameters["mode"]?.Value<string>()?.Trim().ToLowerInvariant();
            string system = parameters["system"]?.Value<string>()?.Trim().ToLowerInvariant();

            bool isTaiwanPlumbing =
                mode == "taiwan-plumbing" || mode == "taiwan_plumbing" ||
                mode == "taiwanplumbing" || mode == "tw-plumbing";
            bool isTaiwan = mode == "taiwan" || isTaiwanPlumbing;

            // 1) 決定基底系統
            UnitSystem baseSystem = UnitSystem.Metric;
            if (mode == "imperial" || system == "imperial")
                baseSystem = UnitSystem.Imperial;

            Units units = new Units(baseSystem); // 一口氣帶入該系統的全部預設單位

            var applied = new List<object>();

            // 2) 模式預設覆寫（在個別覆寫之前）
            if (isTaiwan)
            {
                // §102 通風量單位
                ApplyFormat(units, SpecTypeId.AirFlow, "airFlow",
                    UnitTypeId.CubicMetersPerHour, null, 0.1, applied, "mode=" + mode);

                // Length 補單位符號 — 修正公制預設的裸數字（見類別註解）
                ApplyFormat(units, SpecTypeId.Length, "length",
                    UnitTypeId.Millimeters, SymbolTypeId.Mm, 1.0, applied, "mode=" + mode);
            }

            if (isTaiwanPlumbing)
            {
                // 管徑：設備編 §43／§46（條文以公厘為主，並列英吋）
                ApplyFormat(units, SpecTypeId.PipeSize, "pipeSize",
                    UnitTypeId.Millimeters, SymbolTypeId.Mm, 1.0, applied, "mode=" + mode);

                // 流量：給排水技術規範 3.4 節水器具以 L/min 表示
                ApplyFormat(units, SpecTypeId.Flow, "flow",
                    UnitTypeId.LitersPerMinute, SymbolTypeId.LPerMin, 0.1, applied, "mode=" + mode);

                // 流速：業界慣用 m/s。精度 0.01——0.1 位會讓 2.98 與 3.05 看起來相同，
                // 2026-08-20 實測就是這樣產生一次假綠燈的。
                ApplyFormat(units, SpecTypeId.PipingVelocity, "velocity",
                    UnitTypeId.MetersPerSecond, SymbolTypeId.MPerS, 0.01, applied, "mode=" + mode);

                // 壓力：§46 的 kgf/cm² 在 Revit 不存在 → mH2O 代用（1 kgf/cm² = 10.0 mH2O）
                ApplyFormat(units, SpecTypeId.PipingPressure, "pressure",
                    UnitTypeId.MetersOfWaterColumn, SymbolTypeId.MH2O, 0.01, applied, "mode=" + mode);

                // 摩擦損失：與壓力同族
                ApplyFormat(units, SpecTypeId.PipingFriction, "friction",
                    UnitTypeId.MillimetersOfWaterColumnPerMeter, SymbolTypeId.MmH2OPerM, 0.1, applied, "mode=" + mode);

                // 坡度：技術規範 3.2.2 的 1/50、1/100 寫法
                ApplyFormat(units, SpecTypeId.PipingSlope, "slope",
                    UnitTypeId.OneToRatio, SymbolTypeId.OneColon, 0.01, applied, "mode=" + mode);
            }

            // 3) 個別覆寫（優先權最高）
            ApplyUnitOverride(units, parameters, "length", SpecTypeId.Length, _lengthUnitMap, applied);
            ApplyUnitOverride(units, parameters, "area", SpecTypeId.Area, _areaUnitMap, applied);
            ApplyUnitOverride(units, parameters, "volume", SpecTypeId.Volume, _volumeUnitMap, applied);
            ApplyUnitOverride(units, parameters, "airFlow", SpecTypeId.AirFlow, _airFlowUnitMap, applied);
            ApplyUnitOverride(units, parameters, "pipeSize", SpecTypeId.PipeSize, _pipeSizeUnitMap, applied);
            ApplyUnitOverride(units, parameters, "flow", SpecTypeId.Flow, _flowUnitMap, applied);
            ApplyUnitOverride(units, parameters, "velocity", SpecTypeId.PipingVelocity, _pipeVelocityUnitMap, applied);
            ApplyUnitOverride(units, parameters, "pressure", SpecTypeId.PipingPressure, _pipePressureUnitMap, applied);
            ApplyUnitOverride(units, parameters, "friction", SpecTypeId.PipingFriction, _pipeFrictionUnitMap, applied);
            ApplyUnitOverride(units, parameters, "slope", SpecTypeId.PipingSlope, _pipeSlopeUnitMap, applied);

            // 4) 套用（全案性，單一 Transaction）
            using (Transaction t = new Transaction(doc, "Set Project Units"))
            {
                t.Start();
                doc.SetUnits(units);
                t.Commit();
            }

            // 5) 回讀：從 Document 讀回實際生效的設定，不是參數回聲
            Units after = doc.GetUnits();

            return new
            {
                Success = true,
                Mode = mode ?? (system ?? "metric"),
                BaseSystem = baseSystem.ToString(),
                Applied = applied,
                Result = new
                {
                    Length = ReportFormat(after, SpecTypeId.Length),
                    Area = ReportFormat(after, SpecTypeId.Area),
                    Volume = ReportFormat(after, SpecTypeId.Volume),
                    AirFlow = ReportFormat(after, SpecTypeId.AirFlow),
                    PipeSize = ReportFormat(after, SpecTypeId.PipeSize),
                    Flow = ReportFormat(after, SpecTypeId.Flow),
                    Velocity = ReportFormat(after, SpecTypeId.PipingVelocity),
                    Pressure = ReportFormat(after, SpecTypeId.PipingPressure),
                    Friction = ReportFormat(after, SpecTypeId.PipingFriction),
                    Slope = ReportFormat(after, SpecTypeId.PipingSlope),
                },
                Note = isTaiwanPlumbing
                    ? "壓力為 mH2O 代用（Revit 2026 無 kgf/cm²）：1 kgf/cm² = 10.0 mH2O，設備編 §46 的「≥1.7」讀作「≥17.0 mH2O」。另注意坡度 1:ratio 的零坡度會顯示 1:0.00，字面語意相反（那是平的，不是無限陡）。"
                    : null,
                Message = "已套用專案單位（可用 Ctrl+Z 還原）。Result 為套用後從 Document 回讀的實際值，非參數回聲。"
            };
        }

        /// <summary>
        /// 設定單一 spec 的單位＋符號＋精度。任一步失敗都不中斷整批，逐項回報。
        /// symbol 傳 null 表示不設符號（沿用該單位的預設）。
        /// </summary>
        private static void ApplyFormat(
            Units units, ForgeTypeId spec, string label,
            ForgeTypeId unit, ForgeTypeId symbol, double accuracy,
            List<object> applied, string from)
        {
            string symbolStatus = symbol == null ? "not-requested" : "pending";
            FormatOptions fo;

            try
            {
                fo = new FormatOptions(unit);
            }
            catch (Exception ex)
            {
                applied.Add(new { spec = label, from, error = "建立 FormatOptions 失敗：" + ex.Message });
                return;
            }

            try { fo.Accuracy = accuracy; }
            catch (Exception ex) { applied.Add(new { spec = label, from, warning = "精度設定失敗：" + ex.Message }); }

            if (symbol != null)
            {
                try { fo.SetSymbolTypeId(symbol); symbolStatus = "set"; }
                catch (Exception ex) { symbolStatus = "failed: " + ex.Message; }
            }

            try
            {
                units.SetFormatOptions(spec, fo);
                applied.Add(new { spec = label, unit = SafeTypeId(unit), symbol = symbolStatus, accuracy, from });
            }
            catch (Exception ex)
            {
                applied.Add(new { spec = label, unit = SafeTypeId(unit), symbol = symbolStatus, accuracy, from, error = ex.Message });
            }
        }

        private static void ApplyUnitOverride(
            Units units, JObject parameters, string paramKey,
            ForgeTypeId spec, Dictionary<string, ForgeTypeId> map, List<object> applied)
        {
            string v = parameters[paramKey]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            if (!map.TryGetValue(v, out ForgeTypeId unitId))
                throw new Exception("不支援的 " + paramKey + " 單位 '" + v + "'。可用值：" + string.Join(", ", map.Keys));

            // 覆寫時盡量保留既有精度與符號（若該 spec 已被模式設過）
            double accuracy = 0.01;
            ForgeTypeId symbol = null;
            try
            {
                FormatOptions prev = units.GetFormatOptions(spec);
                accuracy = prev.Accuracy;
                try
                {
                    ForgeTypeId s = prev.GetSymbolTypeId();
                    if (s != null && !string.IsNullOrEmpty(s.TypeId)) symbol = s;
                }
                catch { }
            }
            catch { }

            // 單位換了，舊符號多半不再合法 → ApplyFormat 內部設不上就退回無符號，不中斷
            ApplyFormat(units, spec, paramKey, unitId, symbol, accuracy, applied, "override");
        }

        private static string SafeTypeId(ForgeTypeId id)
        {
            try { return id == null ? null : id.TypeId; } catch { return null; }
        }

        /// <summary>套用後從 Document 回讀實際生效的單位／符號／精度。</summary>
        private static object ReportFormat(Units units, ForgeTypeId spec)
        {
            try
            {
                FormatOptions fo = units.GetFormatOptions(spec);
                string sym = null;
                try
                {
                    ForgeTypeId s = fo.GetSymbolTypeId();
                    if (s != null && !string.IsNullOrEmpty(s.TypeId)) sym = s.TypeId;
                }
                catch { }

                return new { unit = SafeTypeId(fo.GetUnitTypeId()), symbol = sym, accuracy = fo.Accuracy };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        #endregion
    }
}
