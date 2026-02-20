/**
 * Curtain Wall Pattern Preview Server
 * 本地 HTTP 伺服器，用於接收/傳遞帷幕牆排列資料
 */
import http from 'http';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 10001;

// 預設資料 (之後會從 Revit 取得)
let curtainWallData = {
    elementId: null,
    columns: 18,
    rows: 5,
    panelWidth: 1200,  // mm
    panelHeight: 1500, // mm
    panelTypes: [
        { id: 'A', name: 'Type A - 深灰', color: '#404040', revitTypeId: null },
        { id: 'B', name: 'Type B - 淺灰', color: '#C0C0C0', revitTypeId: null },
        { id: 'C', name: 'Type C - 藍灰', color: '#6082B6', revitTypeId: null }
    ]
};

// 結果儲存路徑
const RESULT_FILE = path.join(__dirname, 'curtain_pattern_result.json');

const server = http.createServer((req, res) => {
    // CORS 處理
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

    if (req.method === 'OPTIONS') {
        res.writeHead(200);
        res.end();
        return;
    }

    // GET /api/data - 取得初始資料
    if (req.method === 'GET' && req.url === '/api/data') {
        res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(JSON.stringify(curtainWallData));
        console.log('[Server] 發送帷幕牆資料');
        return;
    }

    // POST /api/result - 接收用戶調整結果
    if (req.method === 'POST' && req.url === '/api/result') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                const result = JSON.parse(body);
                // 儲存結果到檔案
                fs.writeFileSync(RESULT_FILE, JSON.stringify(result, null, 2), 'utf-8');
                console.log('[Server] 已儲存使用者設定到:', RESULT_FILE);
                console.log('[Server] 設定內容:', JSON.stringify(result, null, 2).substring(0, 500) + '...');

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: true, message: '設定已儲存！AI 將讀取並套用到 Revit。' }));
            } catch (err) {
                res.writeHead(400, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: false, error: err.message }));
            }
        });
        return;
    }

    // POST /api/data - AI 更新帷幕牆資料
    if (req.method === 'POST' && req.url === '/api/data') {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                const data = JSON.parse(body);
                curtainWallData = { ...curtainWallData, ...data };
                console.log('[Server] 已接收來自 AI 的帷幕牆資料:');
                console.log(`  - Element ID: ${curtainWallData.elementId}`);
                console.log(`  - Grid: ${curtainWallData.columns} 列 x ${curtainWallData.rows} 行`);
                console.log(`  - 面板尺寸: ${curtainWallData.panelWidth}mm x ${curtainWallData.panelHeight}mm`);
                if (curtainWallData.revitPanelTypes) {
                    console.log(`  - Revit Panel Types: ${curtainWallData.revitPanelTypes.length} 種`);
                }

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: true, message: '帷幕牆資料已更新' }));
            } catch (err) {
                res.writeHead(400, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: false, error: err.message }));
            }
        });
        return;
    }

    // GET / - 提供 HTML 頁面
    if (req.method === 'GET' && (req.url === '/' || req.url === '/index.html')) {
        const htmlPath = path.join(__dirname, 'curtain_wall_preview_v3.html');
        if (fs.existsSync(htmlPath)) {
            const html = fs.readFileSync(htmlPath, 'utf-8');
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(html);
        } else {
            res.writeHead(404);
            res.end('HTML file not found');
        }
        return;
    }

    // 404
    res.writeHead(404);
    res.end('Not Found');
});

// 啟動伺服器
server.listen(PORT, () => {
    console.log('========================================');
    console.log('  帷幕牆面板排列預覽伺服器');
    console.log('========================================');
    console.log(`  🌐 開啟瀏覽器: http://localhost:${PORT}`);
    console.log(`  📁 結果檔案: ${RESULT_FILE}`);
    console.log('========================================');
    console.log('');
    console.log('等待使用者操作...');
});

// 匯出 setData 函數，供外部腳本設定資料
export function setWallData(data) {
    curtainWallData = { ...curtainWallData, ...data };
    console.log('[Server] 已更新帷幕牆資料');
}
