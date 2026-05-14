# `.claude/`

Project-specific guidance and tooling for Claude Code in this repo.

## Contents

- [`architecture.md`](architecture.md) — repository layout, namespace convention,
  onion dependency rules. Detailed reference; the top-level `CLAUDE.md` only points here.
- [`agents/`](agents/) — custom subagents available via the Agent tool.
  - [`ada.md`](agents/ada.md) — **ADA**, the in-game assistant. Game knowledge
    (recipes, ratios, milestone unlocks, building specs, layout tips). Live
    factory state is consumed from the running ApiService at `/factory/state`.
    Not for code work.

## Adding things later

- New agent → drop a markdown file in `agents/` with YAML frontmatter (`name`,
  `description`, `tools`). Keep the description action-oriented so Claude knows when
  to delegate to it.
- New shared context file → add it here at the top level and link from this README.
- Slash commands → `commands/<name>.md` (create the folder when first needed).
- Hooks / settings → `settings.json` or `settings.local.json` (the latter is
  gitignored for personal overrides).

Keep this folder lightweight. Anything architecturally significant belongs in an ADR
under `docs/adr/`, not here.
