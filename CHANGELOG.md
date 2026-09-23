# Changelog

All notable changes to Subscrio Payments are recorded here.

## [Unreleased]

## [0.5.0] - 2026-09-23

### Changed

- TypeScript supports Subscrio 0.4.x and 0.5.x, with tests against 0.5.0.
- .NET uses published Subscrio.Core 0.5.1 or later; local core builds remain available through UseLocalSubscrioCore.

### Verified

- Stripe payment recording and duplicate handling pass with the updated core dependencies.

## [0.4.0] - 2026-09-20

### Changed

- The TypeScript package now requires a compatible `subscrio` 0.4.x release.
- The TypeScript and .NET packages are versioned together with the 0.4.0 core release.

### Verified

- Stripe invoice payment mapping remains compatible with Subscrio 0.4.0 event handling.
