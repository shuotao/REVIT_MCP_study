using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    public partial class CommandExecutor
    {
        #region 牆飾條等分

        /// <summary>
        /// 在指定牆上以牆飾條（WallSweep）或分隔縫（Reveal）把牆等分。
        /// 垂直方向沿牆長等分、水平方向沿牆高等分，divisions 等分會建立 divisions-1 條。
        /// </summary>
        private object CreateWallSweeps(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string sweepKind = (parameters["sweepKind"]?.Value<string>() ?? "sweep").Trim().ToLowerInvariant();
            bool isReveal = sweepKind == "reveal";
            BuiltInCategory typeCategory = isReveal ? BuiltInCategory.OST_Reveals : BuiltInCategory.OST_Cornices;

            // 用類別過濾而非 OfClass(typeof(HostedSweepType))：實測後者對牆飾條這種系統族群類型抓不到，會回空清單
            List<ElementType> availableTypes = new FilteredElementCollector(doc)
                .OfCategory(typeCategory)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .ToList();

            List<FamilySymbol> availableProfiles = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_ProfileFamilies)
                .Cast<FamilySymbol>()
                .ToList();

            // listOnly：只回報環境現況（有哪些飾條類型、哪些輪廓），不動模型
            if (parameters["listOnly"]?.Value<bool>() ?? false)
            {
                return new
                {
                    Success = true,
                    Mode = "listOnly",
                    SweepKind = isReveal ? "reveal" : "sweep",
                    SweepTypes = availableTypes
                        .Select(t => new { Id = t.Id.GetIdValue(), Name = t.Name, Family = t.FamilyName })
                        .ToList(),
                    Profiles = availableProfiles
                        .Select(s => new { Id = s.Id.GetIdValue(), Family = s.Family?.Name ?? "", Type = s.Name })
                        .ToList(),
                    Message = $"專案中有 {availableTypes.Count} 個{(isReveal ? "分隔縫" : "牆飾條")}類型、{availableProfiles.Count} 個輪廓類型。"
                };
            }

            IdType wallIdValue = parameters["wallId"]?.Value<IdType>() ?? 0;
            if (wallIdValue == 0)
                throw new Exception("必須指定 wallId。");

            Wall wall = doc.GetElement(wallIdValue.ToElementId()) as Wall;
            if (wall == null)
                throw new Exception($"找不到 ID {wallIdValue} 的牆，或該元素不是牆。");

            if (!WallSweep.WallAllowsWallSweep(wall))
                throw new Exception($"牆 {wallIdValue} 不接受牆飾條（帷幕牆、面牆等不支援）。");

            int divisions = parameters["divisions"]?.Value<int>() ?? 5;
            if (divisions < 2)
                throw new Exception("divisions 必須 >= 2（2 等分需要 1 條飾條）。");

            string orientation = (parameters["orientation"]?.Value<string>() ?? "vertical").Trim().ToLowerInvariant();
            bool isVertical = orientation != "horizontal";

            WallSide wallSide =
                (parameters["wallSide"]?.Value<string>() ?? "exterior").Trim().ToLowerInvariant() == "interior"
                    ? WallSide.Interior
                    : WallSide.Exterior;

            DistanceMeasuredFrom measuredFrom =
                (parameters["distanceMeasuredFrom"]?.Value<string>() ?? "base").Trim().ToLowerInvariant() == "top"
                    ? DistanceMeasuredFrom.Top
                    : DistanceMeasuredFrom.Base;

            // 等分跨距：垂直飾條沿牆長、水平飾條沿牆高
            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null)
                throw new Exception("這道牆沒有位置曲線，無法計算等分位置。");

            double wallLengthFt = locCurve.Curve.Length;
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double wallHeightFt = heightParam != null ? heightParam.AsDouble() : 0.0;

            double spanFt = isVertical ? wallLengthFt : wallHeightFt;
            if (spanFt <= 0)
                throw new Exception(isVertical ? "牆長度為 0。" : "取不到牆高度（非未接合高度牆）。");

            List<object> planned = new List<object>();
            for (int i = 1; i < divisions; i++)
            {
                double dFt = spanFt * i / divisions;
                planned.Add(new { Index = i, DistanceMm = Math.Round(dFt * 304.8, 2) });
            }

            if (parameters["dryRun"]?.Value<bool>() ?? false)
            {
                return new
                {
                    Success = true,
                    Mode = "dryRun",
                    WallId = wallIdValue,
                    Orientation = isVertical ? "vertical" : "horizontal",
                    SweepKind = isReveal ? "reveal" : "sweep",
                    Divisions = divisions,
                    SpanMm = Math.Round(spanFt * 304.8, 2),
                    SegmentMm = Math.Round(spanFt * 304.8 / divisions, 2),
                    PlannedSweeps = planned,
                    ExistingSweepTypes = availableTypes.Select(t => new { Id = t.Id.GetIdValue(), Name = t.Name }).ToList(),
                    Message = $"預演：將建立 {divisions - 1} 條{(isVertical ? "垂直" : "水平")}{(isReveal ? "分隔縫" : "牆飾條")}。"
                };
            }

            if (availableTypes.Count == 0)
                throw new Exception(
                    $"專案中沒有任何{(isReveal ? "分隔縫（Reveal）" : "牆飾條（Wall Sweep）")}類型。" +
                    "這是系統族群，API 只能從既有類型複製，無法憑空建立。" +
                    "請先在 Revit 手動放一條（建築 → 牆 → 牆:飾條）或從其他專案 Transfer Project Standards，再重跑本工具。");

            // 輪廓：NewFamilyDocument / SaveAs 必須在 Transaction 之外；專案端 LoadFamily 在 CreateRectangleProfileFamily 內自開獨立交易
            FamilySymbol profileSymbol = ResolveSweepProfile(doc, parameters, availableProfiles, out string profileNote);

            // 來源類型：指定 baseTypeId 就用它，否則取第一個
            ElementType baseType = availableTypes[0];
            IdType baseTypeIdValue = parameters["baseTypeId"]?.Value<IdType>() ?? 0;
            if (baseTypeIdValue != 0)
            {
                baseType = availableTypes.FirstOrDefault(t => t.Id.GetIdValue() == baseTypeIdValue)
                           ?? throw new Exception($"找不到 ID {baseTypeIdValue} 的{(isReveal ? "分隔縫" : "牆飾條")}類型。");
            }

            string typeName = parameters["typeName"]?.Value<string>();
            List<object> created = new List<object>();
            List<string> failures = new List<string>();
            IdType usedTypeId;
            string usedTypeName;

            using (Transaction trans = new Transaction(doc, isVertical ? "垂直牆飾條等分" : "水平牆飾條等分"))
            {
                trans.Start();

                ElementType sweepType = baseType;

                // 需要換輪廓或指定新名稱時才複製類型，避免污染既有類型
                if (profileSymbol != null || !string.IsNullOrWhiteSpace(typeName))
                {
                    string newName = !string.IsNullOrWhiteSpace(typeName)
                        ? typeName
                        : $"MCP-{(isReveal ? "分隔縫" : "飾條")}-{profileSymbol?.Family?.Name ?? "自訂"}";

                    ElementType sameName = availableTypes
                        .FirstOrDefault(t => string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase));

                    sweepType = sameName ?? baseType.Duplicate(newName) as ElementType;
                    if (sameName != null && profileSymbol != null)
                    {
                        // 同名類型已存在：不改寫其輪廓，以免連動改變既有飾條
                        profileNote += $" 類型「{newName}」已存在，未改寫其輪廓參數（避免變更既有飾條）；如需套用新輪廓請改用新的 typeName。";
                        profileSymbol = null;
                    }
                    if (sweepType == null)
                        throw new Exception($"複製{(isReveal ? "分隔縫" : "牆飾條")}類型「{baseType.Name}」失敗。");
                }

                if (profileSymbol != null)
                {
                    if (!profileSymbol.IsActive)
                        profileSymbol.Activate();

                    Parameter profileParam = sweepType.get_Parameter(BuiltInParameter.WALL_SWEEP_PROFILE_PARAM);
                    if (profileParam == null || profileParam.IsReadOnly)
                        throw new Exception($"類型「{sweepType.Name}」沒有可寫入的輪廓參數。");

                    profileParam.Set(profileSymbol.Id);
                }

                usedTypeId = sweepType.Id.GetIdValue();
                usedTypeName = sweepType.Name;

                for (int i = 1; i < divisions; i++)
                {
                    double distanceFt = spanFt * i / divisions;
                    try
                    {
                        WallSweepInfo info = new WallSweepInfo(
                            isReveal ? WallSweepType.Reveal : WallSweepType.Sweep,
                            isVertical);

                        info.Distance = distanceFt;
                        info.WallSide = wallSide;
                        if (!isVertical)
                            info.DistanceMeasuredFrom = measuredFrom;

                        WallSweep sweep = WallSweep.Create(wall, sweepType.Id, info);
                        created.Add(new
                        {
                            Index = i,
                            ElementId = sweep.Id.GetIdValue(),
                            DistanceMm = Math.Round(distanceFt * 304.8, 2)
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"第 {i} 條（距離 {Math.Round(distanceFt * 304.8, 2)} mm）：{ex.Message}");
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = failures.Count == 0,
                WallId = wallIdValue,
                WallLengthMm = Math.Round(wallLengthFt * 304.8, 2),
                WallHeightMm = Math.Round(wallHeightFt * 304.8, 2),
                Orientation = isVertical ? "vertical" : "horizontal",
                SweepKind = isReveal ? "reveal" : "sweep",
                WallSide = wallSide.ToString(),
                Divisions = divisions,
                SegmentMm = Math.Round(spanFt * 304.8 / divisions, 2),
                SweepTypeId = usedTypeId,
                SweepTypeName = usedTypeName,
                ProfileNote = profileNote,
                CreatedCount = created.Count,
                CreatedSweeps = created,
                Failures = failures,
                Message = $"在牆 {wallIdValue} 上建立 {created.Count} 條{(isVertical ? "垂直" : "水平")}" +
                          $"{(isReveal ? "分隔縫" : "牆飾條")}，牆{(isVertical ? "長" : "高")}分成 {divisions} 等分" +
                          $"（每段 {Math.Round(spanFt * 304.8 / divisions, 2)} mm）。"
            };
        }

        /// <summary>
        /// 決定飾條要用的輪廓：指定既有輪廓 &gt; 依尺寸建立矩形輪廓 &gt; 沿用來源類型原本的輪廓（回傳 null）。
        /// 必須在 Transaction 之外呼叫。
        /// </summary>
        private FamilySymbol ResolveSweepProfile(
            Document doc,
            JObject parameters,
            List<FamilySymbol> availableProfiles,
            out string note)
        {
            IdType profileTypeId = parameters["profileTypeId"]?.Value<IdType>() ?? 0;
            if (profileTypeId != 0)
            {
                FamilySymbol picked = availableProfiles.FirstOrDefault(s => s.Id.GetIdValue() == profileTypeId)
                                      ?? throw new Exception($"找不到 ID {profileTypeId} 的輪廓類型。");
                note = $"使用既有輪廓「{picked.Family?.Name}: {picked.Name}」。";
                return picked;
            }

            double widthMm = parameters["profileWidth"]?.Value<double>() ?? 0;
            double depthMm = parameters["profileDepth"]?.Value<double>() ?? 0;
            if (widthMm <= 0 || depthMm <= 0)
            {
                note = "未指定輪廓，沿用來源飾條類型原本的輪廓。";
                return null;
            }

            string familyName = parameters["profileFamilyName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = $"MCP-矩形輪廓-{widthMm:0}x{depthMm:0}";

            FamilySymbol existing = availableProfiles
                .FirstOrDefault(s => string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                note = $"沿用已載入的輪廓族群「{familyName}」。";
                return existing;
            }

            FamilySymbol built = CreateRectangleProfileFamily(doc, familyName, widthMm, depthMm);
            note = $"新建矩形輪廓族群「{familyName}」（{widthMm}×{depthMm} mm）。";
            return built;
        }

        /// <summary>
        /// 用輪廓族群樣板現做一個矩形輪廓並載入專案。必須在外層 Transaction 之外呼叫（內部自開載入交易）。
        /// </summary>
        private FamilySymbol CreateRectangleProfileFamily(Document doc, string familyName, double widthMm, double depthMm)
        {
            string templatePath = FindProfileTemplate(doc.Application.FamilyTemplatePath);
            if (templatePath == null)
                throw new Exception("找不到輪廓族群樣板（公制輪廓.rft / Metric Profile.rft），無法自動建立矩形輪廓。請改用 profileTypeId 指定既有輪廓。");

            string outDir = Path.Combine(Path.GetTempPath(), "RevitMCP_Profiles");
            Directory.CreateDirectory(outDir);
            string rfaPath = Path.Combine(outDir, familyName + ".rfa");

            Document famDoc = doc.Application.NewFamilyDocument(templatePath);
            if (famDoc == null)
                throw new Exception("建立輪廓族群文件失敗。");

            try
            {
                using (Transaction ft = new Transaction(famDoc, "繪製矩形輪廓"))
                {
                    ft.Start();

                    View famView = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate);

                    if (famView == null)
                        throw new Exception("輪廓樣板中找不到可繪製的視圖。");

                    double w = widthMm / 304.8;
                    double d = depthMm / 304.8;

                    // 輪廓平面：X 為沿牆面方向（垂直飾條時即牆高方向），Y 為凸出牆面方向
                    XYZ p0 = new XYZ(-w / 2, 0, 0);
                    XYZ p1 = new XYZ(w / 2, 0, 0);
                    XYZ p2 = new XYZ(w / 2, d, 0);
                    XYZ p3 = new XYZ(-w / 2, d, 0);

                    CurveArray rect = new CurveArray();
                    rect.Append(Line.CreateBound(p0, p1));
                    rect.Append(Line.CreateBound(p1, p2));
                    rect.Append(Line.CreateBound(p2, p3));
                    rect.Append(Line.CreateBound(p3, p0));

                    famDoc.FamilyCreate.NewDetailCurveArray(famView, rect);

                    ft.Commit();
                }

                SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                famDoc.SaveAs(rfaPath, saveOptions);
            }
            finally
            {
                famDoc.Close(false);
            }

            Family loaded;
            using (Transaction lt = new Transaction(doc, "載入矩形輪廓族群"))
            {
                lt.Start();
                bool ok = doc.LoadFamily(rfaPath, out loaded);
                if (!ok || loaded == null)
                {
                    loaded = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
                }

                if (loaded == null)
                {
                    lt.RollBack();
                    throw new Exception($"輪廓族群「{familyName}」載入專案失敗。");
                }
                lt.Commit();
            }

            FamilySymbol symbol = loaded.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .FirstOrDefault(s => s != null);

            if (symbol == null)
                throw new Exception($"輪廓族群「{familyName}」沒有可用的類型。");

            return symbol;
        }

        /// <summary>
        /// 從 Revit 的族群樣板資料夾找出通用輪廓樣板，繁中優先。
        /// </summary>
        private string FindProfileTemplate(string familyTemplateRoot)
        {
            List<string> roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(familyTemplateRoot) && Directory.Exists(familyTemplateRoot))
                roots.Add(familyTemplateRoot);

            foreach (string guess in new[]
                     {
                         $@"C:\ProgramData\Autodesk\RVT {_uiApp.Application.VersionNumber}\Family Templates"
                     })
            {
                if (Directory.Exists(guess) && !roots.Contains(guess))
                    roots.Add(guess);
            }

            string[] preferred = { "公制輪廓.rft", "Metric Profile.rft", "Profile.rft" };

            foreach (string root in roots)
            {
                foreach (string name in preferred)
                {
                    string hit = Directory
                        .EnumerateFiles(root, name, SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (hit != null)
                        return hit;
                }
            }

            return null;
        }

        #endregion
    }
}
