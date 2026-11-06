import assert from 'node:assert/strict';
import test from 'node:test';
import {executeRevitTool} from '../build/tools/revit-tools.js';
test('beam elevation and text-number contract survives MCP dispatch',async()=>{
 const args={mappingMode:'numericSchedule',layerName:'lines',familyName:'RC',cadUnits:'cm',uncertainElevationLevelId:11,defaultLevelId:10,parenthesizedLevelId:11,parenthesizedOffsetUnits:'cm',elevationLayerPairs:[{numberLayer:'main',elevationLayer:'main-elevation'},{numberLayer:'secondary',elevationLayer:'secondary-elevation'}],writeTextNumber:true,dryRun:false,apply:true,previewToken:'fresh'};
 const result=await executeRevitTool('create_beams_from_dwg',args,{async sendCommand(name,payload){assert.equal(name,'create_beams_from_dwg');assert.deepEqual(payload,args);return {success:true,data:{Committed:false,RolledBack:true,Created:0,Error:'Text/編號 readback failed.'}};}});
 assert.equal(result.RolledBack,true);assert.equal(result.Created,0);
});
