# Optional flow configuration

Unset values and absent files use the ordinary protocol. These toggles do not grant
authority or weaken verification. Engineering policy lives in the constitution;
external event delivery lives in task-ledger.md.

## Configuration

- retrospective: off
- agent-optimization: off
- adoption-manifest-sha256: 0ecd8b5cdf14c87d2d8ecdcc7fa35267b6038bab193416af62fed04f9f273918

## Meaning

`retrospective: on` requests a delivery retrospective after confirmed integration using
the local ledger. Missing optional enrichment leaves usage unknown. Failure is advisory.

`agent-optimization: on` permits a suggestion to review capability/model effectiveness
after learning. Applying proposed configuration changes still needs applicable authority.

Adoption-gate keys (all optional; used by `.prdkit/scripts/bash/check-adoption.sh`, a
one-line CI step that verifies the reviewed adoption contract): `adoption-pin` records
the expected kit submodule commit (full 40-hex), `adoption-manifest-sha256` the expected
manifest hash, and `adoption-source` the kit source path when it is not `.product-kit`.
Add them to the Configuration section above and update them together on every kit bump.
