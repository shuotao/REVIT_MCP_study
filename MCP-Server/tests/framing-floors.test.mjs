import assert from "node:assert/strict";
import test from "node:test";
import {registerRevitTools} from "../build/tools/index.js";
import {executeRevitTool} from "../build/tools/revit-tools.js";
test("framing floors expose type selection and safe preview defaults",()=>{
 const old=process.env.MCP_PROFILE;
 try {for(const p of ["full","structural"]){
 process.env.MCP_PROFILE=p;const all=registerRevitTools();
 const t=all.filter(t=>t.name==="create_floors_from_framing_regions");assert.equal(t.length,1);
 assert.deepEqual(t[0].inputSchema.required,["levelId","floorTypeId"]);
 assert.equal(t[0].inputSchema.properties.dryRun.default,true);
 assert.equal(t[0].inputSchema.properties.apply.default,false);
 assert.equal(t[0].annotations.readOnlyHint,false);
 assert.equal(all.find(t=>t.name==="list_floor_types").annotations.readOnlyHint,true);
 }}finally{if(old===undefined)delete process.env.MCP_PROFILE;else process.env.MCP_PROFILE=old;}
});
test("floor bridge preserves selected type, region keys and preview token",async()=>{
 const args={levelId:1,floorTypeId:2,regionKeys:["region"],previewToken:"token",apply:true,dryRun:false};
 const result=await executeRevitTool("create_floors_from_framing_regions",args,{async sendCommand(name,payload,timeout){
 assert.equal(name,"create_floors_from_framing_regions");assert.deepEqual(payload,args);assert.equal(timeout,120000);
 return {success:true,data:{Committed:false,RolledBack:true,Created:0,Failed:1}};
 }});
 assert.equal(result.RolledBack,true);assert.equal(result.Created,0);
});

test("floor bridge exposes partial completion and source exclusions",async()=>{
 const result=await executeRevitTool("create_floors_from_framing_regions",{levelId:1,floorTypeId:2},{
 async sendCommand(){return {success:true,data:{Success:false,PartialSuccess:true,Committed:true,Created:2,Failed:1,
 ExcludedItems:[{ElementId:9,Reason:"SourceGeometryUnavailable"}],
 Results:[{Status:"Created",ElementId:10},{Status:"Failed",Reason:"RegionNotCommitted"}]}};}
 });
 assert.equal(result.Created,2);assert.equal(result.PartialSuccess,true);
 assert.equal(result.ExcludedItems[0].ElementId,9);
 assert.equal(result.Results[1].Reason,"RegionNotCommitted");
});
