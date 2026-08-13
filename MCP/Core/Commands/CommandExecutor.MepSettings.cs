using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// Manage → MEP Settings 的唯讀盤點，兩支工具分工：
    ///
    /// get_mep_segments_and_sizes — Segments and Sizes 那兩頁（管段目錄 + 風管尺寸表）。
    ///   每個 PipeSegment（材質 × Schedule）各帶一份尺寸表，Schedule 撈不到、System Browser 也看不到。
    ///
    /// get_mep_settings — 其餘各頁（Angles / Slopes / Fluids / Calculation / 命名與註記 / Hidden Line）。
    ///
    /// 兩支都純唯讀、不開 Transaction。長度一律由內部單位（feet）轉 mm、角度轉度，
    /// 物理量另附以專案顯示單位格式化的字串，供台灣 CNS 對帳使用。
    /// </summary>
    public partial class CommandExecutor
    {
        #region get_mep_segments_and_sizes

        private object GetMepSegmentsAndSizes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            bool usedOnly = parameters["usedOnly"]?.Value<bool?>() ?? false;
            bool summaryOnly = parameters["summaryOnly"]?.Value<bool?>() ?? false;
            string segmentName = parameters["segmentName"]?.Value<string>()?.Trim();
            bool hasNameFilter = !string.IsNullOrWhiteSpace(segmentName);
            // 指定 segmentName 時預設不帶風管（是在鑽某個管段，風管只會是雜訊）；明確給 includeDuct 則以它為準。
            bool includeDuct = parameters["includeDuct"]?.Value<bool?>() ?? !hasNameFilter;

            // 1) 管段（PipeSegment 是 Segment 唯一的衍生類別）
            var segments = new List<object>();
            int totalPipeSizes = 0;
            int reportedPipeSizes = 0;

            var allPipeSegments = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeSegment))
                .Cast<PipeSegment>()
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pipeSegments = hasNameFilter
                ? allPipeSegments.Where(s => s.Name != null &&
                    s.Name.IndexOf(segmentName, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                : allPipeSegments;

            foreach (PipeSegment seg in pipeSegments)
            {
                var sizes = new List<object>();
                int inSizeLists = 0;
                int inSizing = 0;

                foreach (MEPSize size in seg.GetSizes().OrderBy(s => s.NominalDiameter))
                {
                    totalPipeSizes++;
                    if (size.UsedInSizeLists) inSizeLists++;
                    if (size.UsedInSizing) inSizing++;
                    if (usedOnly && !size.UsedInSizeLists && !size.UsedInSizing) continue;
                    reportedPipeSizes++;
                    if (!summaryOnly) sizes.Add(DescribeSize(size));
                }

                segments.Add(new
                {
                    id = seg.Id.GetIdValue(),
                    kind = "pipe",
                    name = seg.Name,
                    material = GetElementNameOrNull(doc, seg.MaterialId),
                    schedule = GetElementNameOrNull(doc, seg.ScheduleTypeId),
                    description = string.IsNullOrWhiteSpace(seg.Description) ? null : seg.Description,
                    roughness_mm = ToMm(seg.Roughness, 6),
                    sizeCount = seg.SizeCount,
                    usedInSizeListsCount = inSizeLists,
                    usedInSizingCount = inSizing,
                    sizes = summaryOnly ? null : sizes,
                });
            }

            // 2) 風管尺寸（DuctSizeSettings 是全案一份，依 Round / Rectangular / Oval 分表）
            var ductShapes = new List<object>();
            var ductRoundNominalMm = new List<double>();
            string ductNote = null;

            if (includeDuct)
            {
                try
                {
                    DuctSizeSettings ductSettings = DuctSizeSettings.GetDuctSizeSettings(doc);
                    if (ductSettings == null)
                    {
                        ductNote = "此文件沒有 DuctSizeSettings（可能不是 MEP 樣板）。";
                    }
                    else
                    {
                        foreach (DuctShape shape in new[] { DuctShape.Round, DuctShape.Rectangular, DuctShape.Oval })
                        {
                            DuctSizes ductSizes = ductSettings[shape];
                            if (ductSizes == null) continue;

                            var shapeSizes = new List<object>();
                            int inSizeLists = 0;
                            int inSizing = 0;

                            foreach (MEPSize size in EnumerateDuctSizes(ductSizes).OrderBy(s => s.NominalDiameter))
                            {
                                if (size.UsedInSizeLists) inSizeLists++;
                                if (size.UsedInSizing) inSizing++;
                                if (usedOnly && !size.UsedInSizeLists && !size.UsedInSizing) continue;
                                if (!summaryOnly) shapeSizes.Add(DescribeDuctSize(size));
                                if (shape == DuctShape.Round)
                                    ductRoundNominalMm.Add(ToMm(size.NominalDiameter));
                            }

                            ductShapes.Add(new
                            {
                                shape = shape.ToString(),
                                sizeCount = ductSizes.Count,
                                usedInSizeListsCount = inSizeLists,
                                usedInSizingCount = inSizing,
                                sizes = summaryOnly ? null : shapeSizes,
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 風管設定讀取失敗不應讓整支工具失敗——管段目錄仍有價值
                    ductNote = $"讀取風管尺寸失敗：{ex.Message}";
                }
            }

            return new
            {
                Success = true,
                UsedOnly = usedOnly,
                SummaryOnly = summaryOnly,
                IncludeDuct = includeDuct,
                SegmentNameFilter = hasNameFilter ? segmentName : null,
                SegmentCount = segments.Count,
                SegmentTotalCount = allPipeSegments.Count,
                PipeSizeCount = new { total = totalPipeSizes, reported = reportedPipeSizes },
                Segments = segments,
                DuctSizes = includeDuct ? ductShapes : null,
                DuctRoundSizes_mm = includeDuct ? ductRoundNominalMm : null,
                // Revit 的風管尺寸表只有 nominal 一個維度；MEPSize 的 inner/outer 對 duct 一律回固定佔位值
                // （實測 Round/Rectangular/Oval 三表全為 12 ft），輸出會誤導對帳，故只給 nominal。
                DuctSizeNote = includeDuct && ductShapes.Count > 0
                    ? "風管尺寸只有 nominal；Revit 對 duct 的 inner/outer 回傳固定佔位值，故不輸出。"
                    : null,
                DuctNote = ductNote,
                Message = BuildMepInventoryMessage(segments.Count, allPipeSegments.Count, totalPipeSizes, reportedPipeSizes, usedOnly, summaryOnly, hasNameFilter)
            };
        }

        /// <summary>組回傳訊息，把「篩掉了什麼」講清楚，避免把局部結果誤讀成全案盤點</summary>
        private static string BuildMepInventoryMessage(
            int shown, int total, int totalSizes, int reportedSizes,
            bool usedOnly, bool summaryOnly, bool hasNameFilter)
        {
            string scope = hasNameFilter
                ? $"符合名稱篩選的 {shown} 個管段（全案共 {total} 個）"
                : $"共 {shown} 個管段";

            string sizePart = summaryOnly
                ? $"，僅回傳統計（{totalSizes} 筆尺寸未逐筆列出）"
                : usedOnly
                    ? $"；僅列出勾選 Used in Size Lists / Used in Sizing 的尺寸（{reportedSizes}/{totalSizes}）"
                    : $"、{totalSizes} 筆管尺寸";

            return scope + sizePart + "。所有尺寸單位為 mm。";
        }

        /// <summary>管尺寸 → 輸出物件（內部單位 feet 轉 mm；pipe 的 inner/outer 是真實值）</summary>
        private static object DescribeSize(MEPSize size)
        {
            return new
            {
                nominal_mm = ToMm(size.NominalDiameter),
                inner_mm = ToMm(size.InnerDiameter),
                outer_mm = ToMm(size.OuterDiameter),
                usedInSizeLists = size.UsedInSizeLists,
                usedInSizing = size.UsedInSizing,
            };
        }

        /// <summary>
        /// 風管尺寸 → 輸出物件。刻意不輸出 inner/outer：Revit 的風管尺寸表只有 nominal 一個維度，
        /// MEPSize 的 inner/outer 對 duct 是固定佔位值（實測三種形狀全為 12 ft），輸出只會誤導。
        /// </summary>
        private static object DescribeDuctSize(MEPSize size)
        {
            return new
            {
                nominal_mm = ToMm(size.NominalDiameter),
                usedInSizeLists = size.UsedInSizeLists,
                usedInSizing = size.UsedInSizing,
            };
        }

        /// <summary>用 Revit 原生 iterator 走訪 DuctSizes（不依賴其泛型 IEnumerable 介面）</summary>
        private static IEnumerable<MEPSize> EnumerateDuctSizes(DuctSizes ductSizes)
        {
            DuctSizeIterator iterator = ductSizes.GetDuctSizeIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                MEPSize current = iterator.Current;
                if (current != null) yield return current;
            }
        }

        /// <summary>內部單位（feet）轉 mm</summary>
        private static double ToMm(double internalValue, int digits = 4)
        {
            return Math.Round(UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters), digits);
        }

        /// <summary>取得 ElementId 對應元素的名稱；無效 id 或查無元素時回傳 null</summary>
        private static string GetElementNameOrNull(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            Element element = doc.GetElement(id);
            return element?.Name;
        }

        #endregion

        #region get_mep_settings

        private object GetMepSettings(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            Units units = doc.GetUnits();

            bool includeFluids = parameters["includeFluids"]?.Value<bool?>() ?? true;
            bool includeFluidTemperatures = parameters["includeFluidTemperatures"]?.Value<bool?>() ?? false;

            // 每頁各自 try/catch：某一頁讀不到不該讓整支工具失敗，其餘頁面仍有價值
            object duct = MepSafeRead(() => MepReadDuctSettings(doc, units), out string ductNote);
            object pipe = MepSafeRead(() => MepReadPipeSettings(doc, units, includeFluids, includeFluidTemperatures), out string pipeNote);
            object hiddenLine = MepSafeRead(() => MepReadHiddenLineSettings(doc, units), out string hiddenNote);

            return new
            {
                Success = true,
                IncludeFluids = includeFluids,
                IncludeFluidTemperatures = includeFluidTemperatures,
                Duct = duct,
                DuctNote = ductNote,
                Pipe = pipe,
                PipeNote = pipeNote,
                HiddenLine = hiddenLine,
                HiddenLineNote = hiddenNote,
                Coverage = "Angles / Slopes / Fluids / Calculation / 命名與註記 / Hidden Line。管段與尺寸目錄請用 get_mep_segments_and_sizes。",
                Message = "已讀取 MEP Settings（唯讀，未變更模型）。角度為度、長度為 mm，物理量另附專案顯示單位字串。"
            };
        }

        /// <summary>Duct Settings：Angles / Calculation / 命名與註記 / 標高文字</summary>
        private static object MepReadDuctSettings(Document doc, Units units)
        {
            DuctSettings ds = DuctSettings.GetDuctSettings(doc);
            if (ds == null) return null;

            // AirViscosity 在 R26 被移除，R25 起改名 AirDynamicViscosity（R25 兩者並存）
#if REVIT2025_OR_GREATER
            double airViscosity = ds.AirDynamicViscosity;
#else
            double airViscosity = ds.AirViscosity;
#endif
            // NetworkBasedCalculations 自 R24 才有
#if REVIT2024_OR_GREATER
            bool? networkBasedCalculations = ds.NetworkBasedCalculations;
#else
            bool? networkBasedCalculations = null;
#endif

            return new
            {
                fittingAngleUsage = ds.FittingAngleUsage.ToString(),
                // 角度清單一直讀得到，但只有 usage=UseSpecificAngles 時才真正生效
                specificAnglesInEffect = ds.FittingAngleUsage == FittingAngleUsage.UseSpecificAngles,
                specificAngles = MepDescribeAngles(ds.GetSpecificFittingAngles(), ds.GetSpecificFittingAngleStatus),
                otherAngleParameters = MepScanAngleParameters(ds),
                calculation = new
                {
                    airDensity = new { raw = ds.AirDensity, display = MepFormatValue(units, SpecTypeId.MassDensity, ds.AirDensity) },
                    airViscosity = new { raw = airViscosity, display = MepFormatValue(units, SpecTypeId.HvacViscosity, airViscosity) },
                    networkBasedCalculations,
                },
                annotation = new
                {
                    useAnnotationScaleForSingleLineFittings = ds.UseAnnotationScaleForSingleLineFittings,
                    riseDropAnnotationSize_mm = ToMm(ds.RiseDropAnnotationSize),
                    fittingAnnotationSize_mm = ToMm(ds.FittingAnnotationSize),
                },
                sizeNaming = new
                {
                    roundPrefix = ds.RoundDuctSizePrefix,
                    roundSuffix = ds.RoundDuctSizeSuffix,
                    rectangularSeparator = ds.RectangularDuctSizeSeparator,
                    rectangularSuffix = ds.RectangularDuctSizeSuffix,
                    ovalSeparator = ds.OvalDuctSizeSeparator,
                    ovalSuffix = ds.OvalDuctSizeSuffix,
                    connectorSeparator = ds.ConnectorSeparator,
                },
                elevationText = MepDescribeElevationText(
                    ds.Centerline, ds.SetUp, ds.SetDown, ds.SetUpFromBottom, ds.SetDownFromBottom, ds.FlatOnTop, ds.FlatOnBottom),
            };
        }

        /// <summary>Pipe Settings：Angles / Slopes / Fluids / Calculation / 命名與註記 / 標高文字</summary>
        private static object MepReadPipeSettings(Document doc, Units units, bool includeFluids, bool includeFluidTemperatures)
        {
            PipeSettings ps = PipeSettings.GetPipeSettings(doc);
            if (ps == null) return null;

            // GetPipeSlopes() 回傳的是**百分比**，不是 Revit 內部的比值
            // （實測回 0 / 1.0417 / 2.0833 / 4.1667，即 1/8"、1/4"、1/2" 每 12"）。
            //
            // display 只是「Revit 在對話框會怎麼顯示」，會被專案的坡度顯示精度四捨五入
            // ——實測專案精度設 1/2" 時，1/8" 與 1/4" 會被壓成同一個字串，forEditing:true 也擋不住。
            // 所以 percent 與 ratio_1_in 才是可信的數值欄位，display 僅供對照對話框。
            var slopes = new List<object>();
            foreach (double slope in ps.GetPipeSlopes())
            {
                slopes.Add(new
                {
                    percent = Math.Round(slope, 6),
                    ratio_1_in = slope > 0 ? (double?)Math.Round(100.0 / slope, 2) : null,  // 1:N
                    display = MepFormatValue(units, SpecTypeId.Slope, slope / 100.0),
                });
            }

            return new
            {
                fittingAngleUsage = ps.FittingAngleUsage.ToString(),
                // 角度清單一直讀得到，但只有 usage=UseSpecificAngles 時才真正生效
                specificAnglesInEffect = ps.FittingAngleUsage == FittingAngleUsage.UseSpecificAngles,
                specificAngles = MepDescribeAngles(ps.GetSpecificFittingAngles(), ps.GetSpecificFittingAngleStatus),
                otherAngleParameters = MepScanAngleParameters(ps),
                slopes,
                slopesNote = "percent 與 ratio_1_in 才是精確值；display 會被專案的坡度顯示精度四捨五入，不同坡度可能顯示成同一個字串。",
                fluids = includeFluids ? MepReadFluidTypes(doc, units, includeFluidTemperatures) : null,
                calculation = new
                {
                    analysisForClosedLoopHydronicPipingNetworks = ps.AnalysisForClosedLoopHydronicPipingNetworks,
                    // ConnectorTolerance 是**角度**不是長度（實測 raw=0.0872665 恰為 5° 的弧度值），
                    // 當長度轉 mm 會得到毫無意義的 26.6 mm。
                    connectorTolerance = new
                    {
                        raw = ps.ConnectorTolerance,
                        deg = Math.Round(UnitUtils.ConvertFromInternalUnits(ps.ConnectorTolerance, UnitTypeId.Degrees), 4),
                        display = MepFormatValue(units, SpecTypeId.Angle, ps.ConnectorTolerance),
                    },
                },
                annotation = new
                {
                    useAnnotationScaleForSingleLineFittings = ps.UseAnnotationScaleForSingleLineFittings,
                    fittingAnnotationSize_mm = ToMm(ps.FittingAnnotationSize),
                },
                sizeNaming = new
                {
                    sizePrefix = ps.SizePrefix,
                    sizeSuffix = ps.SizeSuffix,
                    connectorSeparator = ps.ConnectorSeparator,
                },
                elevationText = MepDescribeElevationText(
                    ps.Centerline, ps.SetUp, ps.SetDown, ps.SetUpFromBottom, ps.SetDownFromBottom, ps.FlatOnTop, ps.FlatOnBottom),
            };
        }

        /// <summary>Fluids 頁：各流體類型與（可選）其溫度／黏度／密度表</summary>
        private static List<object> MepReadFluidTypes(Document doc, Units units, bool includeTemperatures)
        {
            var fluids = new List<object>();

            var fluidTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FluidType))
                .Cast<FluidType>()
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (FluidType fluid in fluidTypes)
            {
                var temperatures = new List<object>();
                int count = 0;

                FluidTemperatureSetIterator iterator = fluid.GetFluidTemperatureSetIterator();
                iterator.Reset();
                while (iterator.MoveNext())
                {
                    FluidTemperature ft = iterator.Current;
                    if (ft == null) continue;
                    count++;
                    if (!includeTemperatures) continue;

                    temperatures.Add(new
                    {
                        // Revit 溫度內部單位為 K
                        temperature_K = Math.Round(ft.Temperature, 4),
                        temperature_C = Math.Round(ft.Temperature - 273.15, 4),
                        viscosity = new { raw = ft.Viscosity, display = MepFormatValue(units, SpecTypeId.HvacViscosity, ft.Viscosity) },
                        density = new { raw = ft.Density, display = MepFormatValue(units, SpecTypeId.MassDensity, ft.Density) },
                    });
                }

                fluids.Add(new
                {
                    id = fluid.Id.GetIdValue(),
                    name = fluid.Name,
                    temperatureCount = count,
                    inUse = FluidType.IsFluidInUse(doc, fluid.Id),
                    temperatures = includeTemperatures ? temperatures : null,
                });
            }

            return fluids;
        }

        /// <summary>Hidden Line 頁</summary>
        private static object MepReadHiddenLineSettings(Document doc, Units units)
        {
            MEPHiddenLineSettings hl = MEPHiddenLineSettings.GetMEPHiddenLineSettings(doc);
            if (hl == null) return null;

            return new
            {
                drawHiddenLine = hl.DrawHiddenLine,
                lineStyle = GetElementNameOrNull(doc, hl.LineStyle),
                singleLineGap_mm = ToMm(hl.SingleLineGap),
                outsideGap_mm = ToMm(hl.OutsideGap),
                insideGap_mm = ToMm(hl.InsideGap),
            };
        }

        /// <summary>
        /// 角度清單 + 勾選狀態。
        /// 注意：GetSpecificFittingAngles() 回傳的**已經是「度」**，不是 Revit 內部的弧度
        /// （實測 Snowdon Towers 回 11.25 / 22.5 / 30 / 45 / 60 / 90），
        /// 再做一次 ConvertFromInternalUnits(..., Degrees) 會多乘 57.2958 倍。
        /// GetSpecificFittingAngleStatus 也要餵原值，不能餵轉換後的值。
        /// </summary>
        private static List<object> MepDescribeAngles(IList<double> angles, Func<double, bool> statusOf)
        {
            var result = new List<object>();
            if (angles == null) return result;

            foreach (double angle in angles.OrderBy(a => a))
            {
                bool enabled;
                try { enabled = statusOf(angle); }
                catch { continue; }

                result.Add(new
                {
                    deg = Math.Round(angle, 4),
                    enabled,
                });
            }

            return result;
        }

        /// <summary>
        /// 掃設定元素上名稱含「角度」的參數。
        /// FittingAngleUsage=UseAnAngleIncrement 時的「增量值」沒有對應的 BuiltInParameter
        /// （已反射確認 R22/R26 皆無），只能由此通用掃描補上。
        /// </summary>
        private static List<object> MepScanAngleParameters(Element settingsElement)
        {
            var result = new List<object>();
            if (settingsElement == null) return result;

            foreach (Parameter p in settingsElement.Parameters)
            {
                string name = p?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.IndexOf("angle", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("角度", StringComparison.Ordinal) < 0) continue;

                result.Add(new { name, value = MepReadParameterValue(p) });
            }

            return result;
        }

        /// <summary>參數值優先取顯示字串，取不到再依 StorageType 回退</summary>
        private static object MepReadParameterValue(Parameter p)
        {
            try
            {
                string display = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(display)) return display;

                switch (p.StorageType)
                {
                    case StorageType.Double: return p.AsDouble();
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String: return p.AsString();
                    case StorageType.ElementId: return p.AsElementId()?.GetIdValue();
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static object MepDescribeElevationText(
            string centerline, string setUp, string setDown,
            string setUpFromBottom, string setDownFromBottom, string flatOnTop, string flatOnBottom)
        {
            return new
            {
                centerline,
                setUp,
                setDown,
                setUpFromBottom,
                setDownFromBottom,
                flatOnTop,
                flatOnBottom,
            };
        }

        /// <summary>
        /// 以專案顯示單位格式化；spec 不適用時回 null 而非爆掉。
        /// forEditing=true 會略過專案的顯示精度四捨五入 —— 坡度要用它，
        /// 否則專案若把坡度精度設成 1/2"，1/8" 與 1/4" 會被捨進成同一個字串。
        /// </summary>
        private static string MepFormatValue(Units units, ForgeTypeId spec, double value, bool forEditing = false)
        {
            try { return UnitFormatUtils.Format(units, spec, value, forEditing); }
            catch { return null; }
        }

        /// <summary>單頁讀取包一層 try/catch，失敗時把原因放進 note、該頁回 null</summary>
        private static object MepSafeRead(Func<object> read, out string note)
        {
            try
            {
                note = null;
                return read();
            }
            catch (Exception ex)
            {
                note = $"讀取失敗：{ex.Message}";
                return null;
            }
        }

        #endregion
    }
}
