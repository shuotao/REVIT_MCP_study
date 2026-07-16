const fs = require('fs');
const path = require('path');
const WebSocket = require('ws');

const WS_URL = process.env.REVIT_MCP_WS || 'ws://localhost:8964';
const ROOT = path.resolve(__dirname, '..', '..');
const OUTPUT = process.env.PARTITION_OUTPUT || path.join(ROOT, '物流中心_ALL_輕隔間數量計算.csv');
const ROOM_ASSIGN_PADDING_MM = 750;
const USE_WALL_AREA_FOR_EFFECTIVE_HEIGHT = process.env.USE_WALL_AREA_FOR_EFFECTIVE_HEIGHT !== '0';

const ROOM_FIELDS = ['編號', '名稱', '樓層', '周長', '面積', '未設邊界的高度'];
const WALL_FIELDS = ['空間編號', '空間名稱', '底部約束', '長度', '不連續高度', '面積', '房間邊界', '有無開口(Y/N)'];
const DOOR_FIELDS = ['樓層', '寬度', '高度', '粗略寬度', '粗略高度', '窗頂高度', '框總寬度'];
const WINDOW_FIELDS = ['樓層', '寬度', '高度', '粗略寬度', '粗略高度', '窗台高度', '窗頂高度'];

const ws = new WebSocket(WS_URL);
ws.setMaxListeners(0);

const pending = new Map();
let sequence = 0;

ws.on('message', (data) => {
  let msg;
  try {
    msg = JSON.parse(data.toString());
  } catch (err) {
    return;
  }

  const id = msg.id || msg.RequestId;
  const slot = pending.get(id);
  if (!slot) return;

  clearTimeout(slot.timer);
  pending.delete(id);

  if (msg.Error || msg.Success === false) {
    slot.reject(new Error(String(msg.Error || msg.Message || 'Command failed')));
    return;
  }

  slot.resolve(msg.Data !== undefined ? msg.Data : msg);
});

function sendCommand(method, params = {}, timeoutMs = 120000) {
  return new Promise((resolve, reject) => {
    const id = `partition_${Date.now()}_${++sequence}`;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`Timeout calling ${method}`));
    }, timeoutMs);

    pending.set(id, { resolve, reject, timer });
    ws.send(JSON.stringify({ method, params, id }));
  });
}

function queryElements(category, returnFields, extra = {}) {
  return sendCommand('query_elements', {
    category,
    maxCount: 5000,
    returnFields,
    ...extra,
  }).then((data) => {
    const body = data && data.Elements ? data : data && data.Data ? data.Data : data;
    return Array.isArray(body && body.Elements) ? body.Elements : [];
  });
}

async function mapLimit(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;

  async function worker() {
    while (next < items.length) {
      const index = next++;
      results[index] = await fn(items[index], index);
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return results;
}

function clean(value) {
  return String(value ?? '').replace(/^\uFEFF/, '').replace(/\s+/g, ' ').trim();
}

function parseNumber(value) {
  const text = clean(value).replace(/,/g, '');
  const match = text.match(/-?\d+(?:\.\d+)?/);
  return match ? Number(match[0]) : NaN;
}

function round2(value) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

function fixed2(value) {
  return Number.isFinite(value) ? round2(value).toFixed(2) : '';
}

function isPartitionType(typeName) {
  const text = clean(typeName).toUpperCase();
  return text.includes('TYPE');
}

function normalizeLevel(value) {
  let text = clean(value).toUpperCase();
  if (!text) return '';

  const buildingPrefix = text.match(/^[A-Z]+-(.+)$/);
  if (buildingPrefix) text = buildingPrefix[1];

  text = text.replace(/\s+/g, '');
  if (text === 'FN' || text === 'BS') return 'BS';

  const basement = text.match(/^B0*(\d+)F?$/);
  if (basement) return `B${Number(basement[1])}`;

  const roof = text.match(/^R0*(\d+)F?$/);
  if (roof) return `R${Number(roof[1])}`;

  const floor = text.match(/^0*(\d+)F$/);
  if (floor) return `F${Number(floor[1])}`;

  return text.replace(/F$/, '');
}

function roomNumber(room) {
  const explicit = clean(room['編號'] || room.Number || room.RoomNumber);
  if (explicit) return explicit.toUpperCase();
  const name = clean(room.Name || room['名稱']);
  const match = name.match(/((?:BS|FN|B|F|R)\d{2,4})\b/i);
  return match ? match[1].toUpperCase() : '';
}

function roomName(room) {
  const number = roomNumber(room);
  let name = clean(room['名稱'] || room.RoomName || room.Name);
  if (number) name = name.replace(new RegExp(`\\s*${number}\\s*$`, 'i'), '').trim();
  return name;
}

function roomPerimeter(room) {
  const value = parseNumber(room['周長'] || room.Perimeter);
  return Number.isFinite(value) ? fixed2(value) : '';
}

function floorFromRoomNumber(number) {
  const text = clean(number).toUpperCase();
  if (/^(BS|FN)/.test(text)) return { group: 0, floor: 0, numeric: parseNumber(text) || 0 };

  const match = text.match(/^([BFR])(\d{2,4})$/);
  if (!match) return { group: 9, floor: 999, numeric: 999999 };

  const prefix = match[1];
  const digits = Number(match[2]);
  const floor = digits >= 100 ? Math.floor(digits / 100) : digits;
  const group = prefix === 'B' ? 1 : prefix === 'F' ? 2 : 3;
  return { group, floor, numeric: digits };
}

function compareRooms(a, b) {
  const an = roomNumber(a);
  const bn = roomNumber(b);
  const ap = floorFromRoomNumber(an);
  const bp = floorFromRoomNumber(bn);

  if (ap.group !== bp.group) return ap.group - bp.group;
  if (ap.floor !== bp.floor) return ap.floor - bp.floor;
  if (ap.numeric !== bp.numeric) return ap.numeric - bp.numeric;
  return an.localeCompare(bn, 'zh-Hant', { numeric: true, sensitivity: 'base' });
}

function typeSortKey(typeName) {
  const text = clean(typeName).toUpperCase();
  const match = text.match(/TYPE[-\s]*([A-Z]+)(\d*)/);
  if (!match) return `Z-${text}`;

  const letterRank = match[1].split('').reduce((sum, ch) => sum * 26 + (ch.charCodeAt(0) - 64), 0);
  const numberRank = match[2] ? Number(match[2]) : 0;
  return `${String(letterRank).padStart(3, '0')}-${String(numberRank).padStart(3, '0')}-${text}`;
}

function compareTypes(a, b) {
  const ak = typeSortKey(a);
  const bk = typeSortKey(b);
  if (ak !== bk) return ak.localeCompare(bk, 'zh-Hant', { numeric: true, sensitivity: 'base' });
  return clean(a).localeCompare(clean(b), 'zh-Hant', { numeric: true, sensitivity: 'base' });
}

function pointInsideBox(point, box, padding = 0) {
  if (!box) return false;
  return point.x >= box.MinX - padding &&
    point.x <= box.MaxX + padding &&
    point.y >= box.MinY - padding &&
    point.y <= box.MaxY + padding;
}

function distancePointToBox(point, box) {
  if (!box) return Number.POSITIVE_INFINITY;
  const dx = point.x < box.MinX ? box.MinX - point.x : point.x > box.MaxX ? point.x - box.MaxX : 0;
  const dy = point.y < box.MinY ? box.MinY - point.y : point.y > box.MaxY ? point.y - box.MaxY : 0;
  return Math.hypot(dx, dy);
}

function distanceSquared(a, b) {
  return (a.x - b.x) ** 2 + (a.y - b.y) ** 2;
}

function segmentTouchesBox(wall, box, padding = 0) {
  if (!box) return false;
  const samples = [0, 0.25, 0.5, 0.75, 1];
  return samples.some((t) => pointInsideBox({
    x: wall.startX + (wall.endX - wall.startX) * t,
    y: wall.startY + (wall.endY - wall.startY) * t,
  }, box, padding));
}

function csvEscape(value) {
  const text = String(value ?? '');
  if (/[",\r\n]/.test(text)) return `"${text.replace(/"/g, '""')}"`;
  return text;
}

function parseSizeFromName(name) {
  const text = clean(name);
  const match = text.match(/(\d+(?:\.\d+)?)\s*[xX*×]\s*(\d+(?:\.\d+)?)/);
  if (!match) return { width: NaN, height: NaN };

  const a = Number(match[1]);
  const b = Number(match[2]);
  const divisor = /cm/i.test(text) || a > 20 || b > 20 ? 100 : 1;
  return { width: a / divisor, height: b / divisor };
}

function openingDimensions(opening) {
  let width = parseNumber(opening['寬度']);
  let height = parseNumber(opening['高度']);

  if (!Number.isFinite(width) || width === 0) width = parseNumber(opening['粗略寬度']);
  if (!Number.isFinite(height) || height === 0) height = parseNumber(opening['粗略高度']);

  if (!Number.isFinite(width) || !Number.isFinite(height) || width === 0 || height === 0) {
    const parsed = parseSizeFromName(opening.Name);
    if (!Number.isFinite(width) || width === 0) width = parsed.width;
    if (!Number.isFinite(height) || height === 0) height = parsed.height;
  }

  return { width, height };
}

function buildRoomRecord(room, info) {
  const box = info && info.BoundingBox ? info.BoundingBox : null;
  return {
    ...room,
    number: roomNumber(room),
    name: roomName(room),
    level: normalizeLevel((info && info.Level) || room['樓層']),
    perimeter: roomPerimeter(room),
    area: parseNumber(room['面積']),
    center: {
      x: Number(info && info.CenterX) || 0,
      y: Number(info && info.CenterY) || 0,
    },
    box,
  };
}

function buildWallRecord(wall, info) {
  const length = Number(info && info.Length) / 1000;
  const height = Number(info && info.Height) / 1000;
  const fallbackLength = parseNumber(wall['長度']);
  const fallbackHeight = parseNumber(wall['不連續高度']);
  const fallbackArea = parseNumber(wall['面積']);
  const typeName = clean((info && info.WallType) || wall.Name);

  return {
    elementId: String(wall.ElementId),
    typeName,
    roomNumberParam: clean(wall['空間編號']),
    roomNameParam: clean(wall['空間名稱']),
    level: normalizeLevel((info && info.Level) || wall['底部約束']),
    length: Number.isFinite(length) && length > 0 ? length : fallbackLength,
    height: Number.isFinite(height) && height > 0 ? height : fallbackHeight,
    area: Number.isFinite(fallbackArea) && fallbackArea >= 0 ? fallbackArea : NaN,
    startX: Number(info && info.StartX),
    startY: Number(info && info.StartY),
    endX: Number(info && info.EndX),
    endY: Number(info && info.EndY),
    openings: [],
  };
}

function wallMidpoint(wall) {
  return {
    x: (wall.startX + wall.endX) / 2,
    y: (wall.startY + wall.endY) / 2,
  };
}

function assignWallToRoom(wall, rooms, roomsByNumber) {
  if (wall.roomNumberParam && roomsByNumber.has(wall.roomNumberParam.toUpperCase())) {
    return roomsByNumber.get(wall.roomNumberParam.toUpperCase());
  }

  const sameLevelRooms = rooms.filter((room) => room.level && wall.level && room.level === wall.level && room.box);
  const pool = sameLevelRooms.length ? sameLevelRooms : rooms.filter((room) => room.box);
  const mid = wallMidpoint(wall);

  let best = null;
  let bestScore = Number.POSITIVE_INFINITY;

  for (const room of pool) {
    const touches = segmentTouchesBox(wall, room.box, ROOM_ASSIGN_PADDING_MM);
    const boxDistance = distancePointToBox(mid, room.box);
    const centerDistance = Math.sqrt(distanceSquared(mid, room.center));
    const areaPenalty = Number.isFinite(room.area) ? room.area * 0.01 : 0;
    const score = (touches ? -100000 : 0) + boxDistance * 10 + centerDistance + areaPenalty;

    if (score < bestScore) {
      best = room;
      bestScore = score;
    }
  }

  return best;
}

function getParamValue(info, names) {
  const wanted = new Set(names.map(clean));
  const params = Array.isArray(info && info.Parameters) ? info.Parameters : [];
  const found = params.find((param) => wanted.has(clean(param.Name)));
  return found ? found.Value : '';
}

function normalizeElementId(value) {
  const match = clean(value).match(/-?\d+/);
  return match ? match[0] : '';
}

function attachOpeningHost(opening, info) {
  opening.hostElementId = normalizeElementId(getParamValue(info, ['主體 ID', 'Host ID', 'Host Id']));
}

function assignOpeningsToHostWalls(openings, walls) {
  const wallsById = new Map(walls.map((wall) => [String(wall.elementId), wall]));
  let assigned = 0;

  for (const opening of openings) {
    const hostWall = wallsById.get(normalizeElementId(opening.hostElementId));
    if (!hostWall) continue;

    const dims = openingDimensions(opening);
    hostWall.openings.push({
      name: clean(opening.Name),
      width: dims.width,
      height: dims.height,
    });
    assigned++;
  }

  return assigned;
}

function makeRows(rooms, groupedByRoom, wallTypeCols) {
  const rows = [];
  const typeColIndex = new Map(wallTypeCols.map((type, index) => [type, index]));

  for (const room of rooms) {
    const walls = groupedByRoom.get(room.number) || [];

    if (!walls.length) {
      rows.push(new Array(15 + wallTypeCols.length).fill('').map((_, i) => {
        if (i === 0) return room.number;
        if (i === 1) return room.name;
        if (i === 2) return room.perimeter;
        return '';
      }));
      continue;
    }

    const groups = new Map();
    for (const wall of walls) {
      const heightKey = Number.isFinite(wall.height) ? fixed2(wall.height) : '';
      const groupKey = USE_WALL_AREA_FOR_EFFECTIVE_HEIGHT && Number.isFinite(wall.area)
        ? (wall.level || heightKey)
        : heightKey;
      const key = `${wall.typeName}||${groupKey}`;
      if (!groups.has(key)) groups.set(key, { typeName: wall.typeName, height: wall.height, level: wall.level, walls: [], openings: [] });
      const group = groups.get(key);
      group.walls.push(wall);
      group.openings.push(...wall.openings);
    }

    const sortedGroups = Array.from(groups.values()).sort((a, b) => compareTypes(a.typeName, b.typeName) || clean(a.level).localeCompare(clean(b.level), 'zh-Hant', { numeric: true, sensitivity: 'base' }) || (a.height - b.height));

    for (const group of sortedGroups) {
      const openingGroups = new Map();
      for (const op of group.openings) {
        const key = `${op.name}||${fixed2(op.width)}||${fixed2(op.height)}`;
        if (!openingGroups.has(key)) openingGroups.set(key, { ...op, count: 0 });
        openingGroups.get(key).count += 1;
      }

      const openings = Array.from(openingGroups.values()).filter((op) => Number.isFinite(op.width) && Number.isFinite(op.height) && op.width > 0 && op.height > 0);
      const rowCount = Math.max(1, openings.length);
      const lengths = group.walls
        .map((wall) => fixed2(wall.length))
        .filter(Boolean);
      const lengthTotal = lengths.reduce((sum, value) => sum + (parseNumber(value) || 0), 0);
      const openingArea = openings.reduce((sum, op) => sum + op.count * op.width * op.height, 0);
      const wallArea = group.walls.reduce((sum, wall) => {
        return sum + (Number.isFinite(wall.area) && wall.area > 0 ? wall.area : 0);
      }, 0);
      // Keep the template formula as length * height, but anchor the result to
      // Revit's wall area. Using unconnected height directly overcounts attached
      // or clipped walls, as seen in Type-B panel partitions.
      const groupHeight = USE_WALL_AREA_FOR_EFFECTIVE_HEIGHT && wallArea > 0 && lengthTotal > 0
        ? (wallArea + openingArea) / lengthTotal
        : group.height;

      for (let i = 0; i < rowCount; i++) {
        const row = new Array(15 + wallTypeCols.length).fill('');
        row[0] = i === 0 ? room.number : '';
        row[1] = i === 0 ? room.name : '';
        row[2] = i === 0 ? room.perimeter : '';

        if (i === 0) {
          row[3] = group.typeName;
          row[4] = lengths.length ? `=${lengths.join('+')}` : '';
          row[5] = fixed2(groupHeight);
          row[6] = 'AREA_FORMULA';
          row[7] = 'SUBTOTAL_FORMULA';
          row[14] = 'TOTAL_FORMULA';

          const col = typeColIndex.get(group.typeName);
          if (col !== undefined) row[15 + col] = 'TYPE_FORMULA';
        }

        if (openings[i]) {
          row[8] = openings[i].name;
          row[9] = String(openings[i].count);
          row[10] = fixed2(openings[i].width);
          row[11] = fixed2(openings[i].height);
          row[12] = 'OPENING_AREA_FORMULA';
          row[13] = 'OPENING_SUBTOTAL_FORMULA';
        }

        rows.push(row);
      }
    }
  }

  return rows;
}

function fillFormulas(rows, wallTypeCols) {
  const firstDataRow = 6;

  for (let i = 0; i < rows.length; i++) {
    const row = rows[i];
    const rowNumber = firstDataRow + i;

    if (row[6] === 'AREA_FORMULA') row[6] = `=E${rowNumber}*F${rowNumber}`;
    if (row[7] === 'SUBTOTAL_FORMULA') row[7] = `=G${rowNumber}`;
    if (row[12] === 'OPENING_AREA_FORMULA') row[12] = `=J${rowNumber}*K${rowNumber}*L${rowNumber}`;
    if (row[13] === 'OPENING_SUBTOTAL_FORMULA') row[13] = `=M${rowNumber}`;

    if (row[14] === 'TOTAL_FORMULA') {
      const deductions = [];
      if (row[13]) deductions.push(`N${rowNumber}`);

      let lookAhead = 1;
      while (rows[i + lookAhead] && !rows[i + lookAhead][3] && rows[i + lookAhead][13]) {
        deductions.push(`N${rowNumber + lookAhead}`);
        lookAhead++;
      }

      row[14] = deductions.length ? `=H${rowNumber}-${deductions.join('-')}` : `=H${rowNumber}`;
    }

    const typeIndex = wallTypeCols.indexOf(row[3]);
    if (typeIndex >= 0 && row[15 + typeIndex] === 'TYPE_FORMULA') {
      row[15 + typeIndex] = `=O${rowNumber}`;
    }
  }

  return rows;
}

function writeCsv(rows, wallTypeCols) {
  const lines = [];
  const trailing = ','.repeat(14 + wallTypeCols.length);

  lines.push(`工程採購數量計算表${trailing}`);
  lines.push(`物流中心${trailing}`);
  lines.push(`輕隔間數量計算表 (全棟合併)${trailing}`);
  lines.push(`樓別名稱,,輕隔間牆,,,,,,門開口、窗開口,,,,,,,輕隔間牆面積${','.repeat(Math.max(wallTypeCols.length - 1, 0))}`);
  lines.push([
    '編號', '空間名稱', '空間周長', '輕隔間牆類型', '牆長', '牆高', '面積', '小計',
    '名稱', '數量', '長', '高', '面積', '小計', '總計',
    ...wallTypeCols,
  ].map(csvEscape).join(','));

  for (const row of rows) lines.push(row.map(csvEscape).join(','));

  fs.writeFileSync(OUTPUT, `\uFEFF${lines.join('\n')}\n`, 'utf8');
}

function summarize(rows, wallTypeCols) {
  const summary = {};
  for (const type of wallTypeCols) summary[type] = 0;

  function openingAreaFromRow(row) {
    if (!row[9] || !row[10] || !row[11]) return 0;
    return (parseNumber(row[9]) || 0) * (parseNumber(row[10]) || 0) * (parseNumber(row[11]) || 0);
  }

  for (let i = 0; i < rows.length; i++) {
    const row = rows[i];
    const type = row[3];
    if (!type || !(type in summary)) continue;

    const lengthFormula = clean(row[4]).replace(/^=/, '');
    const length = lengthFormula.split('+').reduce((sum, part) => sum + (parseNumber(part) || 0), 0);
    const height = parseNumber(row[5]) || 0;
    let opening = openingAreaFromRow(row);

    let lookAhead = 1;
    while (rows[i + lookAhead] && !rows[i + lookAhead][3]) {
      opening += openingAreaFromRow(rows[i + lookAhead]);
      lookAhead++;
    }

    summary[type] += Math.max(0, length * height - opening);
  }

  return Object.fromEntries(Object.entries(summary).map(([k, v]) => [k, round2(v)]));
}

async function main() {
  await new Promise((resolve, reject) => {
    ws.once('open', resolve);
    ws.once('error', reject);
  });

  console.log('[1/7] Querying project, rooms, walls, doors, and windows...');
  // The Revit add-in processes one ExternalEvent at a time. Keep direct socket calls
  // sequential so requests are not overwritten by later ones.
  const project = await sendCommand('get_project_info', {});
  const roomRows = await queryElements('Rooms', ROOM_FIELDS);
  const wallRows = await queryElements('Walls', WALL_FIELDS);
  const doorRows = await queryElements('Doors', DOOR_FIELDS);
  const windowRows = await queryElements('Windows', WINDOW_FIELDS);
  const wallTypesData = await sendCommand('get_wall_types', {});

  console.log(`[2/7] Rooms=${roomRows.length}, walls=${wallRows.length}, doors=${doorRows.length}, windows=${windowRows.length}`);

  const rawPartitionWalls = wallRows.filter((wall) => isPartitionType(wall.Name));
  console.log(`[3/7] Partition wall instances=${rawPartitionWalls.length}`);

  console.log('[4/7] Fetching room bounding boxes...');
  const roomInfos = await mapLimit(roomRows, 1, async (room, index) => {
    if ((index + 1) % 25 === 0 || index === roomRows.length - 1) {
      console.log(`      room info ${index + 1}/${roomRows.length}`);
    }
    try {
      return await sendCommand('get_room_info', { roomId: Number(room.ElementId) }, 60000);
    } catch (err) {
      return null;
    }
  });

  const rooms = roomRows
    .map((room, index) => buildRoomRecord(room, roomInfos[index]))
    .filter((room) => room.number || room.name)
    .sort(compareRooms);
  const roomsByNumber = new Map(rooms.filter((room) => room.number).map((room) => [room.number.toUpperCase(), room]));

  console.log('[5/7] Fetching partition wall geometry...');
  const wallInfos = await mapLimit(rawPartitionWalls, 1, async (wall, index) => {
    if ((index + 1) % 25 === 0 || index === rawPartitionWalls.length - 1) {
      console.log(`      wall info ${index + 1}/${rawPartitionWalls.length}`);
    }
    try {
      return await sendCommand('get_wall_info', { wallId: Number(wall.ElementId) }, 60000);
    } catch (err) {
      return null;
    }
  });

  const partitionWalls = rawPartitionWalls
    .map((wall, index) => buildWallRecord(wall, wallInfos[index]))
    .filter((wall) => isPartitionType(wall.typeName) && Number.isFinite(wall.length) && wall.length > 0 && Number.isFinite(wall.height) && wall.height > 0);

  console.log('[6/7] Fetching door/window host walls and assigning verified openings...');
  const openings = [...doorRows, ...windowRows];
  const openingInfos = await mapLimit(openings, 1, async (opening, index) => {
    if ((index + 1) % 50 === 0 || index === openings.length - 1) {
      console.log(`      opening host ${index + 1}/${openings.length}`);
    }
    try {
      return await sendCommand('get_element_info', { elementId: Number(opening.ElementId) }, 60000);
    } catch (err) {
      return null;
    }
  });

  openings.forEach((opening, index) => {
    attachOpeningHost(opening, openingInfos[index]);
  });

  const assignedOpenings = assignOpeningsToHostWalls(openings, partitionWalls);

  console.log('[7/7] Assigning walls to rooms and writing CSV...');
  const groupedByRoom = new Map();
  let assignedWalls = 0;

  for (const wall of partitionWalls) {
    const room = assignWallToRoom(wall, rooms, roomsByNumber);
    if (!room) continue;
    if (!groupedByRoom.has(room.number)) groupedByRoom.set(room.number, []);
    groupedByRoom.get(room.number).push(wall);
    assignedWalls++;
  }

  const definedTypeNames = Array.isArray(wallTypesData && wallTypesData.WallTypes)
    ? wallTypesData.WallTypes.map((type) => clean(type.Name)).filter(isPartitionType)
    : [];
  const usedTypeNames = partitionWalls.map((wall) => wall.typeName).filter(isPartitionType);
  const wallTypeCols = Array.from(new Set([...definedTypeNames, ...usedTypeNames])).sort(compareTypes);

  const rows = fillFormulas(makeRows(rooms, groupedByRoom, wallTypeCols), wallTypeCols);
  writeCsv(rows, wallTypeCols);

  const typeSummary = summarize(rows, wallTypeCols);
  console.log(JSON.stringify({
    output: OUTPUT,
    project: project && project.ProjectName,
    rooms: rooms.length,
    partitionWalls: partitionWalls.length,
    assignedWalls,
    openings: openings.length,
    assignedOpenings,
    wallTypes: wallTypeCols,
    typeSummary,
  }, null, 2));

  ws.close();
}

main().catch((err) => {
  console.error(err && err.stack ? err.stack : err);
  try { ws.close(); } catch {}
  process.exitCode = 1;
});
