# Toposolid 樓板投影整地試跑 Implementation Plan

> **供代理工作者使用：** 必須使用 `superpowers:subagent-driven-development`（建議）或 `superpowers:executing-plans`，依工作逐項實作；所有步驟以核取方塊追蹤。

**目標：** 在 Revit 2024 新增 `grade_toposolid_to_floors` MCP 工具，將設計地形在兩片樓板投影範圍內貼齊樓板底面，並以 Revit 內建參數驗證 CUT、FILL 與淨土方。

**架構：** 純幾何層負責投影多邊形、點位判斷與重疊衝突，不依賴 Revit API；Revit 配接層負責擷取樓板底面、複製 Toposolid、設定階段及操作 `SlabShapeEditor`。命令層以 `TransactionGroup` 統籌，只有在 Revit 重新生成後能讀到有效 CUT/FILL 結果才提交，否則完整回滾。

**技術棧：** C#／.NET Framework 4.8、Autodesk Revit 2024 API、Nice3point.Revit.Sdk 6.1、NUnit 3、TypeScript 5.7、Model Context Protocol SDK。

## 全域限制

- 僅支援 Revit 2024 的 `Toposolid` 與 `Floor`。
- 新增的 Revit API 程式以 `#if REVIT2024_OR_GREATER` 隔離，Revit 2022／2023 組態仍須可建置。
- 本次只支援 `mode: "footprint_only"` 與 `targetFace: "bottom"`。
- 不實作 Offset、坡度、指定邊界或 `IUpdater`。
- 不使用反射呼叫未公開 Revit API，也不以 UI 模擬取代 API。
- 不刪除控制樓板；API 不支援正式 CUT/FILL 關係時必須回滾。
- 所有錯誤訊息、程式碼註解、文件與 commit 訊息使用繁體中文。
- Revit 執行中不可覆寫已載入 DLL；部署前由使用者關閉 Revit，禁止強制終止程序。

---

## 檔案結構

- 新增 `MCP/Core/Grading/GradingModels.cs`：純資料模型、工具輸入驗證與固定關聯 schema 識別碼。
- 新增 `MCP/Core/Grading/Polygon2D.cs`：點在多邊形內、線段相交與樓板投影衝突判斷。
- 新增 `MCP/Core/Grading/RevitToposolidGradingAdapter.cs`：Revit 元素、幾何、階段與 Shape Editor 配接。
- 新增 `MCP/Core/Commands/CommandExecutor.ToposolidGrading.cs`：交易、回滾與回應組裝。
- 修改 `MCP/Core/CommandExecutor.cs`：註冊 Revit 命令分派。
- 新增 `MCP.Tests/RevitMCP.Tests.csproj`：可在 Revit 外執行的 NUnit 測試專案。
- 新增 `MCP.Tests/Grading/GradingRequestTests.cs`：工具輸入與衝突規則測試。
- 新增 `MCP.Tests/Grading/Polygon2DTests.cs`：純幾何測試。
- 新增 `MCP-Server/src/tools/grading-tools.ts`：MCP 工具 schema。
- 新增 `MCP-Server/src/tools/grading-tools.test.ts`：使用 Node 內建測試驗證 schema。
- 修改 `MCP-Server/src/tools/revit-tools.ts`：各 profile 註冊整地工具。
- 修改 `MCP-Server/package.json`：加入 schema 測試命令。

---

### Task 1：建立純資料模型與測試基礎

**檔案：**

- 建立：`MCP.Tests/RevitMCP.Tests.csproj`
- 建立：`MCP/Core/Grading/GradingModels.cs`
- 建立：`MCP.Tests/Grading/GradingRequestTests.cs`

**介面：**

- 產出：`GradingRequest.Validate()`、`Point2D`、`FloorFootprint`、`GradingResult`。
- 後續工作只透過上述型別交換資料，不直接傳遞匿名物件。

- [ ] **Step 1：建立測試專案與失敗測試**

`MCP.Tests/RevitMCP.Tests.csproj` 使用以下內容：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\MCP\Core\Grading\GradingModels.cs" Link="Core\Grading\GradingModels.cs" />
    <Compile Include="..\MCP\Core\Grading\Polygon2D.cs" Link="Core\Grading\Polygon2D.cs" Condition="Exists('..\MCP\Core\Grading\Polygon2D.cs')" />
  </ItemGroup>
</Project>
```

`GradingRequestTests.cs` 先加入四個測試：接受 `footprint_only/bottom`、拒絕空樓板清單、拒絕其他模式、拒絕 `updateExisting=true`。

```csharp
using NUnit.Framework;
using RevitMCP.Core.Grading;

namespace RevitMCP.Tests.Grading
{
    [TestFixture]
    public class GradingRequestTests
    {
        [Test]
        public void Validate_合法試跑參數_不拋出例外()
        {
            Assert.DoesNotThrow(() => new GradingRequest
            {
                ToposolidId = 6278563,
                FloorIds = new[] { 7512796L, 7512816L },
                Mode = "footprint_only",
                TargetFace = "bottom"
            }.Validate());
        }

        [Test]
        public void Validate_樓板清單為空_回報繁體中文錯誤()
        {
            var error = Assert.Throws<System.ArgumentException>(() => new GradingRequest
            {
                ToposolidId = 6278563,
                FloorIds = new long[0],
                Mode = "footprint_only",
                TargetFace = "bottom"
            }.Validate());
            StringAssert.Contains("至少一片樓板", error.Message);
        }

        [Test]
        public void Validate_非本次模式_拒絕執行()
        {
            var error = Assert.Throws<System.ArgumentException>(() => new GradingRequest
            {
                ToposolidId = 6278563,
                FloorIds = new[] { 7512796L },
                Mode = "slope_transition",
                TargetFace = "bottom"
            }.Validate());
            StringAssert.Contains("footprint_only", error.Message);
        }

        [Test]
        public void Validate_要求更新既有結果_拒絕執行()
        {
            var error = Assert.Throws<System.ArgumentException>(() => new GradingRequest
            {
                ToposolidId = 6278563,
                FloorIds = new[] { 7512796L },
                Mode = "footprint_only",
                TargetFace = "bottom",
                UpdateExisting = true
            }.Validate());
            StringAssert.Contains("updateExisting=true", error.Message);
        }
    }
}
```

- [ ] **Step 2：執行測試並確認 RED**

執行：

```powershell
dotnet test MCP.Tests/RevitMCP.Tests.csproj --filter GradingRequestTests
```

預期：FAIL，原因為 `GradingRequest` 與命名空間尚不存在。

- [ ] **Step 3：建立最小資料模型**

`GradingModels.cs` 必須定義：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCP.Core.Grading
{
    public struct Point2D
    {
        public Point2D(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
    }

    public sealed class GradingRequest
    {
        public long ToposolidId { get; set; }
        public IReadOnlyList<long> FloorIds { get; set; }
        public string Mode { get; set; }
        public string TargetFace { get; set; }
        public bool AllowPhaseSetup { get; set; }
        public bool UpdateExisting { get; set; }

        public void Validate()
        {
            if (ToposolidId <= 0) throw new ArgumentException("地形 ID 必須大於 0。");
            if (FloorIds == null || FloorIds.Count == 0) throw new ArgumentException("至少一片樓板才能執行整地。");
            if (FloorIds.Any(id => id <= 0)) throw new ArgumentException("樓板 ID 必須大於 0。");
            if (!string.Equals(Mode, "footprint_only", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("本次試跑僅支援 footprint_only。");
            if (!string.Equals(TargetFace, "bottom", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("本次試跑僅支援樓板底面 bottom。");
            if (UpdateExisting)
                throw new ArgumentException("本次試跑尚未支援 updateExisting=true。");
        }
    }

    public sealed class FloorFootprint
    {
        public long FloorId { get; set; }
        public IReadOnlyList<Point2D> OuterLoop { get; set; }
        public Func<double, double, double> BottomElevationAt { get; set; }
    }

    public sealed class GradingResult
    {
        public long OriginalToposolidId { get; set; }
        public long DesignToposolidId { get; set; }
        public IReadOnlyList<long> FloorIds { get; set; }
        public double CutCubicMeters { get; set; }
        public double FillCubicMeters { get; set; }
        public double NetCubicMeters => FillCubicMeters - CutCubicMeters;
        public int ModifiedPointCount { get; set; }
        public string AssociationId { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }
}
```

此純資料檔不可引用 `Autodesk.Revit.DB.Toposolid`，確保 Revit 2022／2023 組態仍可編譯。

- [ ] **Step 4：執行測試並確認 GREEN**

執行：`dotnet test MCP.Tests/RevitMCP.Tests.csproj --filter GradingRequestTests`

預期：4 個測試通過。

- [ ] **Step 5：提交**

```powershell
git add MCP/Core/Grading/GradingModels.cs MCP.Tests
git commit -m "測試：建立整地請求模型與驗證"
```

---

### Task 2：實作樓板投影幾何與衝突偵測

**檔案：**

- 建立：`MCP/Core/Grading/Polygon2D.cs`
- 建立：`MCP.Tests/Grading/Polygon2DTests.cs`

**介面：**

- 產出：`Polygon2D.Contains(Point2D, double)` 與 `Polygon2D.Overlaps(IReadOnlyList<Point2D>, IReadOnlyList<Point2D>, double)`。
- Revit 配接層提供英尺座標；容差固定使用 `1.0 / 304.8` 英尺（1 mm）。

- [ ] **Step 1：先寫失敗測試**

測試涵蓋：內點、外點、邊界點、相交矩形、不相交矩形、只接觸邊界不視為高程衝突。

```csharp
[Test]
public void Overlaps_兩個矩形有實際面積交集_回傳真()
{
    var a = Rect(0, 0, 10, 10);
    var b = Rect(5, 5, 15, 15);
    Assert.That(Polygon2D.Overlaps(a, b, 0.001), Is.True);
}

[Test]
public void Overlaps_矩形只有共邊_回傳假()
{
    var a = Rect(0, 0, 10, 10);
    var b = Rect(10, 0, 20, 10);
    Assert.That(Polygon2D.Overlaps(a, b, 0.001), Is.False);
}
```

- [ ] **Step 2：執行測試並確認 RED**

執行：`dotnet test MCP.Tests/RevitMCP.Tests.csproj --filter Polygon2DTests`

預期：FAIL，原因為 `Polygon2D` 尚不存在。

- [ ] **Step 3：實作射線法與線段相交**

`Polygon2D` 必須：先以嚴格線段相交判斷穿越，再以一個多邊形的非邊界頂點是否位於另一多邊形內判斷包含；共邊或只碰單點回傳 `false`。不得使用 BoundingBox 當作最終判斷。

- [ ] **Step 4：執行全部純幾何測試**

執行：`dotnet test MCP.Tests/RevitMCP.Tests.csproj`

預期：所有測試通過，無警告。

- [ ] **Step 5：提交**

```powershell
git add MCP/Core/Grading/Polygon2D.cs MCP.Tests/Grading/Polygon2DTests.cs
git commit -m "功能：加入樓板投影衝突判斷"
```

---

### Task 3：建立 Revit 2024 Toposolid 配接層

**檔案：**

- 建立：`MCP/Core/Grading/RevitToposolidGradingAdapter.cs`

**介面：**

- 產出：`ValidateElements`、`ExtractBottomFootprints`、`CreateDesignCopy`、`WriteAssociation`、`ApplyFootprintOnly`、`ReadCutFill`。
- 所有修改方法只能在呼叫端已開啟的交易中執行，不自行提交交易。

- [ ] **Step 1：加入編譯失敗的命令層契約**

先建立配接層類別宣告與以下公開方法簽章，但不加入方法本體，執行建置確認因未實作介面而失敗：

```csharp
internal interface IToposolidGradingAdapter
{
    Toposolid ValidateToposolid(Document doc, long id);
    IReadOnlyList<Floor> ValidateFloors(Document doc, IReadOnlyList<long> ids);
    IReadOnlyList<FloorFootprint> ExtractBottomFootprints(IReadOnlyList<Floor> floors);
    Toposolid CreateDesignCopy(Document doc, Toposolid original, bool allowPhaseSetup);
    string WriteAssociation(Document doc, Toposolid design, long originalId, IReadOnlyList<long> floorIds);
    int ApplyFootprintOnly(Document doc, Toposolid design, IReadOnlyList<FloorFootprint> footprints);
    (double cutCubicMeters, double fillCubicMeters) ReadCutFill(Toposolid design);
}
```

- [ ] **Step 2：執行建置並確認 RED**

執行：`dotnet build MCP/RevitMCP.csproj -c Release.R24`

預期：FAIL，錯誤指出介面方法沒有實作。

- [ ] **Step 3：實作元素與底面擷取**

實作規則：

- 以 `doc.GetElement(new ElementId((int)id))` 驗證 `Toposolid` 與 `Floor`。
- 從樓板 `GeometryElement` 取得所有非空 `Solid`。
- 只接受 `PlanarFace` 且 `FaceNormal.Z < -0.999` 的底面；外圈取面積絕對值最大的 `CurveLoop`。
- 將曲線依最大弦長 300 mm 離散化；每一條直線保留端點，圓弧與樣條依長度分段。
- `BottomElevationAt(x,y)` 使用平面方程式 `z = origin.Z - (nx*(x-origin.X)+ny*(y-origin.Y))/nz`。
- 找不到唯一有效底面時拋出「樓板 ID {id} 沒有可用的單一平面底面」。

- [ ] **Step 4：實作設計地形副本與階段檢查**

`RevitToposolidGradingAdapter.cs` 全檔放在 `#if REVIT2024_OR_GREATER`／`#endif` 內。

採用公開 API：`ElementTransformUtils.CopyElement` 建立完全重合副本；原地形必須位於較早階段，設計副本位於目前階段。若需要改動原地形的 `PHASE_CREATED`／`PHASE_DEMOLISHED` 且 `allowPhaseSetup=false`，拋出清楚錯誤並不修改模型。

副本建立後必須 `doc.Regenerate()`，並確認其為 `Toposolid`、`SketchId` 有效、`GetSlabShapeEditor()` 有效；任一條件不成立即拋出例外。

以固定 GUID `9B4B16C7-4C9C-4B73-9D13-B44F88650D29` 建立 Extensible Storage schema `RevitMCP_ToposolidGrading`，寫入 `AssociationId`（新的 GUID 字串）、`OriginalToposolidId` 與逗號分隔的 `FloorIds`。回應中的 `AssociationId` 必須與儲存值一致；本次不使用它更新既有結果。

- [ ] **Step 5：實作 footprint-only 形狀編輯**

處理順序固定：

1. 從設計 Toposolid 的 `SlabShapeEditor` 讀取既有頂點。
2. 對每個樓板投影邊界，以 Toposolid solid 與垂直 `Line` 的 `Solid.IntersectWithCurve` 找到現況地形頂面 Z。
3. `AddPoints` 加入去重後的樓板邊界點並 `doc.Regenerate()`。
4. 重新取得全部頂點；凡 XY 位於樓板投影內或邊界上者，計算樓板底面目標 Z。
5. 以 `ModifySubElement` 設定相對 Toposolid 參考平面的 offset；參考平面高程以新加入點的 `Position.Z` 與目前 offset 校準，不猜測專案樓層高程。
6. 修改後再次 `doc.Regenerate()`；抽樣所有控制點，誤差大於 2 mm 即拋出例外。

若 API 無法取得可靠的目前 offset，必須拋出「Revit 2024 公開 API 無法可靠設定此 Toposolid 的絕對完成面」，不得提交近似結果。

- [ ] **Step 6：實作 CUT/FILL 驗收閘門**

以 `BuiltInParameter.HOST_AREA_COMPUTED` 之外的實際 CUT/FILL 內建參數查找；先使用 `BuiltInParameter` 列舉的 Revit 2024 定義，若套件沒有公開對應列舉，使用英文與目前 Revit 語系名稱的 `LookupParameter` 僅作讀取。CUT 與 FILL 參數缺失、非數值或幾何已變更但兩者皆為零時拋出例外，使交易回滾。

單位使用 `UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.CubicMeters)`。

- [ ] **Step 7：建置並確認 GREEN**

執行：`dotnet build MCP/RevitMCP.csproj -c Release.R24`

預期：成功建立 `MCP/bin/Release.R24/RevitMCP.dll`，零錯誤。

- [ ] **Step 8：提交**

```powershell
git add MCP/Core/Grading/RevitToposolidGradingAdapter.cs
git commit -m "功能：加入 Revit 2024 地形整地配接層"
```

---

### Task 4：實作交易命令與完整回滾

**檔案：**

- 建立：`MCP/Core/Commands/CommandExecutor.ToposolidGrading.cs`
- 修改：`MCP/Core/CommandExecutor.cs`

**介面：**

- 產出：私有命令 `GradeToposolidToFloors(JObject parameters)`。
- 消費：Task 1 的 `GradingRequest` 與 Task 3 的 `IToposolidGradingAdapter`。

- [ ] **Step 1：先加入命令分派並確認 RED**

在 `ExecuteCommand` switch 加入：

```csharp
case "grade_toposolid_to_floors":
    result = GradeToposolidToFloors(parameters);
    break;
```

上述 case 與 `CommandExecutor.ToposolidGrading.cs` 全檔均以 `#if REVIT2024_OR_GREATER` 隔離。

執行 R24 build，預期因方法不存在而失敗。

- [ ] **Step 2：實作參數解析與交易群組**

命令必須以 `TransactionGroup` 包覆兩個交易：第一個建立設計副本與階段關係；第二個編輯地形並讀取 CUT/FILL。只有兩階段全部完成才 `Assimilate()`；catch 區塊在群組仍啟動時呼叫 `RollBack()`，再保留原始繁體中文錯誤往上拋。

必要解析如下：

```csharp
var request = new GradingRequest
{
    ToposolidId = parameters["toposolidId"]?.Value<long>() ?? 0,
    FloorIds = parameters["floorIds"]?.Values<long>().ToArray() ?? new long[0],
    Mode = parameters["mode"]?.Value<string>() ?? "footprint_only",
    TargetFace = parameters["targetFace"]?.Value<string>() ?? "bottom",
    AllowPhaseSetup = parameters["allowPhaseSetup"]?.Value<bool>() ?? false,
    UpdateExisting = parameters["updateExisting"]?.Value<bool>() ?? false
};
request.Validate();
```

成功回應必須是具名欄位：`OriginalToposolidId`、`DesignToposolidId`、`FloorIds`、`CutCubicMeters`、`FillCubicMeters`、`NetCubicMeters`、`ModifiedPointCount`、`AssociationId`、`Warnings`、`Message`。

- [ ] **Step 3：重新執行純測試與 R24 build**

```powershell
dotnet test MCP.Tests/RevitMCP.Tests.csproj
dotnet build MCP/RevitMCP.csproj -c Release.R24
dotnet build MCP/RevitMCP.csproj -c Release.R23
```

預期：測試全綠；R24 與 R23 build 均為零錯誤。

- [ ] **Step 4：提交**

```powershell
git add MCP/Core/Commands/CommandExecutor.ToposolidGrading.cs MCP/Core/CommandExecutor.cs
git commit -m "功能：新增樓板投影整地交易命令"
```

---

### Task 5：註冊 MCP 工具與 schema 測試

**檔案：**

- 建立：`MCP-Server/src/tools/grading-tools.ts`
- 建立：`MCP-Server/src/tools/grading-tools.test.ts`
- 修改：`MCP-Server/src/tools/revit-tools.ts`
- 修改：`MCP-Server/package.json`

**介面：**

- 產出：MCP 工具名稱 `grade_toposolid_to_floors`。
- schema 僅接受整數 ID、非空 `floorIds`、固定 mode／targetFace，以及明確的 `allowPhaseSetup`／`updateExisting`。

- [ ] **Step 1：建立 schema 失敗測試**

使用 Node 內建 `node:test`，驗證工具存在、required 欄位與 enum：

```typescript
import test from "node:test";
import assert from "node:assert/strict";
import { gradingTools } from "./grading-tools.js";

test("整地工具只暴露本次核准模式", () => {
  const tool = gradingTools.find(item => item.name === "grade_toposolid_to_floors");
  assert.ok(tool);
  assert.deepEqual(tool.inputSchema.required, ["toposolidId", "floorIds"]);
  assert.deepEqual((tool.inputSchema.properties?.mode as { enum: string[] }).enum, ["footprint_only"]);
  assert.deepEqual((tool.inputSchema.properties?.targetFace as { enum: string[] }).enum, ["bottom"]);
});
```

在 `package.json` 加入 `"test": "npm run build && node --test build/tools/*.test.js"`，先執行 `npm test`，預期因 `grading-tools.ts` 不存在而失敗。

- [ ] **Step 2：建立工具 schema**

`grading-tools.ts` 定義單一工具；`allowPhaseSetup` 與 `updateExisting` 均預設 `false`。`allowPhaseSetup` 的 description 必須警告「設為 true 可能調整原地形階段，執行前請先儲存模型」；`updateExisting` 說明本次只接受 `false`。

- [ ] **Step 3：註冊所有相關 profile**

將 `gradingTools` 加入 `full`、`architect` 與 `structural`；不加入 `mep` 或 `fire-safety`。

- [ ] **Step 4：執行測試與 TypeScript build**

```powershell
Set-Location MCP-Server
npm test
Set-Location ..
```

預期：schema 測試通過且 `tsc` 零錯誤。

- [ ] **Step 5：提交**

```powershell
git add MCP-Server/package.json MCP-Server/src/tools/grading-tools.ts MCP-Server/src/tools/grading-tools.test.ts MCP-Server/src/tools/revit-tools.ts
git commit -m "功能：註冊 Toposolid 整地 MCP 工具"
```

---

### Task 6：部署、重新連線與目前模型試跑

**檔案：**

- 驗證：`MCP/bin/Release.R24/RevitMCP.dll`
- 部署：`%APPDATA%/Autodesk/Revit/Addins/2024/RevitMCP/RevitMCP.dll`
- 不修改其他專案檔案。

**介面：**

- 消費：Task 5 暴露的 `grade_toposolid_to_floors`。
- 產出：目前模型中的設計 Toposolid ID 與 CUT/FILL 結果。

- [ ] **Step 1：完成交付前測試**

```powershell
dotnet test MCP.Tests/RevitMCP.Tests.csproj
dotnet build MCP/RevitMCP.csproj -c Release.R24
Set-Location MCP-Server
npm test
Set-Location ..
```

預期：所有命令 exit code 0。

- [ ] **Step 2：記錄 DLL 資訊**

```powershell
Get-Item MCP/bin/Release.R24/RevitMCP.dll | Select-Object FullName,Length,LastWriteTime
```

預期：DLL 存在且時間晚於本次程式修改。

- [ ] **Step 3：請使用者儲存並關閉 Revit**

先回報「需要重新載入外掛；請先儲存模型並關閉 Revit 2024」。不得使用 `Stop-Process`。確認 Revit 已關閉後才部署。

- [ ] **Step 4：部署 DLL 並請使用者重啟模型**

```powershell
$target = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\RevitMCP'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item 'MCP\bin\Release.R24\RevitMCP.dll' $target -Force
Get-Item (Join-Path $target 'RevitMCP.dll') | Select-Object FullName,Length,LastWriteTime
```

使用者重啟目前模型後，以 `get_project_info` 與 `get_selected_elements` 驗證連線及元素仍為 Toposolid `6278563`、Floors `7512796/7512816`；若 ID 改變，以當前選取結果為準。

- [ ] **Step 5：先以禁止階段修改模式試跑**

呼叫：

```json
{
  "toposolidId": 6278563,
  "floorIds": [7512796, 7512816],
  "mode": "footprint_only",
  "targetFace": "bottom",
  "allowPhaseSetup": false,
  "updateExisting": false
}
```

若工具回報需要階段調整，先將訊息與將被修改的階段名稱呈現給使用者；取得明確同意後才以 `allowPhaseSetup: true` 重試。

- [ ] **Step 6：驗證結果或證明安全回滾**

成功條件：

- 回傳新的設計 Toposolid ID。
- 回傳非空 `AssociationId`，且設計地形的 Extensible Storage 可讀到同一值。
- 兩片樓板投影內抽樣點與樓板底面誤差不超過 2 mm。
- 原地形 ID 仍存在，控制樓板未刪除。
- CUT/FILL 參數可讀且至少一項大於 0。
- 將新設計地形與兩片樓板選取並縮放，供使用者視覺檢查。

失敗條件：若 Revit 2024 公開 API 無法建立正式 CUT/FILL 關係，工具回傳限制訊息，模型中不得留下設計副本或部分形狀修改；以 `get_element_info` 驗證原三個元素仍存在。

- [ ] **Step 7：提交最終驗證紀錄**

在成功試跑後，將測試命令與 Revit 回傳結果補入 commit 本文，不新增含專案敏感資料的檔案：

```powershell
git status --short
git log -5 --oneline
```

確認沒有意外加入 Revit 模型、備份檔或工作區既有未追蹤檔案。
