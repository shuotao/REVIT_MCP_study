using System;
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
        #region 元素操作（移動、翻轉）

        /// <summary>
        /// 移動元素（依 dx, dy, dz 公釐位移）
        /// 來源 fork: poisonsam/main:MCP/Core/Commands/CommandExecutor.Base.cs:718-751
        /// </summary>
        private object MoveElement(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType elementId = parameters["elementId"]?.Value<IdType>() ?? 0;
            double dx = parameters["dx"]?.Value<double>() ?? 0;
            double dy = parameters["dy"]?.Value<double>() ?? 0;
            double dz = parameters["dz"]?.Value<double>() ?? 0;

            Element element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                throw new Exception($"找不到元素 ID: {elementId}");
            }

            using (Transaction trans = new Transaction(doc, $"移動元素: {elementId}"))
            {
                trans.Start();

                // 單位原本為公釐，需要轉換成 Revit 專案的內部單位英尺
                XYZ translation = new XYZ(dx / 304.8, dy / 304.8, dz / 304.8);
                ElementTransformUtils.MoveElement(doc, new ElementId(elementId), translation);

                trans.Commit();

                return new
                {
                    ElementId = elementId,
                    Dx = dx,
                    Dy = dy,
                    Dz = dz,
                    Message = "成功移動元素"
                };
            }
        }

        /// <summary>
        /// 翻轉元素 facing 或 hand（門/窗）
        /// 來源 fork: poisonsam/main:MCP/Core/Commands/CommandExecutor.Base.cs:754-806
        /// </summary>
        private object FlipElement(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            IdType elementId = parameters["elementId"]?.Value<IdType>() ?? 0;
            string flipType = parameters["flipType"]?.Value<string>() ?? "facing";

            Element element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                throw new Exception($"找不到元素 ID: {elementId}");
            }

            if (!(element is FamilyInstance familyInstance))
            {
                throw new Exception($"元素 ID: {elementId} 不是有效的家庭實體，無法翻轉");
            }

            using (Transaction trans = new Transaction(doc, $"翻轉元素: {elementId}"))
            {
                trans.Start();

                if (flipType.ToLower() == "facing")
                {
                    if (familyInstance.CanFlipFacing)
                        familyInstance.flipFacing();
                    else
                        throw new Exception("此元素不支援翻轉面向 (Facing)");
                }
                else if (flipType.ToLower() == "hand")
                {
                    if (familyInstance.CanFlipHand)
                        familyInstance.flipHand();
                    else
                        throw new Exception("此元素不支援翻轉開向 (Hand)");
                }
                else
                {
                    throw new Exception("無效的翻轉類型，請使用 'facing' 或 'hand'");
                }

                trans.Commit();

                return new
                {
                    ElementId = elementId,
                    FlipType = flipType,
                    Message = "成功翻轉元素"
                };
            }
        }

        #endregion

        #region 元素參數複製（CreateDoor / CreateWindow 之 sourceElementId 支援用）

        /// <summary>
        /// 複製來源 Element 的 instance parameters 到 target Element
        /// 排除：唯讀、樓層 / 主體 / ID 類別、標記欄位（由建立時決定，不應複製）
        /// 來源 fork: poisonsam/main:MCP/Core/Commands/CommandExecutor.Architecture.cs:171-213
        /// 改動：移除原檔重複的 IsReadOnly check（line 178）
        /// </summary>
        private void CopyInstanceParameters(Element source, Element target)
        {
            foreach (Parameter sourceParam in source.Parameters)
            {
                if (sourceParam.IsReadOnly || !sourceParam.HasValue) continue;

                string paramName = sourceParam.Definition.Name;
                if (paramName.Contains("樓層") || paramName.Contains("Level") ||
                    paramName.Contains("主體") || paramName.Contains("Host") ||
                    paramName.Contains("ID") ||
                    paramName == "標記" || paramName == "Mark")
                    continue;

                Parameter targetParam = target.LookupParameter(sourceParam.Definition.Name);
                if (targetParam != null && !targetParam.IsReadOnly)
                {
                    try
                    {
                        switch (sourceParam.StorageType)
                        {
                            case StorageType.String:
                                targetParam.Set(sourceParam.AsString());
                                break;
                            case StorageType.Double:
                                targetParam.Set(sourceParam.AsDouble());
                                break;
                            case StorageType.Integer:
                                targetParam.Set(sourceParam.AsInteger());
                                break;
                            case StorageType.ElementId:
                                targetParam.Set(sourceParam.AsElementId());
                                break;
                        }
                    }
                    catch { /* 忽略個別參數設定失敗 */ }
                }
            }
        }

        #endregion

    }
}
