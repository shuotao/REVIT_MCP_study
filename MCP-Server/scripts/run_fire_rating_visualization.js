/**
 * ?†é??²ç«?²ç??§èƒ½è¦–è¦º?? * ?é? WebSocket ?´æŽ¥??Ž¥ Revit MCP Server
 */

import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:11111');

// é¡è‰²? å??ç½®
const COLOR_MAP = {
    "2å°æ?": { r: 0, g: 180, b: 0, transparency: 20, label: "?Ÿ¢ 2å°æ??²ç«" },
    "1.5å°æ?": { r: 100, g: 220, b: 100, transparency: 30, label: "?Ÿ¢ 1.5å°æ??²ç«" },
    "1å°æ?": { r: 255, g: 255, b: 0, transparency: 30, label: "?Ÿ¡ 1å°æ??²ç«" },
    "0.5å°æ?": { r: 255, g: 165, b: 0, transparency: 30, label: "?? 0.5å°æ??²ç«" },
    "?¡é˜²??: { r: 100, g: 150, b: 255, transparency: 40, label: "?”µ ?¡é˜²?? },
    "?ªè¨­å®?: { r: 200, g: 0, b: 200, transparency: 50, label: "?Ÿ£ ?ªè¨­å®? }
};

const PARAMETER_NAMES = ["?²ç«?²ç??§èƒ½", "?²ç«?‚æ?", "Fire Rating", "FireRating", "?²ç«?§èƒ½"];

let currentView = null;
let allWalls = [];
let wallDataList = [];
let currentWallIndex = 0;
let distribution = {};
let stage = 'get_view';

function sendCommand(commandName, parameters) {
    const command = {
        CommandName: commandName,
        Parameters: parameters,
        RequestId: `${commandName}_${Date.now()}`
    };
    console.log(`[?¼é€] ${commandName}`);
    ws.send(JSON.stringify(command));
}

function getColorForValue(value) {
    for (const [key, config] of Object.entries(COLOR_MAP)) {
        if (value && value.includes(key)) {
            return config;
        }
    }
    return COLOR_MAP["?ªè¨­å®?];
}

ws.on('open', function () {
    console.log('='.repeat(60));
    console.log('?†é??²ç«?²ç??§èƒ½è¦–è¦º??);
    console.log('='.repeat(60));
    console.log('\næ­¥é? 1: ?–å??¶å?è¦–å?...');
    sendCommand('get_active_view', {});
});

ws.on('message', function (data) {
    const response = JSON.parse(data.toString());

    if (!response.Success) {
        console.log('???¯èª¤:', response.Error);
        ws.close();
        return;
    }

    switch (stage) {
        case 'get_view':
            currentView = response.Data;
            console.log(`???¶å?è¦–å?: ${currentView.Name} (ID: ${currentView.Id})`);

            console.log('\næ­¥é? 2: ?¥è©¢?€?‰ç?é«?..');
            stage = 'get_walls';
            sendCommand('query_elements', { category: 'Walls', viewId: currentView.Id });
            break;

        case 'get_walls':
            allWalls = response.Data.Elements || [];
            console.log(`???¾åˆ° ${allWalls.length} ?¢ç?`);

            if (allWalls.length === 0) {
                console.log('???¶å?è¦–å?ä¸­æ??‰ç?é«?);
                ws.close();
                return;
            }

            console.log('\næ­¥é? 3: ?†æ??²ç«?²ç??§èƒ½?ƒæ•¸...');
            stage = 'get_wall_info';
            currentWallIndex = 0;
            sendCommand('get_element_info', { elementId: allWalls[currentWallIndex].ElementId });
            break;

        case 'get_wall_info':
            const wallInfo = response.Data;
            let fireRatingValue = "?ªè¨­å®?;

            // ?¥æ‰¾?²ç«?ƒæ•¸
            if (wallInfo.Parameters) {
                for (const paramName of PARAMETER_NAMES) {
                    const param = wallInfo.Parameters.find(p => p.Name === paramName);
                    if (param && param.Value) {
                        fireRatingValue = param.Value.trim();
                        break;
                    }
                }
            }

            wallDataList.push({
                elementId: allWalls[currentWallIndex].ElementId,
                name: wallInfo.Name || "?ªå‘½??,
                fireRating: fireRatingValue
            });

            // çµ±è??†å?
            if (!distribution[fireRatingValue]) {
                distribution[fireRatingValue] = 0;
            }
            distribution[fireRatingValue]++;

            currentWallIndex++;
            if (currentWallIndex < allWalls.length) {
                // ç¹¼ç??•ç?ä¸‹ä??¢ç?
                if (currentWallIndex % 10 === 0) {
                    console.log(`  ?•ç?ä¸?.. ${currentWallIndex}/${allWalls.length}`);
                }
                sendCommand('get_element_info', { elementId: allWalls[currentWallIndex].ElementId });
            } else {
                // ?€?‰ç?é«”å??å???                console.log(`???†æ?å®Œæ? ${allWalls.length} ?¢ç?`);
                console.log('\n?ƒæ•¸?¼å?å¸?');
                for (const [value, count] of Object.entries(distribution)) {
                    const config = getColorForValue(value);
                    console.log(`  ${config.label}: ${count} ?¢`);
                }

                console.log('\næ­¥é? 4: ?‰ç”¨é¡è‰²è¦†å¯«...');
                stage = 'apply_override';
                currentWallIndex = 0;
                applyNextOverride();
            }
            break;

        case 'apply_override':
            currentWallIndex++;
            if (currentWallIndex < wallDataList.length) {
                if (currentWallIndex % 10 === 0) {
                    console.log(`  è¦†å¯«ä¸?.. ${currentWallIndex}/${wallDataList.length}`);
                }
                applyNextOverride();
            } else {
                // ?€?‰è?å¯«å???                console.log(`??è¦†å¯«å®Œæ? ${wallDataList.length} ?¢ç?`);
                printFinalReport();
                ws.close();
            }
            break;
    }
});

function applyNextOverride() {
    const wall = wallDataList[currentWallIndex];
    const colorConfig = getColorForValue(wall.fireRating);

    sendCommand('override_element_graphics', {
        elementId: wall.elementId,
        viewId: currentView.Id,
        surfaceFillColor: { r: colorConfig.r, g: colorConfig.g, b: colorConfig.b },
        transparency: colorConfig.transparency
    });
}

function printFinalReport() {
    console.log('\n' + '='.repeat(60));
    console.log('?†é??²ç«?²ç??§èƒ½è¦–è¦º?–å ±??);
    console.log('='.repeat(60));

    console.log(`\nè¦–å?: ${currentView.Name} (ID: ${currentView.Id})`);
    console.log(`ç¸½ç?é«”æ•¸?? ${wallDataList.length} ?¢`);

    console.log('\n?²ç«?§èƒ½?†å?:');
    for (const [value, count] of Object.entries(distribution)) {
        const config = getColorForValue(value);
        const percentage = ((count / wallDataList.length) * 100).toFixed(1);
        console.log(`  ${config.label}: ${count} ??(${percentage}%)`);
    }

    console.log('\né¡è‰²? å?è¡?');
    for (const [value, config] of Object.entries(COLOR_MAP)) {
        console.log(`  ${config.label}: RGB(${config.r}, ${config.g}, ${config.b}) ?æ?åº?${config.transparency}%`);
    }

    const allIds = wallDataList.map(w => w.elementId);
    console.log('\næ¸…é™¤é¡è‰²è¦†å¯«?‡ä»¤:');
    console.log(`node -e "...clear_element_override({ elementIds: [${allIds.slice(0, 5).join(', ')}...], viewId: ${currentView.Id} })"`);

    console.log('\n' + '='.repeat(60));
    console.log('???·è?å®Œæ?ï¼è?æª¢æŸ¥ Revit è¦–å?ä¸­ç?é¡è‰²æ¨™è???);
    console.log('='.repeat(60));
}

ws.on('error', function (error) {
    console.error('??????¯èª¤:', error.message);
    console.log('è«‹ç¢ºèª?Revit å·²å??•ä? MCP ?å?å·²é???);
});

ws.on('close', function () {
    process.exit(0);
});

setTimeout(() => {
    console.log('? ï? ?·è?è¶…æ?');
    ws.close();
    process.exit(1);
}, 120000);

