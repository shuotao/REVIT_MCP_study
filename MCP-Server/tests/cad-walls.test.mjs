import assert from 'node:assert/strict';
import test from 'node:test';
import {registerRevitTools} from '../build/tools/index.js';
import {executeRevitTool} from '../build/tools/revit-tools.js';
test('CAD walls require explicit levels and safe preview',()=>{const t=registerRevitTools().find(t=>t.name==='create_walls_from_dwg');assert.deepEqual(t.inputSchema.required,['baseLevelId','topLevelId']);assert.equal(t.inputSchema.properties.dryRun.default,true);assert.equal(t.inputSchema.properties.apply.default,false);assert.equal(t.annotations.readOnlyHint,false);});
test('CAD wall bridge retains constraints and rollback outcome',async()=>{const args={baseLevelId:1,topLevelId:2,layerName:'WALL',previewToken:'x',dryRun:false,apply:true};const r=await executeRevitTool('create_walls_from_dwg',args,{async sendCommand(n,p,t){assert.equal(n,'create_walls_from_dwg');assert.deepEqual(p,args);assert.equal(t,120000);return {success:true,data:{Created:0,RolledBack:true}};}});assert.equal(r.Created,0);assert.equal(r.RolledBack,true);});
