#!/usr/bin/env python3
"""
Monstrare Kanban MCP Server (Stdio JSON-RPC 2.0)
================================================
提供 VS Code、Claude Code、Cline、Roo Code 等 MCP 用戶端
存取與操作 Monstrare 看板卡片 (tools/kanban/cards/*.json)
"""

import json
import os
import sys
import glob

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARDS_DIR = os.path.join(WORKSPACE, "tools", "kanban", "cards")

TOOLS = [
    {
        "name": "get_kanban_status",
        "description": "查詢 Monstrare 看板全域狀態，回傳各階段 (backlog/ready/implementing/verify/done) 卡片數量與統計。",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    },
    {
        "name": "list_cards",
        "description": "查詢看板中的任務卡片列表，支援按 stage, epic, owner, risk 過濾。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "stage": {"type": "string", "description": "階段 (backlog/blocked/ready/implementing/verify/done)"},
                "epic": {"type": "string", "description": "Epic 名稱"},
                "owner": {"type": "string", "description": "負責人姓名"},
                "risk": {"type": "string", "description": "風險等級 (low/medium/high)"}
            }
        }
    },
    {
        "name": "get_card",
        "description": "取得特定 ID 之任務卡片詳細資訊 (如 TASK-003)。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string", "description": "卡片 ID (例如 TASK-003)"}
            },
            "required": ["id"]
        }
    },
    {
        "name": "create_card",
        "description": "建立新的 Monstrare 看板任務卡片。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string", "description": "卡片 ID (如 TASK-009)"},
                "title": {"type": "string", "description": "卡片標題"},
                "content": {"type": "string", "description": "卡片內容 (Markdown)"},
                "stage": {"type": "string", "default": "backlog"},
                "epic": {"type": "string"},
                "userStory": {"type": "string"},
                "owner": {"type": "string", "default": "shuotao"},
                "risk": {"type": "string", "default": "medium"}
            },
            "required": ["id", "title"]
        }
    },
    {
        "name": "update_card",
        "description": "更新既有任務卡片之欄位 (如切換 stage 至 verify 或修改內容)。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string", "description": "卡片 ID (如 TASK-003)"},
                "stage": {"type": "string", "description": "新階段 (backlog/ready/implementing/verify/done)"},
                "title": {"type": "string"},
                "content": {"type": "string"},
                "owner": {"type": "string"},
                "risk": {"type": "string"}
            },
            "required": ["id"]
        }
    },
    {
        "name": "delete_card",
        "description": "刪除指定的任務卡片。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "string", "description": "要刪除的卡片 ID"}
            },
            "required": ["id"]
        }
    },
    {
        "name": "get_epics",
        "description": "取得看板中所有 Epics 與 User Stories 列表。",
        "inputSchema": {
            "type": "object",
            "properties": {}
        }
    }
]


def load_all_cards():
    cards = []
    if os.path.exists(CARDS_DIR):
        for fpath in glob.glob(os.path.join(CARDS_DIR, "*.json")):
            try:
                with open(fpath, "r", encoding="utf-8") as f:
                    card = json.load(f)
                    cards.append(card)
            except Exception:
                pass
    cards.sort(key=lambda c: c.get("id", ""))
    return cards


def handle_tool_call(name, args):
    if name == "get_kanban_status":
        cards = load_all_cards()
        stages = {}
        for c in cards:
            st = c.get("stage", "unknown")
            stages[st] = stages.get(st, 0) + 1
        return {
            "total": len(cards),
            "stages": stages,
            "cardsSummary": [{"id": c.get("id"), "title": c.get("title"), "stage": c.get("stage")} for c in cards]
        }

    elif name == "list_cards":
        cards = load_all_cards()
        filtered = []
        for c in cards:
            if "stage" in args and c.get("stage") != args["stage"]:
                continue
            if "epic" in args and args["epic"] not in c.get("epic", ""):
                continue
            if "owner" in args and c.get("owner") != args["owner"]:
                continue
            if "risk" in args and c.get("risk") != args["risk"]:
                continue
            filtered.append(c)
        return {"count": len(filtered), "cards": filtered}

    elif name == "get_card":
        card_id = args.get("id")
        fpath = os.path.join(CARDS_DIR, f"{card_id}.json")
        if os.path.exists(fpath):
            with open(fpath, "r", encoding="utf-8") as f:
                return json.load(f)
        return {"error": f"找不到卡片 {card_id}"}

    elif name == "create_card":
        card_id = args.get("id")
        fpath = os.path.join(CARDS_DIR, f"{card_id}.json")
        os.makedirs(CARDS_DIR, exist_ok=True)
        card_data = {
            "id": card_id,
            "title": args.get("title", ""),
            "content": args.get("content", ""),
            "stage": args.get("stage", "backlog"),
            "risk": args.get("risk", "medium"),
            "owner": args.get("owner", "shuotao"),
            "agent": "antigravity",
            "epic": args.get("epic", ""),
            "userStory": args.get("userStory", ""),
            "createdAt": "2026-08-03"
        }
        with open(fpath, "w", encoding="utf-8") as f:
            json.dump(card_data, f, ensure_ascii=False, indent=2)
        return {"success": True, "message": f"成功建立卡片 {card_id}", "card": card_data}

    elif name == "update_card":
        card_id = args.get("id")
        fpath = os.path.join(CARDS_DIR, f"{card_id}.json")
        if not os.path.exists(fpath):
            return {"error": f"找不到卡片 {card_id}"}
        with open(fpath, "r", encoding="utf-8") as f:
            card_data = json.load(f)

        for key in ["stage", "title", "content", "owner", "risk"]:
            if key in args:
                card_data[key] = args[key]

        with open(fpath, "w", encoding="utf-8") as f:
            json.dump(card_data, f, ensure_ascii=False, indent=2)
        return {"success": True, "message": f"成功更新卡片 {card_id}", "card": card_data}

    elif name == "delete_card":
        card_id = args.get("id")
        fpath = os.path.join(CARDS_DIR, f"{card_id}.json")
        if os.path.exists(fpath):
            os.remove(fpath)
            return {"success": True, "message": f"已刪除卡片 {card_id}"}
        return {"error": f"找不到卡片 {card_id}"}

    elif name == "get_epics":
        cards = load_all_cards()
        epics = {}
        for c in cards:
            ep = c.get("epic", "未分類")
            if ep not in epics:
                epics[ep] = set()
            if c.get("userStory"):
                epics[ep].add(c["userStory"])
        return {ep: list(stories) for ep, stories in epics.items()}

    else:
        raise Exception(f"未知的工具: {name}")


def main():
    # 強制 stdin/stdout 使用 utf-8
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")

    while True:
        try:
            line = sys.stdin.readline()
            if not line:
                break
            line_str = line.strip()
            if not line_str:
                continue

            # 處理 Content-Length header (若 MCP client 使用標頭格式)
            if line_str.lower().startswith("content-length:"):
                length = int(line_str.split(":")[1].strip())
                sys.stdin.readline() # blank line
                body = sys.stdin.read(length)
                req = json.loads(body)
            else:
                req = json.loads(line_str)

            method = req.get("method")
            msg_id = req.get("id")

            if method == "initialize":
                resp = {
                    "jsonrpc": "2.0",
                    "id": msg_id,
                    "result": {
                        "protocolVersion": "2024-11-05",
                        "capabilities": {"tools": {}},
                        "serverInfo": {"name": "monstrare-kanban-server", "version": "1.0.0"}
                    }
                }
            elif method == "notifications/initialized":
                continue
            elif method == "tools/list":
                resp = {
                    "jsonrpc": "2.0",
                    "id": msg_id,
                    "result": {"tools": TOOLS}
                }
            elif method == "tools/call":
                params = req.get("params", {})
                t_name = params.get("name")
                t_args = params.get("arguments", {})
                try:
                    result_data = handle_tool_call(t_name, t_args)
                    resp = {
                        "jsonrpc": "2.0",
                        "id": msg_id,
                        "result": {
                            "content": [{"type": "text", "text": json.dumps(result_data, ensure_ascii=False, indent=2)}]
                        }
                    }
                except Exception as e:
                    resp = {
                        "jsonrpc": "2.0",
                        "id": msg_id,
                        "result": {
                            "isError": True,
                            "content": [{"type": "text", "text": f"Error: {str(e)}"}]
                        }
                    }
            elif method == "ping":
                resp = {"jsonrpc": "2.0", "id": msg_id, "result": {}}
            else:
                resp = {
                    "jsonrpc": "2.0",
                    "id": msg_id,
                    "error": {"code": -32601, "message": f"Method not found: {method}"}
                }

            out_bytes = json.dumps(resp, ensure_ascii=False).encode("utf-8")
            sys.stdout.buffer.write(f"Content-Length: {len(out_bytes)}\r\n\r\n".encode("utf-8"))
            sys.stdout.buffer.write(out_bytes)
            sys.stdout.buffer.flush()

        except Exception:
            pass


if __name__ == "__main__":
    main()
