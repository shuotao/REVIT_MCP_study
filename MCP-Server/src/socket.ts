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
    private connectPromise: Promise<void> | null = null;
    private reconnectTimer: NodeJS.Timeout | null = null;
    private intentionalDisconnect: boolean = false;

    constructor(host: string = 'localhost', port?: number) {
        this.host = host;
        this.port = port ?? getConfiguredPort();
    }

    /**
     * Connect to Revit Plugin
     * Concurrent callers share the same in-flight attempt; the Revit side
     * only holds one MCP connection, so parallel sockets would clobber it.
     */
    async connect(): Promise<void> {
        if (this.isConnected()) {
            return;
        }
        if (this.connectPromise) {
            return this.connectPromise;
        }

        this.intentionalDisconnect = false;

        this.connectPromise = new Promise<void>((resolve, reject) => {
            const wsUrl = `ws://${this.host}:${this.port}`;
            console.error(`[Socket] Connecting to Revit: ${wsUrl}`);

            const ws = new WebSocket(wsUrl);
            this.ws = ws;

            let settled = false;
            const settle = (error?: Error) => {
                if (settled) return;
                settled = true;
                clearTimeout(connectionTimeout);
                this.connectPromise = null;
                if (error) {
                    reject(error);
                } else {
                    resolve();
                }
            };

            ws.on('open', () => {
                console.error('[Socket] Connected to Revit Plugin');
                settle();
            });

            ws.on('message', (data: WebSocket.Data) => {
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

            ws.on('error', (error) => {
                console.error('[Socket] WebSocket Error:', error);
                settle(error instanceof Error ? error : new Error(String(error)));
            });

            ws.on('close', () => {
                console.error('[Socket] Connection closed');
                if (this.ws === ws) {
                    this.ws = null;
                }
                settle(new Error('Connection closed before it was established'));

                // Reconnect logic — skip after an intentional disconnect,
                // and never stack a second timer on top of a pending one.
                if (this.intentionalDisconnect || this.reconnectTimer) {
                    return;
                }
                this.reconnectTimer = setTimeout(() => {
                    this.reconnectTimer = null;
                    console.error('[Socket] Attempting to reconnect...');
                    this.connect().catch(err => {
                        console.error('[Socket] Reconnection failed:', err);
                    });
                }, this.reconnectInterval);
            });

            // Connection Timeout
            const connectionTimeout = setTimeout(() => {
                if (ws.readyState !== WebSocket.OPEN) {
                    settle(new Error('Connection Timeout: Please ensure Revit Plugin is running and MCP server is enabled'));
                    ws.terminate();
                }
            }, 10000);
        });

        return this.connectPromise;
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
            // Request Timeout
            const requestTimeout = setTimeout(() => {
                if (this.responseHandlers.has(requestId)) {
                    this.responseHandlers.delete(requestId);
                    reject(new Error('Command timed out'));
                }
            }, 30000); // 30 seconds timeout

            // Register response handler
            this.responseHandlers.set(requestId, (response: RevitResponse) => {
                clearTimeout(requestTimeout);
                if (response.success) {
                    resolve(response);
                } else {
                    reject(new Error(response.error || 'Command failed'));
                }
            });

            // Send command
            this.ws?.send(JSON.stringify(command));
        });
    }

    /**
     * Check connection status
     */
    isConnected(): boolean {
        return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
    }

    /**
     * Disconnect (intentional — suppresses auto-reconnect)
     */
    disconnect(): void {
        this.intentionalDisconnect = true;
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