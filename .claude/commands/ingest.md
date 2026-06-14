# /ingest - Personal Vault Ingest

Run the personal knowledge vault Ingest operation. This is a thin adapter: the
authoritative definition lives in `vault/CLAUDE.md` (section 操作 / Ingest).

## Behavior

1. If `vault/CLAUDE.md` does not exist, stop and tell the user their personal
   vault is not set up yet. Point them to
   `docs/BIM_MCP/reference/personal-llm-wiki.html` (step 7: copy the build
   prompt) and do nothing else.
2. Read `vault/CLAUDE.md` and follow its Ingest definition exactly. In short:
   `git pull` at the repo root, find files changed since the last ingest via
   `git diff`, digest only the changed files into `vault/wiki/`, update
   `vault/index.md`, append to `vault/log.md`.
3. Obey the vault disciplines: never write outside `vault/`, keep provenance
   (`source` / `source_version`) on every wiki page, never modify upstream files.

## Scope Guard

This command operates on the user's personal vault only. It is not part of
project development; do not append to the upstream `log/` and do not run QA/QC.
