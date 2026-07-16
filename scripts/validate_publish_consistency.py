#!/usr/bin/env python3
"""
validate_publish_consistency.py

送出到 MCP Registry 前的最後一道校驗閘。
確保 server.json / MCP-Server/package.json 三處(Registry name、npm 套件名、
版本號、repository URL)在「文字、數字、字元、編碼」上完全一致且合法,避免
`mcp-publisher publish` 因為欄位不符或編碼漂移(mojibake)而失敗。

用法:
    python3 scripts/validate_publish_consistency.py

退出碼:
    0 = 全部通過,可安全發佈
    1 = 有不一致 / 不合法,禁止發佈

不需要任何第三方套件。若環境有安裝 jsonschema,會額外做 server.json 的
schema 驗證(schema 檔若存在於 scripts/schemas/server.schema.json)。
"""

from __future__ import annotations

import json
import re
import sys
import unicodedata
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SERVER_JSON = REPO_ROOT / "server.json"
PACKAGE_JSON = REPO_ROOT / "MCP-Server" / "package.json"
SCHEMA_FILE = REPO_ROOT / "scripts" / "schemas" / "server.schema.json"

# GitHub 帳號(命名空間擁有權驗證用)
GITHUB_USER = "shuotao"
EXPECTED_NAMESPACE = f"io.github.{GITHUB_USER}/"
EXPECTED_NPM_SCOPE = f"@{GITHUB_USER}/"

SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$")

errors: list[str] = []
warnings: list[str] = []
oks: list[str] = []


def fail(msg: str) -> None:
    errors.append(msg)


def warn(msg: str) -> None:
    warnings.append(msg)


def ok(msg: str) -> None:
    oks.append(msg)


def read_bytes(path: Path) -> bytes | None:
    if not path.exists():
        fail(f"缺少檔案: {path.relative_to(REPO_ROOT)}")
        return None
    return path.read_bytes()


def check_encoding(path: Path, raw: bytes) -> str | None:
    """回傳解碼後文字;順便檢查 BOM、可疑字元、mojibake。"""
    rel = path.relative_to(REPO_ROOT)

    if raw.startswith(b"\xef\xbb\xbf"):
        fail(f"{rel}: 檔案含 UTF-8 BOM,JSON 應為無 BOM 的純 UTF-8")
        raw = raw[3:]

    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        fail(f"{rel}: 不是合法 UTF-8({e})")
        return None

    ok(f"{rel}: 合法 UTF-8、無 BOM")

    # 常見 mojibake 前導字元(Big5/latin1 誤解 UTF-8 的殘骸)
    for bad in ("Ã", "Â", "â\x80", "ï¿½", "�"):
        if bad in text:
            fail(f"{rel}: 偵測到可能的亂碼/mojibake 片段 {bad!r}")

    # 智慧引號 / 零寬字元 / 不斷行空白 混進 JSON 值會造成比對不一致
    suspicious = {
        "‘": "左單引號", "’": "右單引號",
        "“": "左雙引號", "”": "右雙引號",
        "​": "零寬空白", "‌": "零寬非連字",
        "‍": "零寬連字", "﻿": "零寬不斷行空白",
        " ": "不斷行空白(NBSP)", "　": "全形空白",
    }
    for ch, name in suspicious.items():
        if ch in text:
            warn(f"{rel}: 含可疑字元 {name}({ch!r}),請確認不是混進識別字串")

    return text


def load_json(path: Path):
    raw = read_bytes(path)
    if raw is None:
        return None
    text = check_encoding(path, raw)
    if text is None:
        return None
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        fail(f"{path.relative_to(REPO_ROOT)}: JSON 解析失敗({e})")
        return None


def norm(s: str) -> str:
    """NFC 正規化,避免視覺相同但碼位不同的字串比對失敗。"""
    return unicodedata.normalize("NFC", s)


def assert_equal(label: str, a, b) -> None:
    if a is None or b is None:
        return
    if norm(str(a)) == norm(str(b)):
        ok(f"{label} 一致: {a!r}")
    else:
        fail(f"{label} 不一致: {a!r} != {b!r}")


def main() -> int:
    server = load_json(SERVER_JSON)
    pkg = load_json(PACKAGE_JSON)

    if server is None or pkg is None:
        return report()

    # 取值
    s_name = server.get("name")
    s_version = server.get("version")
    s_repo = (server.get("repository") or {}).get("url")
    packages = server.get("packages") or []

    p_name = pkg.get("name")
    p_mcp = pkg.get("mcpName")
    p_version = pkg.get("version")
    p_repo = (pkg.get("repository") or {}).get("url")

    # 1) 命名空間格式
    if s_name and not str(s_name).startswith(EXPECTED_NAMESPACE):
        fail(f"server.json name 必須以 {EXPECTED_NAMESPACE!r} 開頭(GitHub 驗證),實際 {s_name!r}")
    else:
        ok(f"server.json name 命名空間正確: {s_name!r}")

    if p_name and not str(p_name).startswith(EXPECTED_NPM_SCOPE):
        fail(f"package.json name 必須以 {EXPECTED_NPM_SCOPE!r} 開頭,實際 {p_name!r}")
    else:
        ok(f"package.json name scope 正確: {p_name!r}")

    # 2) Registry name == npm mcpName(硬性規定)
    assert_equal("Registry name <-> package.json mcpName", s_name, p_mcp)

    # 3) 版本號三處一致 + semver 合法
    assert_equal("版本號 server.json <-> package.json", s_version, p_version)
    for v, where in ((s_version, "server.json"), (p_version, "package.json")):
        if v and not SEMVER_RE.match(str(v)):
            fail(f"{where} version 不是合法 semver: {v!r}")

    # 4) packages[] 檢查
    if not packages:
        fail("server.json 缺少 packages[](Registry 需要指向已發佈的套件)")
    for i, p in enumerate(packages):
        rt = p.get("registryType")
        ident = p.get("identifier")
        pv = p.get("version")
        transport = (p.get("transport") or {}).get("type")
        if rt != "npm":
            warn(f"packages[{i}].registryType={rt!r}(本流程預期 npm)")
        assert_equal(f"packages[{i}].identifier <-> package.json name", ident, p_name)
        assert_equal(f"packages[{i}].version <-> server.json version", pv, s_version)
        if transport != "stdio":
            warn(f"packages[{i}].transport.type={transport!r}(預期 stdio)")
        if p.get("registryBaseUrl") not in (None, "https://registry.npmjs.org"):
            fail(f"packages[{i}].registryBaseUrl 對 npm 只能是 https://registry.npmjs.org")

    # 5) repository URL 一致(容忍 git+ 前綴與 .git 後綴)
    def canon_repo(u):
        if not u:
            return u
        return str(u).removeprefix("git+").removesuffix(".git")

    assert_equal("repository URL server.json <-> package.json", canon_repo(s_repo), canon_repo(p_repo))

    # 6) 選配:JSON schema 驗證
    try_schema_validate(server)

    return report()


def try_schema_validate(server) -> None:
    if not SCHEMA_FILE.exists():
        warn(f"未找到 {SCHEMA_FILE.relative_to(REPO_ROOT)},略過 schema 驗證(選配)")
        return
    try:
        import jsonschema  # type: ignore
    except ImportError:
        warn("未安裝 jsonschema,略過 schema 驗證(pip install jsonschema 可啟用)")
        return
    schema = json.loads(SCHEMA_FILE.read_text(encoding="utf-8"))
    try:
        jsonschema.validate(server, schema)
        ok("server.json 通過 JSON schema 驗證")
    except jsonschema.ValidationError as e:  # type: ignore
        fail(f"server.json schema 驗證失敗: {e.message}")


def report() -> int:
    print("=" * 60)
    print("MCP Registry 發佈前一致性校驗")
    print("=" * 60)
    for m in oks:
        print(f"  ✅ {m}")
    for m in warnings:
        print(f"  ⚠️  {m}")
    for m in errors:
        print(f"  ❌ {m}")
    print("-" * 60)
    if errors:
        print(f"結果: 失敗 — {len(errors)} 個錯誤,{len(warnings)} 個警告。禁止發佈。")
        return 1
    print(f"結果: 通過 — 0 錯誤,{len(warnings)} 個警告。可安全發佈。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
