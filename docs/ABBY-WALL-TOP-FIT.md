# Wall top fitted to floor

join_wall_tops accepts wallTopToFloor with wallId, floorId and optional extendFloorPlane. This mutually exclusive mode trims a detached straight vertical basic wall with no inserts or existing profile. It preserves wall bottom, identity and location. This is a static profile, not dynamic attachment.

The nearest unambiguous floor plane is used. Transverse slopes and conflicting actual samples fail. Extrapolation requires explicit extendFloorPlane and each missing sample must be within 600 mm of the face edge. New top must remain within existing wall height.

Preview rolls back the entire TransactionGroup. Apply requires a fresh token and explicit dryRun=false/apply=true. Four sketch edges are replaced inside SketchEditScope, followed by 205 surface samples within 1 mm and axis/bottom checks. Runtime preview is required after deployment.

Wall-end joins may remove part of the rectangular envelope. Verification samples only locations where both original wall surfaces exist, requires at least 100 valid samples and coverage reaching the first and last 5% of wall length. Missing original geometry is not extrapolated as wall material.

# 梁頂最高／最低兩點模式

亦支援 `wallTopToBeams.targetSurface="bottom"`：從朝下的梁底平面計算下包絡，再取最高、最低兩點形成牆頂斜線。預設 `top` 保持既有行為。結果回報 `TargetSurface`，預覽 token 也包含表面選擇，不能跨模式套用。

鋼梁內側圓角可略過，但須以實際曲面射線交點驗證平面包絡，誤差超過 1 mm 拒絕；尚不支援以曲面作為主要梁底／梁頂的斷面。

`join_wall_tops.wallTopToBeams` 接受 `wallId` 與 `beamIds`（1–50 支）。
以牆實際厚度的中心垂直面截取梁頂平面，計算其上包絡的最高／最低點，連成一條直線並延伸至牆兩端；等高區採實際覆蓋區內最接近中點的位置。只形成單一縱向斜牆頂，不逐面貼附。

保留原牆 ID、類型、厚度、位置、方向及底部貼附。修改前後比較實際牆底與牆頂，容許誤差 1 mm；不解除牆底貼附。既有編輯輪廓、洞口、頂部貼附及曲面梁暫不支援。預期牆頂若低於現有牆底則拒絕。

先以 `dryRun:true, apply:false` 試作並回滾，取得 `previewToken`。正式套用須 `dryRun:false, apply:true` 與相同輸入／最新 token。全部操作包在 TransactionGroup，提交後驗證不符整組回滾。此為靜態輪廓，梁後續變動不會自動帶動牆。

