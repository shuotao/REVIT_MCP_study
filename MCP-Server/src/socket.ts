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

    constructor(host: string = 'localhost', port?: number) {
        this.host = host;
        this.port = port ?? getConfiguredPort();
    }

    /**
     * Connect to Revit Plugin
     */
    async connect(): Promise<void> {
        return new Promise((resolve, reject) => {
            const wsUrl = `ws://${this.host}:${this.port}`;
            console.error(`[Socket] Connecting to Revit: ${wsUrl}`);

            this.ws = new WebSocket(wsUrl);

            this.ws.on('open', () => {
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
                console.error('[Socket] WebSocket Error:', error);
                reject(error);
            });

            this.ws.on('close', () => {
                console.error('[Socket] Connection closed');
                this.ws = null;

                // Reconnect logic
                setTimeout(() => {
                    console.error('[Socket] Attempting to reconnect...');
                    this.connect().catch(err => {
                        console.error('[Socket] Reconnection failed:', err);
                    });
                }, this.reconnectInterval);
            });

            // Connection Timeout
            setTimeout(() => {
                if (this.ws?.readyState !== WebSocket.OPEN) {
                    reject(new Error('Connection Timeout: Please ensure Revit Plugin is running and MCP server is enabled'));
                }
            }, 10000);
        });
    }

    /**
     * Send command to Revit
     */
    async sendCommand(commandName: string, parameters: Record<string, any> = {}): Promise<RevitResponse> {
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
            // Register response handler
            this.responseHandlers.set(requestId, (response: RevitResponse) => {
                if (response.success) {
                    resolve(response);
                } else {
                    reject(new Error(response.error || 'Command failed'));
                }
            });

            // Send command
            this.ws?.send(JSON.stringify(command));

            // Request Timeout
            setTimeout(() => {
                if (this.responseHandlers.has(requestId)) {
                    this.responseHandlers.delete(requestId);
                    reject(new Error('Command timed out'));
                }
            }, 30000); // 30 seconds timeout
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