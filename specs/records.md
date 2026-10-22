# Records

<!-- One line each, newest first. Agents record a HOLD or WAIVER only when the owner decided it,
with the owner's quote and date in the reason; never invent one. Deploy workflows, or the agent
that observed a deployment, add DEPLOY lines. A value runs to the next key=; the reason follows
" — ".
HOLD   <id> owner=<role> scope=<part|env|change> since=<date> until=<date|condition> — <reason>
WAIVER <id> owner=<role> check=<acceptance item> scope=<scope> expires=<date> — <reason>
ATTEST <id> by=<owner|agent> what=<claim> env=<env> version=<rev> date=<date> — <evidence link>
DEPLOY <app> env=<env> rev=<commit> date=<date> by=<workflow run link>
DEPLOY <app> env=<env> rev=none date=<date> by=<owner|agent> — <why nothing is deployed there> -->

DEPLOY agd env=LOCAL rev=none date=2026-10-22 by=agent — AGD ships as tools users install themselves from a release bundle or from source; there is no hosted, server or SaaS deployment
ATTEST v0.3.0-publication by=agent what=stable v0.3.0 release assets match the qualified bundle env=github-release version=v0.3.0 date=2026-10-01 — the release record and its source revision are kept in the pre-public history (specs/archive.md)
