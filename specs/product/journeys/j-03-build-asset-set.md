# J-03 Build the asset set (validated)

**Who**: Project maintainer, or the agent acting for them · **Area**: Review and delivery · **Surfaces**: CLI, CI

## Intent

Generate every export and sprite sheet the project ships from one declaration, and learn in CI
when committed outputs no longer match their sources.

## Situation

The project already has design sources in its repository and wires the outputs into its own
application. It wants no project-specific export, recolouring, sprite or drift-check scripts.

## Scenario

1. They declare the project's exports and sprite sheets in one asset manifest.
2. They run the build, which writes exactly the declared outputs.
3. CI runs the check, which reads only and reports drift as a distinct result.

## Variations

- **An output path the manifest does not own**: refused, nothing written there.
- **Outputs left over from earlier declarations**: reported, never deleted.
- **A malformed manifest**: a structured error before any work.
- **Off the qualified profile**: the file-safety probe fails closed with a platform error,
  except on Windows, which it lets through unqualified (Q-05, P1.I-09).

## Success

The asset set builds and checks with AGD alone; the project keeps only its framework wiring,
accessibility and visual approval.
