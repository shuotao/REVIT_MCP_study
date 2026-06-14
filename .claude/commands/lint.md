# /lint - Personal Vault Health Check

Run the personal knowledge vault Lint operation. This is a thin adapter: the
authoritative definition lives in `vault/CLAUDE.md` (section 操作 / Lint).

## Behavior

1. If `vault/CLAUDE.md` does not exist, stop and tell the user their personal
   vault is not set up yet. Point them to
   `docs/BIM_MCP/reference/personal-llm-wiki.html` (step 7: copy the build
   prompt) and do nothing else.
2. Read `vault/CLAUDE.md` and follow its Lint definition exactly. In short, check:
   (a) wiki pages whose `source_version` is behind the upstream file,
   (b) contradictions between wiki pages,
   (c) orphan pages with no inbound links,
   (d) findings worth contributing upstream — compile a proposal list
       (related links to add, missing legal references, new applications;
       hand off to `/hj-pr-proposal` when the user wants to file a PR),
   (e) whether the Fixed Core `schema_version` in `vault/CLAUDE.md` is behind
       `templates/personal-vault/VAULT-CLAUDE.md` — if so, offer the upgrade
       (replace Fixed Core wholesale, keep the Personal section).
3. Report results per item. Fix nothing without the user's confirmation.

## Scope Guard

This command operates on the user's personal vault only. It is not the
repository QA/QC gate — that is `/qaqc`.
