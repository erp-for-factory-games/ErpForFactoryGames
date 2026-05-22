# Project costs

Costs are paid out-of-pocket by [@ChrisonSimtian](https://github.com/ChrisonSimtian).
Sponsorship via the [GitHub Sponsors button](https://github.com/sponsors/ChrisonSimtian)
helps offset these — this page is here so anyone considering sponsoring can see exactly
what their contribution covers.

This is a list of *current, recurring* costs. One-offs (e.g. dev hardware) are not
tracked here.

| Item | Cost (NZD) | Cost (original currency) | Frequency | Notes |
|------|-----------:|-------------------------:|-----------|-------|
| Domain — `erp-for-factory.games` | ~$44 | USD $26.20 | Yearly | Cloudflare registrar, at-cost pricing for `.games` TLD. Registered 2026-05-21 ([ADR-0020](docs/adr/0020-rebrand-to-erp-for-factory-games.md)). NZD figure is approximate at ~1.68 NZD/USD; refresh from the next Cloudflare invoice. |

**Total recurring**: ~NZD $44/year (USD $26.20/year)

## Not currently incurred

These are foreseeable but not yet paid:

- **App hosting** — the app isn't deployed anywhere yet. When it lands, expect a
  homelab option (zero cost; runs on Chris's existing Proxmox cluster) or a
  small VPS / managed hosting tier.
- **CI minutes** — GitHub-hosted runners on the free tier suffice for the current
  workload ([PR #152](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/152)).
  Listed here so the line item appears the moment we exceed it.
- **NuGet GitHub Packages bandwidth** — free for public packages.

## Wishlist — what sponsorship would unlock

These are real items the project would benefit from but **can't currently
justify out-of-pocket**. They're listed here so anyone considering sponsoring
can see exactly what their contribution would buy back for users.

| Item | Cost (NZD) | Cost (original currency) | Frequency | What it unlocks |
|------|-----------:|-------------------------:|-----------|-----------------|
| Apple Developer Program | ~$165 | USD $99 | Yearly | macOS `.pkg` notarization → installer opens without the Gatekeeper "unidentified developer" warning. Tracked in [#223](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/223). |
| Windows Authenticode cert (OV) | ~$335 | USD $200 | Yearly | Windows MSI carries a real publisher identity → SmartScreen reputation builds over a few weeks of downloads. Tracked in [#223](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/223). |
| Windows Authenticode cert (EV) | ~$670 | USD $400 | Yearly | *Alternative* to OV above (not additive): immediate SmartScreen trust day one. Useful if there's a sponsor-funded launch push. |

Pick the OV path if any sponsorship lands; the EV upgrade is a stretch goal.

**If everything on the wishlist were funded**: ~NZD $500/year added to the
current ~$44/year domain spend (OV path), or ~$835/year (EV path).

## Keeping this current

This list is the *current* state. When a new cost arises (or an existing one
changes), update the table in the same PR as the change. Stale cost lists are
worse than missing ones.
