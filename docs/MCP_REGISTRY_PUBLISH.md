# MCP Registry Publish Playbook

Authoritative, command-first playbook to make the MCP Registry publish
**succeed on the first try** for this repo.

> **Consistency ownership.** The publish artifacts below (`server.json`,
> `MCP-Server/package.json`, this playbook) are kept in sync by two mandated
> **Sonnet** subagents — `.claude/agents/mcp-registry-sync.md` (fixes drift) and
> `.claude/agents/mcp-registry-ops-inspect.md` (read-only audit). Whenever a
> registry "main file" changes, run sync → inspect, then the QAQC hard gate.
> See CLAUDE.md § *MCP Registry Publish Consistency*. Never hand-edit a version
> backwards, and never `npm publish` manually — release only by pushing a `v*` tag.

| Fact | Value |
| --- | --- |
| Repo root | `<YOUR_PROJECT_PATH>` (wherever you cloned this repo; no fixed machine path) |
| GitHub repo | https://github.com/shuotao/REVIT_MCP_study |
| GitHub owner | `shuotao` |
| Registry server name | `io.github.shuotao/revit-mcp-server` |
| npm package | `@shuotao/revit-mcp-server` (scoped, public) |
| Current version | `1.6.0` |
| Metadata file | `server.json` (repo root) |
| npm package dir | `MCP-Server/` (entry `build/index.js`, transport `stdio`) |
| Auto-publish | `.github/workflows/publish-mcp.yml` (trigger: `v*` tag) |
| Consistency gate | `scripts/validate_publish_consistency.py` |

---

## 1. Overview

The **MCP Registry** is a public catalog of Model Context Protocol servers.
Key properties that drive this playbook:

- **Metadata only, not artifacts.** The Registry stores your `server.json`
  (name, description, repository, and a *pointer* to where the package lives).
  It does **not** host the code. The actual artifact — the npm package
  `@shuotao/revit-mcp-server` — lives on the npm registry. Publishing to the
  MCP Registry therefore requires the npm package to already be published.
- **The entry does NOT auto-track the repo.** The Registry takes a snapshot of
  `server.json` at publish time. Pushing new commits or a new npm version does
  **not** update the Registry entry. **Each release must be re-published.**
  This repo solves that with the **tag-triggered GitHub Actions workflow**:
  push a `vX.Y.Z` tag and the workflow re-publishes both npm and the Registry.
- **Namespace ownership is proven via GitHub.** The `io.github.shuotao/*`
  namespace is authorized by proving control of the `shuotao` GitHub account
  (OIDC in CI, or interactive OAuth locally).

---

## 2. One-time setup checklist

Do these **once** before the first publish.

- [ ] **Create an npm Automation token.**
  npmjs.com → avatar → *Access Tokens* → *Generate New Token* →
  **Automation** (bypasses 2FA, safe for CI).
- [ ] **Add it as a repo secret named `NPM_TOKEN`.**
  GitHub repo → *Settings* → *Secrets and variables* → *Actions* →
  *New repository secret* → Name `NPM_TOKEN`, value = the token.
  The workflow reads it as `NODE_AUTH_TOKEN` during `npm publish`.
- [ ] **Confirm the `@shuotao` npm scope is available / owned.**
  ```bash
  npm login                     # as user shuotao
  npm whoami                    # -> shuotao
  npm access list packages @shuotao   # scope reachable (may be empty pre-publish)
  ```
  If `@shuotao` is taken by someone else, the scoped name cannot be published —
  stop and resolve ownership first.
- [ ] **Confirm you control the `shuotao` GitHub account** (needed to prove the
  `io.github.shuotao` namespace). No action beyond being able to log in.

---

## 3. First publish — two paths

Pick **one**. Path A is the intended day-to-day flow; Path B is the manual,
human-gated fallback for debugging or a first dry run.

### Path A — Automated (recommended)

Everything runs in CI. Prerequisite: `NPM_TOKEN` secret is set (section 2).

```bash
cd <YOUR_PROJECT_PATH>
git tag v1.0.0
git push origin v1.0.0
```

The workflow (`.github/workflows/publish-mcp.yml`) then:

1. Syncs the version from the tag into `server.json` and `MCP-Server/package.json`.
2. Runs `scripts/validate_publish_consistency.py` as a **hard gate**.
3. `npm ci` + `npm run build` in `MCP-Server/`.
4. `npm publish` (auth via `NPM_TOKEN`).
5. Installs `mcp-publisher`, authenticates with **GitHub OIDC**
   (`mcp-publisher login github-oidc` — no secret needed), then
   `mcp-publisher publish` (reads root `server.json`).

Watch it: GitHub repo → *Actions* → latest *Publish to MCP Registry* run.

### Path B — Manual (human-gated, run locally)

Run from the repo root. Requires an interactive terminal (npm login + GitHub
OAuth). Use this to validate the pipeline once, or when CI is unavailable.

```bash
cd <YOUR_PROJECT_PATH>

# 0. Gate first — never publish if this fails
python3 scripts/validate_publish_consistency.py

# 1. Publish the npm artifact
cd MCP-Server
npm login                       # if not already logged in
npm publish --access public     # --access public required for a scoped pkg
cd ..

# 2. Install the Registry CLI (pick one)
brew install mcp-publisher
# --- or the curl one-liner ---
curl -L "https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher_$(uname -s | tr '[:upper:]' '[:lower:]')_$(uname -m | sed 's/x86_64/amd64/;s/aarch64/arm64/').tar.gz" | tar xz mcp-publisher

# 3. Prove io.github.shuotao ownership (opens browser, GitHub OAuth)
mcp-publisher login github

# 4. Publish metadata (reads root server.json)
mcp-publisher publish
```

Order matters: **npm publish before `mcp-publisher publish`** — the Registry
validates that the referenced npm package/version exists.

---

## 4. Success verification

```bash
# npm artifact is live
open https://www.npmjs.com/package/@shuotao/revit-mcp-server
npm view @shuotao/revit-mcp-server version      # -> 1.6.0 (current)

# Registry entry is live
curl -s "https://registry.modelcontextprotocol.io/v0/servers?search=io.github.shuotao/revit-mcp-server" | jq .

# Or check CLI-reported status
mcp-publisher status io.github.shuotao/revit-mcp-server
```

Expect the Registry response to contain `io.github.shuotao/revit-mcp-server`
with the current `version` (`1.6.0`) and a `packages[]` entry pointing at
`@shuotao/revit-mcp-server` on npm.

---

## 5. Updating later

The Registry does not follow the repo — **every release is a re-publish**,
driven by a tag.

```bash
# 1. Bump the version everywhere the validator checks (keep all 3 in sync):
#    - server.json           .version  AND  .packages[].version
#    - MCP-Server/package.json  .version
#    (In Path A, the workflow's "Sync version from tag" step rewrites these
#     from the tag, so the tag is the source of truth.)

# 2. Tag and push
git tag v1.1.0
git push origin v1.1.0
```

The **version must stay consistent in 3 places**. Both the validator
(`scripts/validate_publish_consistency.py`) and the workflow's `Sync version
from tag` step enforce this; a mismatch fails the gate before anything ships.

---

## 6. Rollback / deprecate

The Registry has **no true delete** — you deprecate, you do not remove.

```bash
# Mark a server entry deprecated in the Registry
mcp-publisher status io.github.shuotao/revit-mcp-server   # inspect current state
# then publish an updated server.json with the deprecation status,
# or use the CLI's deprecate flow, and re-run:
mcp-publisher publish
```

**npm unpublish limitations** (npm-side, separate from the Registry):

- A version can be unpublished only within **72 hours** of publishing.
- After 72 hours you cannot unpublish; use `npm deprecate` instead:
  ```bash
  npm deprecate @shuotao/revit-mcp-server@1.0.0 "Use >=1.1.0"
  ```
- An unpublished version number **cannot be reused** — bump to a new version.
- Because the Registry only *points* at npm, unpublishing the npm package
  leaves a dangling Registry entry. Prefer deprecate over unpublish.

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `mcpName` mismatch / "name does not match package" | `package.json` `mcpName` != `server.json` `name` | Set `MCP-Server/package.json` `mcpName` = `io.github.shuotao/revit-mcp-server`; re-run the validator. |
| "namespace not authorized" / prefix mismatch | `server.json` `name` not prefixed `io.github.shuotao/`, or logged in as a different GitHub user | Ensure `name` starts with `io.github.shuotao/`; `mcp-publisher login github` as `shuotao` (or OIDC in CI). |
| npm publish 401 / `ENEEDAUTH` in CI | `NPM_TOKEN` secret missing, expired, or wrong scope | Recreate an **Automation** token with `@shuotao` publish rights; reset the `NPM_TOKEN` repo secret. |
| `mcp-publisher` auth "token expired" / 401 (JWT) | Registry login JWT is short-lived and stale | Re-authenticate: `mcp-publisher login github` (local) or re-run the OIDC step in CI, then re-publish. |
| Registry publish rejects package | npm version not yet live | Publish npm **first**; wait for it to appear, then `mcp-publisher publish`. |
| Validator fails on version | 3-place version drift | Align `.version` in `server.json`, `server.json` `.packages[].version`, and `package.json`; or let the tag-sync step set them. |
| Validator flags mojibake / BOM | Non-UTF-8 or smart quotes in identifiers | Save files as UTF-8 (no BOM); remove curly quotes / zero-width / NBSP from JSON values. |

---

### Quick reference

```bash
# Full automated release
git tag vX.Y.Z && git push origin vX.Y.Z

# Manual, gated
python3 scripts/validate_publish_consistency.py
(cd MCP-Server && npm publish --access public)
mcp-publisher login github && mcp-publisher publish
```
