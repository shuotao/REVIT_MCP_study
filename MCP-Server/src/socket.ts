/**
 * Revit Socket Client
 * Handles WebSocket communication with Revit Plugin
 */

import WebSocket from 'ws';

export interface RevitCommand {
    commandName: string;
    parameters: Record<string, any>;
    requestId?: string;
}

export interface RevitResponse {
    success: boolean;
    data?: any;
    error?: string;
    requestId?: string;
}

// 預設 port 為 8964，可透過環境變數 REVIT_MCP_PORT 覆寫
const DEFAULT_PORT = 8964;

function getConfiguredPort(): number {
    const envPort = process.env.REVIT_MCP_PORT;
    if (envPort) {
        const parsed = parseInt(envPort, 10);
        if (!isNaN(parsed) && parsed >= 1024 && parsed <= 65535) {
            return parsed;
        }
        console.error(`[Socket] Invalid REVIT_MCP_PORT="${envPort}", using default ${DEFAULT_PORT}`);
    }
    return DEFAULT_PORT;
}

export class RevitSocketClient {
    private ws: WebSocket | null = null;
    private host: string = 'localhost';
    private port: number = DEFAULT_PORT;
    private reconnectInterval: number = 5000; // 5 seconds
    private responseHandlers: Map<string, (response: RevitResponse) => void> = new Map();

    /**
     * 單一重連 timer。每個 'close' 事件各自呼叫 setTimeout 會讓 timer 累積，
     * 多個 timer 先後觸發 connect() 時，後來者會覆寫掉前一條「已連上」的 socket 參照
     * (this.ws)，而它自己又因 Revit 端的獨占鎖被回 409 而永遠連不上 ——
     * 結果是好連線還握著鎖、卻沒有人持有它的參照，之後每一次呼叫都失敗。
     * 因此重連必須單例化：排新的之前先清掉舊的。
     */
    private reconnectTimer: NodeJS.Timeout | null = null;

    /**
     * 進行中的連線 (in-flight promise)。
     *
     * 不可以用「readyState === CONNECTING 就提早 resolve」來擋重複連線 ——
     * 那會把 connect() 的後置條件從「resolve 即已連上」弱化成「resolve 只代表有人正在連」，
     * 而 index.ts 的呼叫端是 `if (!isConnected()) await connect();` 之後立刻送指令，
     * sendCommand() 又會再檢查一次 isConnected()。結果會是 connect() 成功回傳、
     * 下一行卻丟「Not connected to Revit Plugin」。
     *
     * MCP SDK 的 request handler 沒有序列化佇列 (protocol.js 的 _onrequest 是 fire-and-forget)，
     * 並行的 tool call 會同時走到這裡，所以這條路徑是可達的。
     *
     * 正解是讓後到者共用同一條 in-flight promise 排隊等結果，而不是拿到一個空頭承諾。
     */
    private connecting: Promise<void> | null = null;

    /**
     * 是否為使用者主動斷線。disconnect() 之後不應該再自動把 Revit 的獨占鎖搶回來。
     */
    private intentionalClose: boolean = false;

    /**
     * 連線時回報給 Revit add-in 的客戶端名稱（來自 MCP initialize 的 clientInfo.name，
     * 例如 claude-code / claude-ai / Visual Studio Code）。由 index.ts 在連線前設定。
     * add-in 會以此顯示「目前佔用連線的工具」。
     */
    public clientName: string = "unknown";

    constructor(host: string = 'localhost', port?: number) {
        this.host = host;
        this.port = port ?? getConfiguredPort();
    }

    /**
     * Connect to Revit Plugin
     */
    async connect(): Promise<void> {
        // 只有真的 OPEN 才可以快速返回 —— 此時後置條件（resolve 即已連上）成立。
        // 刻意不把 CLOSING 納入：ws 的 graceful close 有 30 秒逾時，socket 可以合法
        // 卡在 CLOSING 長達 30 秒，若視為「不用連」會製造一個 30 秒的死窗。
        // CLOSING 直接開新連線是安全的：Revit 端 IsLocked_NoLock() 只認 Open 與
        // CloseReceived，不會因為 client 側的 CLOSING 而回 409。
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            return;
        }

        // 已有一次連線在飛行中：共用它的結果，不要另開 socket。
        // 新 socket 會覆寫 this.ws，而它必然被 Revit 的獨占鎖回 409（鎖正被前一條握著），
        // 造成好連線還握著鎖、卻沒有人持有它的參照的自我鎖死。
        if (this.connecting) {
            return this.connecting;
        }

        const p = this.openSocket();
        this.connecting = p;
        // 成功與失敗都要清掉 in-flight 標記；分開帶兩個 callback，
        // 避免這條清理鏈自己產生 unhandled rejection。
        p.then(
            () => { if (this.connecting === p) this.connecting = null; },
            () => { if (this.connecting === p) this.connecting = null; }
        );
        return p;
    }

    /**
     * 實際建立一條連線。只由 connect() 呼叫，重複連線的防護在 connect()。
     */
    private openSocket(): Promise<void> {
        return new Promise((resolve, reject) => {
            this.intentionalClose = false;

            const wsUrl = `ws://${this.host}:${this.port}/?client=${encodeURIComponent(this.clientName)}`;
            console.error(`[Socket] Connecting to Revit: ${wsUrl}`);

            this.ws = new WebSocket(wsUrl);
            const socket = this.ws;

            // 連線逾時 timer。三個出口（open/error/close）都要清掉，
            // 否則每 5 秒一次的重連會持續堆疊短命 timer 白白喚醒 event loop。
            const connectTimeout = setTimeout(() => {
                if (socket.readyState !== WebSocket.OPEN) {
                    // 逾時的這條要收掉，否則會留下一條沒人管、卻可能握著 Revit 獨占鎖的 socket。
                    // 這也是上面 OPEN-only 快速返回的安全網：卡在 CONNECTING 的 socket
                    // 若不 terminate 就永遠不會發出 'close'，重連鏈會斷。
                    try { socket.terminate(); } catch { /* ignore */ }
                    reject(new Error('Connection Timeout: Please ensure Revit Plugin is running and MCP server is enabled'));
                }
            }, 10000);

            this.ws.on('open', () => {
                clearTimeout(connectTimeout);
                console.error('[Socket] Connected to Revit Plugin');
                resolve();
            });

            this.ws.on('message', (data: WebSocket.Data) => {
                try {
                    const rawResponse = JSON.parse(data.toString());
                    // Map PascalCase from C# to camelCase for internal use
                    const response: RevitResponse = {
                        success: rawResponse.Success,
                        data: rawResponse.Data,
                        error: rawResponse.Error,
                        requestId: rawResponse.RequestId,
                    };
                    console.error('[Socket] Received response:', response);

                    // Handle Response
                    if (response.requestId) {
                        const handler = this.responseHandlers.get(response.requestId);
                        if (handler) {
                            handler(response);
                            this.responseHandlers.delete(response.requestId);
                        }
                    }
                } catch (error) {
                    console.error('[Socket] Failed to parse message:', error);
                }
            });

            this.ws.on('error', (error) => {
                clearTimeout(connectTimeout);
                console.error('[Socket] WebSocket Error:', error);
                reject(error);
            });

            this.ws.on('close', () => {
                clearTimeout(connectTimeout);
                console.error('[Socket] Connection closed');
                // 只有「關掉的正是目前這條」才清空參照。否則會把後來建立的
                // 有效連線誤清成 null。
                if (this.ws === socket) {
                    this.ws = null;
                }

                // 使用者主動斷線就不要再自動連回來 —— 那會把 Revit 的獨占鎖搶回來。
                if (this.intentionalClose) {
                    return;
                }

                // Reconnect logic —— 單例 timer，排新的之前先清掉舊的，
                // 避免多個 timer 疊加後彼此覆寫 socket 參照。
                if (this.reconnectTimer) {
                    clearTimeout(this.reconnectTimer);
                }
                this.reconnectTimer = setTimeout(() => {
                    this.reconnectTimer = null;
                    console.error('[Socket] Attempting to reconnect...');
                    this.connect().catch(err => {
                        console.error('[Socket] Reconnection failed:', err);
                    });
                }, this.reconnectInterval);
            });
        });
    }

    /**
     * Send command to Revit
     */
    async sendCommand(commandName: string, parameters: Record<string, any> = {}, timeoutMs: number = 30000): Promise<RevitResponse> {
        if (!this.isConnected()) {
            throw new Error('Not connected to Revit Plugin');
        }

        const requestId = this.generateRequestId();
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId,
        };

        console.error(`[Socket] Sending command: ${commandName}`, parameters);

        return new Promise((resolve, reject) => {
            // Timeout handle, captured so the response path can disarm it. Settling is already
            // guaranteed once by the responseHandlers.has() guard below; clearing the timer only
            // stops every completed command from leaving a timer armed for its full timeoutMs.
            let timeoutTimer: ReturnType<typeof setTimeout> | undefined;

            // Register response handler
            this.responseHandlers.set(requestId, (response: RevitResponse) => {
                if (timeoutTimer !== undefined) clearTimeout(timeoutTimer);
                if (response.success) {
                    resolve(response);
                } else {
                    reject(new Error(response.error || 'Command failed'));
                }
            });

            // Send command
            this.ws?.send(JSON.stringify(command));

            // Request Timeout
            timeoutTimer = setTimeout(() => {
                if (this.responseHandlers.has(requestId)) {
                    this.responseHandlers.delete(requestId);
                    reject(new Error('Command timed out'));
                }
            }, timeoutMs); // default 30 seconds; cross-document commands pass a longer value
        });
    }

    /**
     * Check connection status
     */
    isConnected(): boolean {
        return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
    }

    /**
     * Disconnect
     */
    disconnect(): void {
        // 標記為主動斷線，讓 'close' handler 不要再排重連。
        this.intentionalClose = true;
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
    }

    /**
     * Generate Request ID
     */
    private generateRequestId(): string {
        return `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    }
}