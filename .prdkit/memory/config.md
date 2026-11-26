# Optional flow configuration

Unset values and absent files use the ordinary protocol. These settings do not grant
authority or weaken verification. Engineering policy lives in the constitution.

## Configuration

- adoption-manifest-sha256: c26db47e07d676dfd428a75fe3cb7c84a3f428aec3b14998d071aac79e5c635e

## Meaning

Adoption-gate keys (all optional; used by `.prdkit/scripts/bash/check-adoption.sh`, a
one-line CI step that verifies the reviewed adoption contract): `adoption-pin` records
the expected kit submodule commit (full 40-hex), `adoption-manifest-sha256` the expected
manifest hash, and `adoption-source` the kit source path when it is not `.product-kit`.
Add them to the Configuration section above and update them together on every kit bump.

Product-Kit v3.1.5 (source commit `2ffad1d81364c12d794fff18b7b52f96157c1c9e`) is
vendored in copy mode. There is no gitlink or `adoption-pin`; clones and CI need no kit
checkout. Upgrade from the public tag with `init.sh --upgrade --copy`, preserve project
policy, and update the manifest hash and third-party notices in the same change.
