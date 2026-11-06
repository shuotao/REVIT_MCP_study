import assert from 'node:assert/strict';
import test from 'node:test';
import {executeRevitTool} from '../build/tools/revit-tools.js';
test('floor CAD schedule reaches Revit with text-group values and rules intact',async()=>{
 const args={levelId:1,floorTypeId:2,dryRun:true,apply:false,cadSchedule:{importInstanceUniqueId:'cad',textLayerName:'slab',cadUnits:'cm',pythonExecutable:'python',floorValue:'B1F',rules:[{pattern:'S*',thicknessMm:200},{pattern:'S2',thicknessMm:250}]}};
 const result=await executeRevitTool('create_floors_from_framing_regions',args,{async sendCommand(name,payload){assert.equal(name,'create_floors_from_framing_regions');assert.deepEqual(payload,args);return {success:true,data:{Results:[{Number:'S2',FloorValue:'B1F',Status:'Skipped',Reason:'OtherSlabThickness'}]}};}});
 assert.equal(result.Results[0].Reason,'OtherSlabThickness');assert.equal(result.Results[0].Number,'S2');
});
