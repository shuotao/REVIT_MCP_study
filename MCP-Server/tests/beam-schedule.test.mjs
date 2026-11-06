import assert from "node:assert/strict";
import test from "node:test";
import {registerRevitTools} from "../build/tools/index.js";
import {executeRevitTool} from "../build/tools/revit-tools.js";
test("numeric beam schedule exposed as opt-in with safe flags",()=>{
 const tool=registerRevitTools().find(t=>t.name==="create_beams_from_dwg"),p=tool.inputSchema.properties;
 assert.deepEqual(p.mappingMode.enum,["legacy","numericSchedule","elevationSteel"]);
 assert.equal(p.dryRun.default,true);assert.equal(p.apply.default,false);
});
test("beam schedule bridge keeps case-sensitive arguments and preview flags",async()=>{
 const args={mappingMode:"numericSchedule",familyName:"混凝土梁",layerName:"梁線",textLayerNameX:"梁號",cadUnits:"cm",dryRun:true,apply:false};
 await executeRevitTool("create_beams_from_dwg",args,{async sendCommand(n,a,t){
 assert.deepEqual(a,args);assert.equal(t,120000);return {success:true,data:{Created:0}};
 }});
});
