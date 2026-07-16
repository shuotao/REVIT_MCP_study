# HANDOFF — PR #86 Windows 建置測試任務

> 這條分支 `salvage/reclaim-contributor-impl` 收攏了被 domain-only `check-pr` 白名單擋在 fork 內的貢獻者實作。
> Mac 端只能做靜態檢查,**C#/TS 的實際建置驗證要在這台 Windows + Revit SDK 完成**。
> 完成後把 PR #86 由 draft 轉正、merge,即可關閉被取代的 #79 / #81 / #82 / #85。
> **本檔為建置指引,merge 後可刪除。** 完整說明見 PR #86 描述。

## 拉取

```bash
git fetch origin salvage/reclaim-contributor-impl
git checkout salvage/reclaim-contributor-impl
```

## 建置步驟(依序)

### 1. 補 dispatcher case
把下列 16 條 case 加進 `MCP/Core/CommandExecutor.cs` 的 `switch (request.CommandName.ToLower())`
(刻意未自動改,以免蓋掉另一 fork 的既有 case;以下由各 fork dispatcher 原樣擷取,可直接貼上):

```csharp
// --- Jacky820507 (#79/#81/#82) ---
case "calculate_exterior_wall_scaffold_perimeter": result = CalculateExteriorWallScaffoldPerimeter(parameters); break;
case "calculate_room_scaffold_perimeters":         result = CalculateRoomScaffoldPerimeters(parameters); break;
case "calculate_selected_detail_line_perimeter":   result = CalculateSelectedDetailLinePerimeter(parameters); break;
case "analyze_tall_partition_rooms":               result = AnalyzeTallPartitionRooms(parameters); break;
case "duplicate_views_with_detailing":             result = DuplicateViewsWithDetailing(parameters); break;
case "create_room_filled_regions":                 result = CreateRoomFilledRegions(parameters); break;
case "get_room_door_counts":                       result = GetRoomDoorCounts(parameters); break;
case "get_room_window_counts":                     result = GetRoomWindowCounts(parameters); break;
case "auto_convert_rotated_viewport_patterns":     result = AutoConvertRotatedViewportPatterns(); break;
case "batch_create_rc_filled_region":              result = BatchCreateRCFilledRegions(parameters); break;
case "convert_drafting_to_model_pattern":          result = ConvertDraftingToModelPattern(); break;
case "create_rc_filled_region":                    result = CreateRCFilledRegion(parameters); break;
case "sync_ifc_structural_to_native":              result = SyncIfcStructuralToNative(parameters); break;

// --- 916kevin-gif 林孟毅 (#85) 帷幕立面(除 main 既有 3 個 curtain case 外新增)---
case "create_curtain_wall_elevations":             result = CreateCurtainWallElevations(parameters); break;
case "diagnose_curtain_wall_elevation_direction":  result = DiagnoseCurtainWallElevationDirection(parameters); break;
case "diagnose_curtain_wall_elevation_directions": result = DiagnoseCurtainWallElevationDirections(parameters); break;
```

### 2. 建置 C# addin
```powershell
dotnet build -c Release.R24 MCP\RevitMCP.csproj
# 視需要對 R22–R26 各版建置
```
⚠️ **已知風險(只有 build 會浮現)**:這些新 partial class 檔可能引用了各 fork `CommandExecutor.cs` 內、但 main 沒有的私有 helper / 欄位。若 build 報缺方法,需一併從對應 fork 補入該 helper。

### 3. 建置 MCP-Server
```powershell
cd MCP-Server
npm install
npm run build
# 確認 registerRevitTools() 回報 159 tools(MCP_PROFILE=full)
```

### 4. 決定空 catch 處理(專案規範是移除/補處理)
```
MCP/Core/Commands/CommandExecutor.RoomFilledRegions.cs:171   catch { }
MCP/Core/Commands/CommandExecutor.RoomDoorCounts.cs:245      catch { }
MCP/Core/Commands/CommandExecutor.RoomDoorCounts.cs:259      catch { }
MCP/Core/Commands/CommandExecutor.RoomWindowCounts.cs:245    catch { }
MCP/Core/Commands/CommandExecutor.RoomWindowCounts.cs:259    catch { }
MCP/Core/Commands/CommandExecutor.FillPatterns.cs:588        catch { }
```

### 5. `MCP-Server/scratch/batch_rc_filled.cjs`(#79 測試腳本)
因 `.gitignore` 排除 `MCP-Server/scratch/`,此本機測試腳本未入庫;如需保留請自 Jacky fork 取用或移出 scratch 目錄。

## 靜態檢查(Mac 已完成)
- `.IntegerValue`:0 hit ✅(符合 Revit 2022–2026 需 `IdType` / `GetIdValue()`)
- bin/obj/version-csproj/addin 夾帶:無 ✅
- 編碼:全部 44 檔 valid UTF-8、0 U+FFFD、0 mojibake ✅

## 完成後
1. build 全綠 + `npm run build` 回報 159 tools。
2. PR #86 由 draft 轉正 → merge 到 main。
3. 關閉被取代的 **#79 / #81 / #82 / #85**。
4. 回報給 Mac 端 → 進行 **MCP Registry 發佈**(server.json / npm token / GitHub Actions 均已就緒)。
