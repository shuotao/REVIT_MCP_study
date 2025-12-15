/**
 * Revit Socket 客戶端
 * 負責與 Revit Plugin 的 WebSocket 通訊
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

export class RevitSocketClient {
    private ws: WebSocket | null = null;
    private host: string = 'localhost';
    private port: number = 8765;
    private reconnectInterval: number = 5000; // 5 秒
    private responseHandlers: Map<string, (response: RevitResponse) => void> = new Map();

    constructor(host: string = 'localhost', port: number = 8765) {
        this.host = host;
        this.port = port;
    }

    /**
     * 連線到 Revit Plugin
     */
    async connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            const wsUrl = `ws://${this.host}:${this.port}`;
            console.error(`[Socket] 連線至 Revit: ${wsUrl}`);

            this.ws = new WebSocket(wsUrl);

            this.ws.on('open', () => {
                console.error('[Socket] 已連線至 Revit Plugin');
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
                    console.error('[Socket] 收到回應:', response);

                    // 處理回應
                    if (response.requestId) {
                        const handler = this.responseHandlers.get(response.requestId);
                        if (handler) {
                            handler(response);
                            this.responseHandlers.delete(response.requestId);
                        }
                    }
                } catch (error) {
                    console.error('[Socket] 解析訊息失敗:', error);
                }
            });

            this.ws.on('error', (error) => {
                console.error('[Socket] WebSocket 錯誤:', error);
                reject(error);
            });

            this.ws.on('close', () => {
                console.error('[Socket] 連線已關閉');
                this.ws = null;

                // 自動重連
                setTimeout(() => {
                    console.error('[Socket] 嘗試重新連線...');
                    this.connect().catch(err => {
                        console.error('[Socket] 重新連線失敗:', err);
                    });
                }, this.reconnectInterval);
            });

            // 連線逾時
            setTimeout(() => {
                if (this.ws?.readyState !== WebSocket.OPEN) {
                    reject(new Error('連線逾時：請確認 Revit Plugin 是否已啟動並開啟 MCP 服務'));
                }
            }, 10000);
        });
    }

    /**
     * 發送命令到 Revit
     */
    async sendCommand(commandName: string, parameters: Record<string, any> = {}): Promise<RevitResponse> {
        if (!this.isConnected()) {
            throw new Error('未連線至 Revit Plugin');
        }

        const requestId = this.generateRequestId();
        const command = {
            CommandName: commandName,
            Parameters: parameters,
            RequestId: requestId,
        };

        console.error(`[Socket] 發送命令: ${commandName}`, parameters);

        return new Promise((resolve, reject) => {
            // 註冊回應處理器
            this.responseHandlers.set(requestId, (response: RevitResponse) => {
                if (response.success) {
                    resolve(response);
                } else {
                    reject(new Error(response.error || '命令執行失敗'));
                }
            });

            // 發送命令
            this.ws?.send(JSON.stringify(command));

            // 設定逾時
            setTimeout(() => {
                if (this.responseHandlers.has(requestId)) {
                    this.responseHandlers.delete(requestId);
                    reject(new Error('命令執行逾時'));
                }
            }, 30000); // 30 秒逾時
        });
    }

    /**
     * 檢查連線狀態
     */
    isConnected(): boolean {
        return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
    }

    /**
     * 關閉連線
     */
    disconnect(): void {
        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }
    }

    /**
     * 生成唯一請求 ID
     */
    private generateRequestId(): string {
        return `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    }
}
