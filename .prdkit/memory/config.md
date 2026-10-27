# Optional flow configuration

Unset values and absent files use the ordinary protocol. These toggles do not grant
authority or weaken verification. Engineering policy lives in the constitution;
external event delivery lives in task-ledger.md.

## Configuration

- retrospective: off
- agent-optimization: off
- adoption-manifest-sha256: c26db47e07d676dfd428a75fe3cb7c84a3f428aec3b14998d071aac79e5c635e

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

Product-Kit v1.2.1 (source commit `589589aeae2791d7e509214b9fb82b014bd59f82`) is
vendored in copy mode. There is no gitlink or `adoption-pin`; clones and CI need no kit
checkout. Upgrade from the public tag with `init.sh --upgrade --copy`, preserve project
policy, and update the manifest hash and third-party notices in the same change.
