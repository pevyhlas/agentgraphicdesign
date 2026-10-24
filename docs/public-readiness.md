# Public readiness

AGD is an **experimental public preview**, qualified on **.NET 10 Linux x64**. Public source
is not a published binary release, a completed full editor, or proof of macOS qualification.

## What is already in place

- Public MIT source, exact dependency approvals, notices and a private vulnerability-report path.
- GitHub-hosted fork-safe CI, SHA-pinned actions, read-only tokens and no PR secrets.
- Core, CLI, MCP and Skia test jobs; combined coverage; dependency/vulnerability/format gates;
  package smoke, readiness artifacts, workflow policy and Product-Kit integrity checks.
- Active main/release-tag rulesets. Main requires all eight Linux checks; the scheduled/manual
  macOS lane is informational and not a merge requirement.
- Public Product-Kit **1.1.1**, vendored with manifest integrity and full upstream notices.
- Exact SDK **10.0.113**, including the repository audit check and its fixtures.

The baseline main run passed the required Linux checks:
[GitHub Actions](https://github.com/pevyhlas/agentgraphicdesign/actions/runs/39885425381).
It predates this maintenance and does not qualify it. Maintenance exact-head CI is **pending**:
the current GitHub App credential cannot push to this repository (403). No PR or release is
claimed; restore repository access, publish the branch and run every required Linux check.

## What “ready” means next

| Outcome | Remaining work | Source / authority |
|---|---|---|
| First public release bundle | Select the exact version/source, run clean-archive bundle qualification and authorize the tag/assets | Maintainer; [release process](../REFERENCE.md#release-bundle) |
| Apple silicon support | Finish the platform registry, file-safety/codec qualification and real harness run; choose the hosted macOS runner profile | [Change 008](../specs/changes/008-macos-qualification.md), [inbox](../specs/inbox.md) |
| Agent onboarding | Implement and qualify doctor, MCP setup and discovery increments, after their dependencies | [Change 009](../specs/changes/009-agent-onboarding.md) |
| End-to-end brand/logo kits | Finish geometry, review/legibility, export targets, variants and kit build/check, then run the integrated macOS journey | [Change 013](../specs/changes/013-brand-workflow-uptake.md) and its child changes |
| Complete editor domains | Select and deliver animation/sprites, tiles, artifact admission/preservation and the broader raster editor | [Change 014](../specs/changes/014-full-editor-domains.md); not implemented claims |

The public history already contains the MCP usability and sub-node-token fixes (PRs #5 and #4).
Do not re-port those completed fixes from pre-public branches. Remaining change records still
carry earlier plans; check actual source and the public PR history before resuming a slice.
No consumer adoption or release publication is claimed by this maintenance.

## Public-data hygiene

Keep raw logs, coverage, test results, local runtime files and machine/environment details
out of tracked source. Review images and sample documents as well as text. Preserve upstream
copyright and licence notices, including Inter and SkiaSharp attributions.

Pattern-based checks are regression protection, not a complete secret/PII audit. Review new
files, commit identities and workflow artifacts before publishing. Revoke/rotate any exposed
credential before cleanup, and report privately through [SECURITY.md](../SECURITY.md); never
paste the value in a public issue or assume a file deletion removes it from history.

CI runs `python3 scripts/check-public-repo.py --self-test` and then the same command without
the flag. Python is developer/CI tooling only, not an authoring or product dependency. The
standard-library guard checks common credential signatures, non-example home paths, tracked
runtime/credential files and entry-page links; it prints rule/location only. Binary payloads,
history and other personal data still need separate review. The bounded baseline security
review found no accidental credentials or private identifiers in the inspected content.

## Verification

Run `bash scripts/validate-local.sh` for local equivalents of the Linux gates and
`bash scripts/release-bundle.sh --check` to validate bundle inputs. A real release also needs
the clean-archive bundle run and exact package qualification. Non-Linux authoring remains
unqualified; do not infer support from a build or the informational macOS job.

Product-Kit doctor stays non-strict while owner questions and fresh-history archive advisories
remain. The action policy in the constitution distinguishes permitted PR merges from
maintainer-only release/tag/assets and distribution decisions.
