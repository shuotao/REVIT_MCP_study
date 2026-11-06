// ============================================================================
// loop-up — Workflow Script 範本（可重複使用的參數化控制流）
//
// 這段不是要被實際執行的程式——是 orchestrator 依此邏輯呼叫 `Agent` / `SendMessage` 工具的規格，用來避免每次呼叫這支 skill 都要
// 重新編排一次控制流。標了 `TASK-SPECIFIC` 的部分每次任務替換；標了 `FIXED SKELETON` 的部分不要動。
// ============================================================================

// ============================================================================
// loop-up workflow template
// TASK-SPECIFIC：每次任務替換 ｜ FIXED SKELETON：固定骨架，不要動
// ============================================================================

// ---------- TASK-SPECIFIC：每次任務替換 ----------
export const meta = {
  skill: "loop-up",
  runId: "<替換：例如 2026-08-10T1530>",
  phases: ["gate", "plan", "upgrade", "verify", "eval"], // 對應「啟動前置門檻」與「三階段執行目標」
} as const;

interface StageBrief {
  stageId: string;                 // 替換
  goal: string;                    // 替換：這個 Stage 完成時世界應變成什麼樣子
  acceptanceCriteria: Check[];     // 替換：inspector 用什麼指令/腳本判定
  targetFiles: string[];           // 替換：預期會動到的檔案（給 observer 當範圍提示，不是限制）
  requiresNegativeTest: boolean;   // 替換：true = 這個 Stage 觸碰 validator/gate 本身
}

interface Check {
  checkId: string;      // 替換
  command: string;      // 替換：inspector 實際要跑的指令/腳本
  passCondition: string; // 替換：什麼輸出/結束碼算 PASS
}

// ---------- FIXED SKELETON：固定骨架 ----------
const LIMITS = { maxTierA: 3, maxAdvisorPerStage: 1, maxAdvisorPerRun: 3 };

interface Finding {
  severity: "blocker" | "major" | "minor";
  where: string;       // checkId 或 file:line
  problem: string;     // 觀察到什麼問題
  evidence: string;    // 唯讀查證得到的指令輸出/diff 摘錄
  promptFix: string;   // 給下一輪 implementer 的具體修法（不是「請修正上面的問題」這種空話）
}

interface InspectorOutput {
  pass: boolean;
  positiveOk: boolean;    // Stage 自身驗收條件在期望情境下通過（GREEN）
  negativeOk: boolean;    // requiresNegativeTest=false 時預設 true；為 true 時需附 RED 證據
  noRegression: boolean;  // 既有已過的檢查（前面 Stage／專案既有測試套件）仍然過
  observerRecordVerified: boolean; // observer 稽核底稿是否通過查核（六項查核見 SKILL.md「Inspection Gate」）
  observerRecordPath: string;      // 底稿實際路徑
  observerRecordEntries: number;   // 底稿總筆數
  findings: Finding[];
  // pass 判定規則（固定，不是 inspector 主觀決定）：
  // pass = positiveOk && negativeOk && noRegression && observerRecordVerified
  //        && findings.every(f => f.severity !== "blocker")
}

async function runStage(stage: StageBrief, runAdvisorCount: { n: number }) {
  let brief = stage;
  let tier: "A" | "B" = "A";
  let attempt = 0;
  let advisorUsedThisStage = false;

  while (true) {
    attempt++;
    const implOut = await implement(brief);                     // Sonnet
    const obs = await observe(implOut, stage.targetFiles);       // Sonnet, quote-only —— 見角色表
    const insp = await inspect(stage.acceptanceCriteria, obs);   // Fable/Opus
    appendObserverLog({ stageId: stage.stageId, attempt, tier, implOut, obs, insp }); // → Observer 記錄格式

    // insp.pass 已固定納入 observerRecordVerified（見上方 InspectorOutput 定義）——
    // 稽核底稿沒查過或查核未過，Stage 一律不算 PASS，不管其餘三項是否都過。
    if (insp.pass) return { status: "PASS", stageId: stage.stageId, attempt, tier };

    if (attempt < LIMITS.maxTierA) {
      brief = buildCorrectionPreface(insp, brief); // prompt-first，見下
      continue;
    }
    if (!advisorUsedThisStage && runAdvisorCount.n < LIMITS.maxAdvisorPerRun) {
      const advised = await advise({ stage, history: getHistory(stage.stageId) }); // Fable, Plan 型
      advisorUsedThisStage = true;
      runAdvisorCount.n++;
      tier = "B";
      attempt = 0;
      brief = advised.revisedBrief; // 或 advised.revisedStagePlan（見 Sub-Workflows §5）
      continue;
    }
    // Tier A 與（若已用過）Tier B 都滿載仍 FAIL，或整個 run 的 advisor 額度已用完
    return { status: "BLOCKED", stageId: stage.stageId, attempt, tier, dossier: getHistory(stage.stageId) };
  }
}

// prompt-first 回饋組裝：findings[].promptFix → 下一輪 implementer prompt 的前綴區塊
// implementer 永遠不需要自行揣摩 inspector 想要什麼
function buildCorrectionPreface(insp: InspectorOutput, prevBrief: StageBrief): StageBrief {
  const ordered = [...insp.findings].sort((a, b) => severityRank(b.severity) - severityRank(a.severity));
  const preface = ordered
    .map((f, i) => `${i + 1}. [${f.severity}] ${f.where}：${f.problem}\n   證據：${f.evidence}\n   修法：${f.promptFix}`)
    .join("\n");
  return {
    ...prevBrief,
    goal:
      `## Correction Preface（依 inspector findings 自動組裝，非 implementer 自行揣摩）\n${preface}\n\n` +
      `請先依序解決以上項目，再繼續處理下方原始目標。不要動無關檔案。不要為了通過檢查而弱化或跳過檢查本身。\n\n---\n${prevBrief.goal}`,
  };
}
