import assert from "node:assert/strict";
import test from "node:test";
import { registerRevitTools } from "../build/tools/index.js";
import { executeRevitTool } from "../build/tools/revit-tools.js";

const name = "create_structural_beam_types";
test("beam type tool is registered once in full and structural profiles", () => {
    const previous = process.env.MCP_PROFILE;
    try {
        for (const profile of ["full", "structural"]) {
            process.env.MCP_PROFILE = profile;
            const tools = registerRevitTools().filter(tool => tool.name === name);
            assert.equal(tools.length, 1);
            assert.equal(tools[0].inputSchema.properties.familyName.default, "混凝土樑-矩形");
            assert.deepEqual(tools[0].inputSchema.required, ["types"]);
            const items = tools[0].inputSchema.properties.types.items;
            assert.deepEqual(items.required, ["typeName", "widthMm", "heightMm"]);
            assert.equal(items.properties.widthMm.exclusiveMinimum, 0);
            assert.equal(items.properties.heightMm.exclusiveMinimum, 0);
        }
    } finally {
        if (previous === undefined) delete process.env.MCP_PROFILE;
        else process.env.MCP_PROFILE = previous;
    }
});
test("bridge forwards the entire batch and preserves rollback details", async () => {
    const args = {
        familyName: "混凝土樑-矩形",
        baseTypeName: "base",
        types: [{ typeName: "B1", widthMm: 300, heightMm: 600 }],
    };
    const data = {
        Success: false, Created: [], Skipped: [],
        Failed: [{ TypeName: "B1", Reason: "Batch not committed" }],
    };
    let calls = 0;
    const client = {
        async sendCommand(command, payload) {
            calls++;
            assert.equal(command, name);
            assert.deepEqual(payload, args);
            return { success: true, data };
        },
    };
    assert.deepEqual(await executeRevitTool(name, args, client), data);
    assert.equal(calls, 1);
});
