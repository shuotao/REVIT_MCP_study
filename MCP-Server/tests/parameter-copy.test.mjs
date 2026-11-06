import assert from "node:assert/strict";
import test from "node:test";
import { registerRevitTools } from "../build/tools/index.js";
import { executeRevitTool } from "../build/tools/revit-tools.js";

test("parameter copy is discoverable once in every supported profile with safe defaults", () => {
    const previous = process.env.MCP_PROFILE;
    try {
        for (const profile of ["full", "architect", "structural", "mep", "fire-safety"]) {
            process.env.MCP_PROFILE = profile;
            const matches = registerRevitTools().filter(t => t.name === "copy_parameter_by_group");
            assert.equal(matches.length, 1);
            assert.equal(matches[0].annotations.readOnlyHint, false);
            const p = matches[0].inputSchema.properties;
            assert.equal(p.sourceParameterName.default, "頂部約束");
            assert.equal(p.targetParameterName.default, "牆頂至");
            assert.equal(p.sourceGroup.default, "約束");
            assert.equal(p.targetGroup.default, "文字");
            assert.equal(p.valueTransform.default, "afterColon");
            assert.equal(p.dryRun.default, true);
            assert.equal(p.apply.default, false);
            assert.equal(p.overwrite.default, true);
            assert.equal(p.scope.default, "project");
            assert.deepEqual(p.categories.default, ["Walls"]);
        }
    } finally {
        if (previous === undefined) delete process.env.MCP_PROFILE;
        else process.env.MCP_PROFILE = previous;
    }
});

test("bridge preserves omitted defaults and all explicit apply/dryRun combinations", async () => {
    for (const args of [{}, ...[true, false].flatMap(apply =>
        [true, false].map(dryRun => ({ apply, dryRun, sourceGroup: "約束", targetGroup: "文字" })))]) {
        let calls = 0;
        const data = { Success: false, RolledBack: true, Updated: 0, Planned: 2, Failed: 2,
            Results: [{ OldValue: "A", NewValue: "B", Status: "Failed" }] };
        const client = { async sendCommand(command, payload, timeoutMs) {
            calls++;
            assert.equal(command, "copy_parameter_by_group");
            assert.equal(timeoutMs, 120000);
            assert.deepEqual(payload, args);
            return { success: true, data };
        } };
        assert.deepEqual(await executeRevitTool("copy_parameter_by_group", args, client), data);
        assert.equal(calls, 1);
    }
});

test("bridge surfaces command validation errors", async () => {
    const client = { async sendCommand() {
        return { success: false, error: "Only scope=project is supported." };
    } };
    await assert.rejects(executeRevitTool("copy_parameter_by_group", { scope: "invalid" }, client),
        /Only scope=project/);
});
