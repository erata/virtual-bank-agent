> **Historical milestone / patch note.** This file documents an earlier revision and may include obsolete defaults or expected test counts. For the CURRENT reference release, see [`README`](../README.md), [`architecture`](architecture.md), [`quality gate`](PUBLICATION_QUALITY_GATE.md) and [`validation status`](VALIDATION_STATUS.md).

# Security dependency upgrade — 2026-11-22

## Problem
Docker build (`dotnet restore`) stopped with `NU1904` because the central package version was `Microsoft.SemanticKernel 1.60.0`. Its transitive `Microsoft.SemanticKernel.Core 1.60.0` is affected by critical security advisory GHSA-2ww3-72rp-wpp4 (CVE-2026-25592).

## Patch
Updated `Directory.Packages.props`: `Microsoft.SemanticKernel` **1.60.0 → 1.71.0**. NuGet publishes version 1.71.0 as compatible with .NET 10; it depends on `Microsoft.SemanticKernel.Core >= 1.71.0`, the version family fixed for this advisory.

Security auditing and `TreatWarningsAsErrors=true` have **not** been disabled.

## Verify on Windows PowerShell (from repository root)

```powershell
dotnet restore .\src\VirtualBank.Web\VirtualBank.Web.csproj
dotnet list .\src\VirtualBank.Web\VirtualBank.Web.csproj package --include-transitive
dotnet build .\src\VirtualBank.Web\VirtualBank.Web.csproj -c Release
docker compose build --no-cache api
docker compose up -d --wait
```

If a different restore/build error appears, preserve its log instead of suppressing vulnerability warnings. After the backend starts, run the normal integration, Keycloak and RAG checks. Real build and Docker validation are **not yet performed** in the packaging environment.

## References
- https://github.com/advisories/GHSA-2ww3-72rp-wpp4
- https://www.nuget.org/packages/Microsoft.SemanticKernel/1.71.0

## Scope
Only the centrally pinned Semantic Kernel version was changed in application configuration. The rest of the repository was preserved. A historical ADR mentioning the original package version is left unchanged.
