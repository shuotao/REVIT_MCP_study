# /wiki - Query the Personal Vault Wiki

Answer the user's question from their personal vault wiki. This is a thin
adapter: the authoritative definition lives in `vault/CLAUDE.md`
(section 操作 / Query). Usage: `/wiki <question>`.

## Behavior

1. If `vault/CLAUDE.md` does not exist, stop and tell the user their personal
   vault is not set up yet. Point them to
   `docs/BIM_MCP/reference/personal-llm-wiki.html` (step 7: copy the build
   prompt) and do nothing else.
2. Read `vault/index.md` first to locate relevant pages, then read those pages
   and answer with citations to the wiki pages (and their `source` domain files).
3. If the answer required live Revit data, call the MCP tools and mark clearly
   which facts came from this turn's tool results (data honesty).
4. If the answer produced something durable (a comparison, an analysis, a new
   connection), offer to file it back into `vault/wiki/` as a new page and
   update `vault/index.md` — explorations should compound.
5. If the wiki has no coverage, say so, then answer from `domain/*.md` directly
   and offer to ingest that domain into the wiki.

## Scope Guard

Method questions are answered with `domain/*.md` as the source of truth; the
wiki is the user's personal understanding layer. Flag conflicts, do not
silently rewrite either side.
