# Git Workflow

AGD edits ordinary files. Use Git for project history, review, branching and
restoration; neither the CLI nor MCP server requires or manages Git for you.

## What to Track

- Track `.agd.json` sources, asset manifests, recipes/settings, external palettes
  and required source artwork. Tokens embedded in a document are tracked with it.
- Preserve large binary sources with Git LFS or another durable, pinned store.
  Referenced bytes must remain retrievable for every project version you retain.
- Keep original or nonreproducible artwork even if it was generated. A recorded
  digest or recipe alone cannot recover missing bytes.
- For native still assets, track `asset.agd-pixels.json`, its referenced immutable
  `payloads/*.bin`, and all chosen `imports/<recordSha256>/` original bundles/records.
  Development source's [still importer](native-still-import.md) preserves historical
  inputs inside the asset; current native layer references remain authoritative.
- Generated exports can be tracked when consumers need them; otherwise ignore
  outputs that can actually be rebuilt. Do not blanket-ignore required artwork.
- Ignore local lock markers and temporary writes. For conventional document names,
  these narrow patterns avoid ignoring project sources or NuGet lockfiles:

```gitignore
.agd-store.lock
*.agd.json.lock
*.agd.json.*.tmp
```

If you use a different source extension, ignore its specific lock/temp sidecars.
Do not ignore an entire document-store directory: it may contain authoritative
sources. Do not delete lock markers while writers are active.

Future explicit chosen asset references belong in tracked project declarations.
The current asset manifest has no new selection field; no standalone selection
store or command is introduced by this decision.

## Edit and Review

Use the normal inspect/edit/validate/preview loop, inspect the source diff, then
stage and commit the files you intend to preserve. AGD never runs Git commands on
your behalf. For a project with `design.agd.json` and `assets.json`, verification is:

```sh
agd validate design.agd.json
agd assets build assets.json
agd assets check assets.json
git diff -- design.agd.json assets.json
```

`build` writes configured outputs; `check` is read-only and reports missing or
different outputs. Neither is a Git operation or a coherent multi-file transaction.

## Restore and Resume

Pause AGD writers before switching branches, restoring files or resolving merges.
Git does not take AGD's file locks. Restore the required payloads too, including
LFS objects or externally retained content; then reload documents, validate, and
rebuild/check derived outputs as appropriate. Resolve source conflicts before editing.

The engine increments `revn` during edits, but Git can restore an older value and
different branches can reuse the same number. Discard pre-restore operation plans
and cached revisions. Read the current content and use its `expectedRevn` for the
next mutation; that check is not protection against concurrent Git operations or
a globally unique content token.

Native pixels use the complete `{assetId,revn,sourceDigest}` snapshot. Re-inspect after
restoration and include all three fields in the next edit/import-layer request. A
different current closure rejects even at the same revision; an exactly restored earlier
token can become eligible again. This is state CAS, not anti-rollback or a retry receipt.

Git history is not native semantic undo/redo or durable request deduplication. If
a mutation's reply is lost, inspect the current document before deciding what to
do; do not assume the write failed and blindly repeat it. Stronger restart/retry
and publication guarantees remain separate future capabilities.
