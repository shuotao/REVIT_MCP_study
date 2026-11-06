// Static contract regression only; does not replace a live Revit geometry/door-host test.
//
// Origin: raychiu/fix/create-wall-level-type (@ray92chiu-png), issue #144.
// Extended here to cover the same "specified-but-not-found is silently substituted"
// defect in the sibling create tools (create_floor / create_door / create_window /
// create_column) and the non-deterministic FindLevel fuzzy match.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { wallTools } from '../build/tools/wall-tools.js';
import { withAnnotations } from '../build/tools/annotations.js';

const tool = name => {
  const t = wallTools.find(x => x.name === name);
  assert.ok(t, `${name} must be registered in wallTools`);
  return t;
};

// ---------------------------------------------------------------- create_wall schema
const schema = tool('create_wall').inputSchema;
assert.deepEqual(schema.required, ['startX', 'startY', 'endX', 'endY']);
for (const key of ['levelId', 'wallTypeId']) {
  assert.equal(schema.properties[key].type, 'integer', `${key} must be an integer ElementId`);
  assert.equal(schema.properties[key].minimum, 1, `${key} must reject 0 / negative ids`);
}
for (const key of ['levelName', 'wallType']) {
  assert.equal(schema.properties[key].type, 'string');
  assert.equal(schema.properties[key].minLength, 1, `${key} must reject the empty string`);
}
assert.equal(schema.properties.height.exclusiveMinimum, 0);
assert.equal(withAnnotations(tool('create_wall')).annotations?.readOnlyHint, false);

// create_wall declares wallType; the C# side must actually read it (issue #144's hidden defect).
const floorSchema = tool('create_floor').inputSchema;
assert.ok(floorSchema.properties.floorType, 'create_floor still declares floorType');
assert.equal(floorSchema.properties.floorType.minLength, 1);

// ---------------------------------------------------------------- C# static guards
const source = readFileSync(new URL('../../MCP/Core/CommandExecutor.cs', import.meta.url), 'utf8');
const between = (from, to, label) => {
  const after = source.split(from);
  assert.equal(after.length, 2, `${label}: expected exactly one '${from}'`);
  const body = after[1].split(to);
  assert.ok(body.length > 1, `${label}: terminator '${to}' not found after '${from}'`);
  return body[0];
};

// --- create_wall
const method = between('private object CreateWall(JObject parameters)', 'private object GetProjectInfo()', 'CreateWall');
assert.ok(!method.includes('FirstOrDefault()'), 'Must not select arbitrary level/type');
assert.ok(method.includes('(doc.ActiveView as ViewPlan)?.GenLevel'));
assert.ok(method.includes('level.Name != levelName'));
assert.ok(method.includes('wallType.Name != wallTypeName'), 'wallType must actually be read, not just declared');
assert.ok(method.includes('wallType.Kind != WallKind.Basic'));
assert.ok(method.includes('double.IsNaN(v) || double.IsInfinity(v)'));
assert.ok(method.includes('height <= 0'));
assert.ok(method.includes('ShortCurveTolerance'));
assert.ok(method.includes('Wall.Create(doc, line, wallType.Id, level.Id, height / 304.8, 0, false, false)'));
assert.ok(method.includes('TransactionStatus.Committed'));
assert.ok(method.includes('WallTypeSource'), 'the response must report which wall type was actually used');
assert.ok(method.includes('LevelSource'), 'the response must report which level was actually used');

// --- FindLevel: deterministic ladder, no bidirectional Contains, fallback is reported
const findLevel = between('private Level FindLevel(Document doc, string levelName, bool useFirstIfNotFound, out bool fellBackToLowest)',
  'private Level FindLevel(Document doc, string levelName, bool useFirstIfNotFound = true)', 'FindLevel');
assert.ok(!findLevel.includes('levelName.Contains(l.Name)'),
  'the bidirectional fuzzy match let "1F" resolve to "B1F" and must not come back');
assert.ok(findLevel.includes('StringComparison.OrdinalIgnoreCase'), 'ladder step 2/3 must be case-insensitive');
assert.ok(findLevel.includes('partial.Count > 1'), 'an ambiguous substring match must throw, not pick one');
assert.ok(findLevel.includes('fellBackToLowest = true'), 'the lowest-level fallback must be reported, not silent');
assert.ok(findLevel.includes('Logger.Info'), 'the lowest-level fallback must leave a log line');

// --- create_floor: floorType must be read; level fallback must surface
const floorMethod = between('private object CreateFloor(JObject parameters)', 'private object ModifyElementParameter(JObject parameters)', 'CreateFloor');
assert.ok(floorMethod.includes('parameters["floorType"]'), 'the declared floorType param must actually be read');
assert.ok(!/\.Cast<FloorType>\(\)\s*\.FirstOrDefault\(\)/.test(floorMethod), 'must not pick an arbitrary FloorType');
assert.ok(floorMethod.includes('GetDefaultElementTypeId(ElementTypeGroup.FloorType)'),
  'the unspecified case must use the project default type, not an arbitrary one');
assert.ok(floorMethod.includes('找不到樓板類型'), 'a specified-but-missing floorType must throw');
assert.ok(floorMethod.includes('out levelFellBack'), 'the level fallback must be observable');
assert.ok(floorMethod.includes('FloorTypeSource'), 'the response must report which floor type was used');

// --- create_door / create_window: a named-type miss must throw, not fall through
for (const [label, from, to, param, msg] of [
  ['CreateDoor', 'private object CreateDoor(JObject parameters)', 'private object CreateWindow(JObject parameters)', 'doorType', '找不到門類型: '],
  ['CreateWindow', 'private object CreateWindow(JObject parameters)', 'private object GetAllGrids()', 'windowType', '找不到窗類型: '],
]) {
  const body = between(from, to, label);
  assert.ok(body.includes(`throw new Exception($"${msg}{${param}}`),
    `${label}: a specified ${param} that matches nothing must throw`);
  assert.ok(body.includes(`${param}Source`) || body.includes(`${param[0].toUpperCase()}${param.slice(1)}Source`),
    `${label}: the response must report how the type was chosen`);
  assert.ok(body.includes('.OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)'),
    `${label}: the last-resort pick must be deterministically ordered`);
}

// --- create_column: a specified-but-missing topLevel must throw instead of being ignored
const columnMethod = between('private object CreateColumn(JObject parameters)', 'private object GetFurnitureTypes(JObject parameters)', 'CreateColumn');
assert.ok(columnMethod.includes('找不到頂部樓層'), 'a specified topLevel that matches nothing must throw');
assert.ok(columnMethod.includes('out bottomLevelFellBack'), 'the bottom-level fallback must be observable');
assert.ok(columnMethod.includes('appliedTopLevel'), 'the response must report the top level actually applied');

// --- check_stair_headroom must not delete every DirectShape this add-in ever made
// Comments are stripped first: ClearExistingMarkers deliberately *documents* the removed clause
// so nobody reinstates it, and that comment must not read as the code being back.
const stair = readFileSync(new URL('../../MCP/Core/Commands/CommandExecutor.StairCompliance.cs', import.meta.url), 'utf8')
  .split(/\r?\n/).filter(l => !l.trim().startsWith('//')).join('\n');
assert.ok(!stair.includes('ds.ApplicationId == "RevitMCP"'),
  'ApplicationId == "RevitMCP" also matches the camera-manager view-cone markers (CameraKeyPlanService.MarkerA.cs) '
  + 'and must not be used to scope stair-marker cleanup');

console.log('PASS: create_wall + sibling create-tool contract and static guard regression (live Revit verification pending).');
