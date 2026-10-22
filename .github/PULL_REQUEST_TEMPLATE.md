<!--
Open this pull request as a draft. Only the maintainer merges, and review is best effort.
For anything beyond a small fix, open an issue first and agree on the change there.
-->

## What and why

Closes #

## Checklist

- [ ] Opened as a draft, and linked to its issue above (a small fix may skip the issue)
- [ ] `dotnet build FTPlus.AgentGraphicDesign.slnx --configuration Release` and `dotnet test FTPlus.AgentGraphicDesign.slnx --configuration Release` pass (Linux x64 is the qualified profile)
- [ ] `dotnet format FTPlus.AgentGraphicDesign.slnx --verify-no-changes` is clean
- [ ] `bash scripts/validate-local.sh` passes, or CI is green on this pull request
- [ ] User-visible changes are noted under `## Unreleased` in `CHANGELOG.md`; versions are left alone
- [ ] New or updated packages follow the dependency policy in `CONTRIBUTING.md`; specs in `specs/` match any changed guarantee
