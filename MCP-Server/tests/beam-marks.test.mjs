import assert from "node:assert/strict";
import test from "node:test";
import { registerRevitTools } from "../build/tools/index.js";
import { executeRevitTool } from "../build/tools/revit-tools.js";
test("Beam marks registered with explicit scope and safe defaults",()=>{
 const saved=process.env.MCP_PROFILE;
 try {
 for(const profile of ["full","structural"]) {
 process.env.MCP_PROFILE=profile;
 const tools=registerRevitTools().filter(t=>t.name==="fill_beam_marks_from_dwg");
 assert.equal(tools.length,1);assert.equal(tools[0].annotations.readOnlyHint,false);
 const s=tools[0].inputSchema;assert.deepEqual(s.required,["beamIds","importInstanceUniqueId","textLayerName","cadUnits"]);
 assert.equal(s.properties.dryRun.default,true);assert.equal(s.properties.apply.default,false);
 }
 } finally {if(saved===undefined)delete process.env.MCP_PROFILE;else process.env.MCP_PROFILE=saved;}
});
test("Beam mark bridge preserves preview arguments and allows enough reader time",async()=>{
 const args={beamIds:[1],importInstanceUniqueId:"cad",textLayerName:"0S-小梁編號",cadUnits:"cm"};
 const result=await executeRevitTool("fill_beam_marks_from_dwg",args,{async sendCommand(name,payload,timeout){
 assert.equal(name,"fill_beam_marks_from_dwg");assert.deepEqual(payload,args);assert.equal(timeout,120000);
 return {success:true,data:{Updated:0,Skipped:1,ByReason:[{Reason:"AmbiguousLabels",Count:1}]}};
 }});
 assert.equal(result.Updated,0);assert.equal(result.ByReason[0].Reason,"AmbiguousLabels");
});
