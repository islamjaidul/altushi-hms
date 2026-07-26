# 0008 — Device integration: local connector-agent contract (Q4)

- **Status:** Accepted
- **Note:** design seam now; connectors are Phase 2 builds
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q4 (§13 inventory)

## Context

§13: Phase 1 devices are printers and barcode scanners only; analyzers (I1), biometrics (I8), DICOM MWL (I9) arrive Phase 2 when machines exist (§9A.3). Q4's constraint: mixed brands per site, new analyzer models constantly — adding one must not require a product release.

## Options considered

| Option | Pros | Cons | RAM cost (MVP) |
|---|---|---|---|
| Drivers inside the app | Fewer parts | Every new analyzer = app release; serial-port access from a container is deployment pain | 0 |
| **Separate connector agent per site (chosen seam)** | Device chaos isolated; connectors ship independently of the product; agent can run on the PC physically wired to the device | One more deployable at Phase 2 sites | 0 in MVP (agent doesn't exist yet) |

## Decision

**MVP devices need no agent:** barcode scanners are keyboard-wedge input (handled by the UI's scan-capture grammar, `05-ui-architecture.md`); label/thermal/A4 printing goes through the browser print pipeline (ADR-0009). SMS gateway is plain outbound HTTP from the app (I7) with simulation mode.

**The Phase-2 seam, fixed now:** the app exposes a versioned local **device API** (authenticated by per-agent token): `GET worklist` (paid orders for a device class) and `POST observations` (results keyed by **sample barcode**, landing in an exception queue when unmatched — §13 I1). A small connector agent (its own codebase, .NET, runs on Windows/Linux beside the device) translates between that API and device protocols (ASTM/HL7 serial or TCP for analyzers, DICOM MWL for modalities, ZKTeco-class SDK/CSV pulls for biometrics). New device model ⇒ new/updated connector plug-in, product untouched.

## Consequences

- MVP ships zero device code beyond printing/scanning, but the LIS data model already stores results as *observations against samples* — exactly the shape the connector will post, so analyzer arrival changes no schema.
- The result-entry screen is the same one used by the connector path (auto-filled, technologist confirms), matching the design reference's analyzer-assisted flow.

## Reversal trigger

If Phase-2 field reality shows most analyzers at target sites speak modern HTTP/HL7-FHIR directly, fold those into the app's device API and reserve agents for serial-legacy hardware only.
