using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
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
        #region 門窗型別批次建立

        /// <summary>
        /// 批次建立門/窗「型別」（FamilySymbol）：複製種子型別 → 命名 → 設定寬×高（輸入公分）。
        /// 只建立型別，絕不放置任何實體。可重複執行（idempotent）：同名型別已存在則略過。
        /// </summary>
        private object CreateOpeningTypes(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var itemsArray = parameters?["items"] as JArray;
            if (itemsArray == null || itemsArray.Count == 0)
                throw new Exception("必須提供 items 陣列（至少一筆門窗型別）");

            var created = new List<object>();
            var skipped = new List<object>();
            var failed = new List<object>();

            foreach (var itemToken in itemsArray)
            {
                var item = itemToken as JObject;
                string typeName = item?["typeName"]?.Value<string>();
                string category = item?["category"]?.Value<string>();
                string seedFamilyName = item?["seedFamilyName"]?.Value<string>();
                string seedTypeName = item?["seedTypeName"]?.Value<string>();

                // widthCm/heightCm 解析包在 try 內：client 傳非數值時該 item 進 failed，不炸整批
                double? widthCm;
                double? heightCm;
                try
                {
                    widthCm = item?["widthCm"]?.Value<double?>();
                    heightCm = item?["heightCm"]?.Value<double?>();
                }
                catch (Exception ex)
                {
                    failed.Add(new { TypeName = typeName ?? "<null>", Reason = $"widthCm/heightCm 不是有效數值: {ex.Message}" });
                    continue;
                }

                // 基本輸入驗證
                if (string.IsNullOrEmpty(typeName))
                {
                    failed.Add(new { TypeName = typeName ?? "<null>", Reason = "缺少 typeName" });
                    continue;
                }
                if (string.IsNullOrEmpty(seedFamilyName) || string.IsNullOrEmpty(seedTypeName))
                {
                    failed.Add(new { TypeName = typeName, Reason = "缺少 seedFamilyName 或 seedTypeName" });
                    continue;
                }
                if (!widthCm.HasValue || !heightCm.HasValue)
                {
                    failed.Add(new { TypeName = typeName, Reason = "缺少 widthCm 或 heightCm" });
                    continue;
                }

                BuiltInCategory bic;
                if (category == "door") bic = BuiltInCategory.OST_Doors;
                else if (category == "window") bic = BuiltInCategory.OST_Windows;
                else
                {
                    failed.Add(new { TypeName = typeName, Reason = $"category 必須為 door 或 window，收到: {category ?? "<null>"}" });
                    continue;
                }

                // a. 找種子 FamilySymbol（唯讀查詢，交易外）
                FamilySymbol seedSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(bic)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.FamilyName == seedFamilyName && s.Name == seedTypeName);

                if (seedSymbol == null)
                {
                    failed.Add(new { TypeName = typeName, Reason = $"seed not found: {seedFamilyName} : {seedTypeName}" });
                    continue;
                }

                // b. 冪等：同 family 內已存在 typeName → 略過
                FamilySymbol existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(bic)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.FamilyName == seedFamilyName && s.Name == typeName);

                if (existing != null)
                {
                    skipped.Add(new { TypeName = typeName, Reason = "exists" });
                    continue;
                }

                // c. 複製並設定寬高（修改包在交易內）
                double widthInternal = UnitUtils.ConvertToInternalUnits(widthCm.Value, UnitTypeId.Centimeters);
                double heightInternal = UnitUtils.ConvertToInternalUnits(heightCm.Value, UnitTypeId.Centimeters);

                BuiltInParameter[] widthBips = bic == BuiltInCategory.OST_Doors
                    ? new[] { BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.DOOR_WIDTH }
                    : new[] { BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.WINDOW_WIDTH };
                BuiltInParameter[] heightBips = bic == BuiltInCategory.OST_Doors
                    ? new[] { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.DOOR_HEIGHT }
                    : new[] { BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.WINDOW_HEIGHT };

                using (Transaction t = new Transaction(doc, $"建立門窗型別 {typeName}"))
                {
                    try
                    {
                        t.Start();

                        var newSymbol = seedSymbol.Duplicate(typeName) as FamilySymbol;
                        if (newSymbol == null)
                        {
                            t.RollBack();
                            failed.Add(new { TypeName = typeName, Reason = "Duplicate 回傳非 FamilySymbol" });
                            continue;
                        }

                        bool widthSet = TrySetTypeDimension(newSymbol, widthBips, new[] { "寬度", "Width" }, widthInternal);
                        bool heightSet = TrySetTypeDimension(newSymbol, heightBips, new[] { "高度", "Height" }, heightInternal);

                        // d. 任一維度設不到 → 回滾＋failed，不留尺寸錯一半的型別
                        if (!widthSet || !heightSet)
                        {
                            t.RollBack();
                            string which = (!widthSet && !heightSet)
                                ? "width and height parameters not settable"
                                : (!widthSet ? "width parameter not settable" : "height parameter not settable");
                            failed.Add(new { TypeName = typeName, Reason = $"{which}（BuiltInParameter 與 繁中/英文名 皆查無可寫參數）" });
                            continue;
                        }

                        t.Commit();

                        created.Add(new
                        {
                            TypeName = newSymbol.Name,
                            ElementId = newSymbol.Id.GetIdValue()
                        });
                    }
                    catch (Exception ex)
                    {
                        if (t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();
                        failed.Add(new { TypeName = typeName, Reason = ex.Message });
                    }
                }
            }

            return new
            {
                Created = created,
                Skipped = skipped,
                Failed = failed
            };
        }

        /// <summary>
        /// 依序嘗試 BuiltInParameter 清單、再嘗試名稱清單，設定第一個可寫的 Double 型別參數。
        /// 全部失敗回傳 false。
        /// </summary>
        private bool TrySetTypeDimension(FamilySymbol symbol, BuiltInParameter[] bips, string[] names, double internalValue)
        {
            foreach (var bip in bips)
            {
                Parameter p = symbol.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    if (p.Set(internalValue)) return true;
                }
            }
            foreach (var name in names)
            {
                Parameter p = symbol.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    if (p.Set(internalValue)) return true;
                }
            }
            return false;
        }

        #endregion
    }
}
