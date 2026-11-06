import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const parameterCopyTools: Tool[] = [{
    name: "copy_parameter_by_group",
    description: "依名稱與 Definition.GetGroupTypeId 精確配對並複製實例參數。必須 apply=true 且 dryRun=false 才寫入。空值、缺少或重複配對、唯讀與不相容參數會跳過。預設覆寫，單一交易失敗全部回復。回傳 Scanned/Planned/Updated/Unchanged/Skipped/Failed、分類統計與原值/新值統計。預設僅掃描 Walls，從約束／頂部約束複製到文字／牆頂至。預設 afterColon 從來源顯示文字取第一個半形或全形冒號後文字並去除前後空白，僅寫入字串目標；沒有冒號或擷取後空值會跳過。raw 模式保留原儲存類型及內部單位。",
    inputSchema: {
        type: "object",
        properties: {
            sourceParameterName: { type: "string", minLength: 1, default: "頂部約束" },
            sourceGroup: { type: "string", minLength: 1, default: "約束", description: "約束/Constraints/PG_CONSTRAINTS、文字/Text/PG_TEXT，或完整 Forge 群組 TypeId。" },
            targetParameterName: { type: "string", minLength: 1, default: "牆頂至" },
            targetGroup: { type: "string", minLength: 1, default: "文字", description: "與 sourceGroup 相同格式。" },
            categories: { type: "array", minItems: 1, uniqueItems: true, items: { type: "string", enum: ["Walls", "StructuralColumns", "StructuralFraming", "Floors", "Doors", "Windows"] }, default: ["Walls"] },
            valueTransform: { type: "string", enum: ["afterColon", "raw"], default: "afterColon", description: "afterColon 取來源 AsString/AsValueString 第一個 : 或 ：後文字，缺少冒號跳過；raw 原值複製，要求相同儲存及資料類型。" },
            scope: { type: "string", enum: ["project"], default: "project", description: "目前文件的實例，不含元素類型或連結文件。" },
            dryRun: { type: "boolean", default: true },
            apply: { type: "boolean", default: false },
            overwrite: { type: "boolean", default: true },
        },
    },
}];
