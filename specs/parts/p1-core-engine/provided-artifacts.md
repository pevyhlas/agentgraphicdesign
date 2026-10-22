# P1 Core engine — provided artifacts

**Serves**: J-04; R-07, Q-02, Q-03, Q-05 · **Code**: `src/FTPlus.AgentGraphicDesign.Core/Artifacts/` · Part: [P1 Core engine](../p1-core-engine.md)

## What it does

- Parses, validates and canonicalizes provided-artifact descriptors v1 with a metadata digest
  (D-06); no file reads.
- Preflights a supplied bundle: inventory, lengths, hashes and PNG structure and CRCs, without
  decoding.
- Verifies decoded samples with an original managed RFC 1950/1951 and PNG decoder (D-19),
  reporting raw RGBA hashes.

Detail: the [descriptor](../../../docs/contracts/provided-artifact-v1.md),
[preflight](../../../docs/contracts/artifact-preflight-v1.md) and
[managed verification](../../../docs/contracts/managed-png-verification-v1.md) contracts.

## Behaviors

- **P1.B-23 Strict descriptors**: unknown or duplicate members and invalid values are errors;
  a valid descriptor has one canonical serialization and digest. Test:
  `ArtifactDescriptorContractTests`, `ProvidedArtifactSchemaTests`.
- **P1.B-24 Checks never admit**: preflight and verification results always report
  `admitted=false` and `provenanceVerified=false`, and never write. Test:
  `ArtifactPreflightTests`, `ArtifactVerificationTests`.
- **P1.B-25 One verification per host**: a second concurrent verification is rejected at once
  with `ARTIFACT_VERIFY_BUSY`, never queued; each run has a cooperative deadline and a block
  cap per PNG. Test: `ArtifactVerificationTests`.

## Interface guarantees

- Descriptor v1: strict and versioned. Schema: `schemas/agd-provided-artifact-v1.schema.json`.
- Execution is managed and in process, not an OS sandbox.
- Platform: preflight opens inputs through the shared asset file probe and runs only where
  the probe lets it (P1.I-09, Q-05).

## Failure semantics

- A missing file, a hash or structure mismatch, or an invalid stream is a completed finding or
  a structured error; nothing is decoded through a native codec.

## Left open

- Other image formats or profiles in descriptors, each a new descriptor version.
