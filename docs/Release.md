# Release Plan for CloudflareST

Overview
- This document outlines the release strategy for Core.dll (netstandard2.1) and the CLI self-contained release artifacts.

- Versioning
Semantic versioning: MAJOR.MINOR.PATCH (example: 1.0.0)
- Each release should be tagged in git and accompanied by release notes.
- For this repo, the primary surface is Core (netstandard2.1) plus CLI self-contained release artifacts. Unity GUI will reuse Core.dll and will be released via a separate Unity packaging pipeline when GUI project is provided.
- Semantic versioning: MAJOR.MINOR.PATCH (e.g., 1.0.0)
- Each release should tag the repository and generate release notes.

- Artifacts
- Core.dll (netstandard2.1) as the core surface for CLI and GUI integration.
- CLI executable package (self-contained) with a consistent release folder layout.
- CHANGELOG.md should reflect changes per release.
- Core.dll (netstandard2.1) as the core surface for CLI and GUI integration.
- CLI executable package (self-contained or framework-dependent as per CI config) with a consistent release folder layout.
- CHANGELOG.md should reflect changes per release.

- Release Process (high level)
- Ensure all unit tests pass.
- Build Core.dll and CLI, produce artifacts under publish/.
- Update CHANGELOG with the summary of changes.
- Create a GitHub Release with attached artifacts.
- After release, update version in documentation and ensure downstream consumers can fetch the artifacts.
- Ensure all unit tests pass.
- Build Core.dll and CLI, produce artifacts under publish/.
- Update CHANGELOG with the summary of changes.
- Create a GitHub Release with attached artifacts.

Notes
- GUI integration (Unity) will reuse Core.dll; GUI packaging will be handled in a separate release path.
