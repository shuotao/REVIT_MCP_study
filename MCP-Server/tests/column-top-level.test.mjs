import assert from 'node:assert/strict';
import test from 'node:test';
import {registerRevitTools} from '../build/tools/index.js';
import {executeRevitTool} from '../build/tools/revit-tools.js';
test('DWG columns support optional explicit top level without changing required args',()=>{const t=registerRevitTools().find(t=>t.name==='create_columns_from_dwg');assert.deepEqual(t.inputSchema.required,['layerName']);assert.equal(t.inputSchema.properties.topLevelId.type,'integer');assert.equal(t.inputSchema.properties.topLevelId.minimum,1);assert.equal(t.inputSchema.properties.topLevelId.default,undefined);});
test('column bridge preserves explicit top level and committed IDs',async()=>{const args={layerName:'column',familyName:'RC',topLevelId:42};const r=await executeRevitTool('create_columns_from_dwg',args,{async sendCommand(n,p){assert.equal(n,'create_columns_from_dwg');assert.deepEqual(p,args);return {success:true,data:{topLevelId:42,createdElementIds:[91,92]}};}});assert.equal(r.topLevelId,42);assert.deepEqual(r.createdElementIds,[91,92]);});
