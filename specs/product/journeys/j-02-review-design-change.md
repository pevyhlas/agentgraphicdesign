# J-02 Review a design change (validated)

**Who**: Project maintainer · **Area**: Review and delivery · **Surfaces**: pull request diffs, rendered previews

## Intent

Approve or reject an agent's design change quickly, seeing exactly what changed.

## Situation

The maintainer reads a pull request in the project's Git host. They may not be a designer and
have only the repository's files, its CI results and rendered previews to judge by.

## Scenario

1. They open the pull request and read the source diff: a property edit is one changed line,
   and moving a node changes only that node's ordering value.
2. They look at the rendered previews or exports the agent attached or CI produced.
3. For pixel art, whose payloads do not diff as text, they rely on previews and the read-only
   export check.
4. They merge, or ask for changes.

## Variations

- **Binary pixel changes**: reviewed through previews and checks, never claimed to be readable
  as text.
- **Committed outputs drift from their sources**: the read-only check reports drift; nothing is
  rewritten.
- **History restored or branches merged with Git**: writers are paused, then sources are
  reloaded and validated before work continues.

## Success

A small, property-level diff plus previews of the result, enough to decide without opening an
editor. For the project: design changes go through the same review as code.
