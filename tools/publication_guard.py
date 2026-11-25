#!/usr/bin/env python3
"""Stdlib-only repository publication preflight. No network, tokens or Docker required.
Usage: python tools/publication_guard.py
Covers repository hygiene, fixture references, migration-snapshot freeze and docs links.
This is NOT a replacement for dotnet build, real RLS integration or live retrieval evaluation.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ERRORS = []


def check(condition, message):
    if not condition:
        ERRORS.append(message)


def tracked_files():
    try:
        result = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, capture_output=True, check=True)
        if result.stdout:
            return [ROOT / Path(p.decode()) for p in result.stdout.split(b"\0") if p]
    except (FileNotFoundError, subprocess.CalledProcessError):
        pass
    skip = {".git", "node_modules", "dist", "bin", "obj", ".angular", "TestResults", "coverage", "backups"}
    return [p for p in ROOT.rglob("*") if p.is_file() and not set(p.relative_to(ROOT).parts) & skip and p.name != ".env"]


def hygiene():
    ignored = (ROOT / ".gitignore").read_text(encoding="utf-8")
    for entry in (".env", ".env.*", "backups/", "*.bak", "*.dump", "TestResults/"):
        check(entry in ignored, f"Missing .gitignore guard: {entry}")
    files = tracked_files()
    for p in files:
        rel = p.relative_to(ROOT).as_posix()
        check(not (rel == ".env" or rel.startswith("backups/") or rel.endswith((".bak", ".dump"))),
              f"Sensitive or backup-like file would be published: {rel}")
        if p.suffix.lower() not in {".cs", ".md", ".json", ".yaml", ".yml", ".props", ".env", ".example", ".ps1", ".sh", ".py", ".bru", ".txt"} and p.name != ".env.example":
            continue
        try:
            content = p.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        # Check plausible live secret lengths; known synthetic '-dev-only' passwords are documented.
        for pattern in (r"sk-or-v1-[A-Za-z0-9_\-]{30,}", r"sk-proj-[A-Za-z0-9_\-]{30,}",
                        r"ghp_[A-Za-z0-9]{36}", r"github_pat_[A-Za-z0-9_]{60,}"):
            check(re.search(pattern, content) is None, f"Possible real secret in {rel}; review before publishing")


def snapshots():
    base = ROOT / "src/VirtualBank.Infrastructure/Persistence/EntityFramework/Migrations"
    for kind, expected_entities in (("Customer", 6), ("Credit", 3), ("Documents", 4)):
        path = base / f"{kind}DbContextModelSnapshot.cs"
        text = path.read_text(encoding="utf-8")
        method = text.split("protected override void BuildModel", 1)[-1]
        check("ConfigureModel(" not in method,
              f"{path.name} delegates to live model; historical snapshot must be frozen")
        entity_count = len(re.findall(r"\b(?:mb|modelBuilder)\.Entity(?:<[^>]+>)?\s*\(", method))
        check(entity_count >= expected_entities,
              f"{path.name} should include complete mapped entities")
    for path in base.glob("Initial*Schema.cs"):
        check("migrationBuilder.CreateTable(" in path.read_text(encoding="utf-8"),
              f"No Code First schema creation in {path.name}")


def fixtures():
    fixture = json.loads((ROOT / "infra/seed/fixtures.json").read_text(encoding="utf-8"))
    realm = json.loads((ROOT / "infra/keycloak/realm-virtual-bank.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "sample-docs/manifest.json").read_text(encoding="utf-8"))
    ids = [r["id"] for r in fixture["customers"]]
    check(len(ids) == len(set(ids)) and len(ids) >= 10, "Duplicate or too few synthetic customers")
    accounts = fixture["accounts"]
    check(len(accounts) >= 25, "Expected a meaningful multicurrency account fixture")
    check({"EUR", "USD", "TRY"}.issubset({a["currency"] for a in accounts}),
          "EUR/USD/TRY test accounts required")
    for account in accounts:
        check(account["customerId"] in ids, f"Orphan account: {account['id']}")
    users = {u["username"] for u in realm["users"]}
    for user in fixture["customers"]:
        if "keycloakUsername" in user:
            check(user["keycloakUsername"] in users, "Customer identity missing: " + user["keycloakUsername"])
    check(len(manifest) >= 30, "Expect 30 meaningful document fixtures (15 original + 15 hard negatives)")
    check(len({d["id"] for d in manifest}) == len(manifest), "Duplicate document identifiers")
    for doc_id in ("DOC-116", "DOC-117", "DOC-122", "DOC-123", "DOC-126", "DOC-130"):
        check(doc_id in {d["id"] for d in manifest}, "Missing versioned or restricted hard-negative document: " + doc_id)
    exts = {"pdf": 0, "docx": 0, "md": 0, "txt": 0, "json": 0}
    for doc in manifest:
        source = ROOT / "sample-docs" / doc["path"]
        check(source.is_file(), f"Missing document source: {doc['path']}")
        ext = source.suffix.lstrip(".")
        if ext in exts:
            exts[ext] += 1
        check(doc["acl"], f"Missing ACL: {doc['id']}")
        check(doc["language"] in {"tr", "en", "de"}, f"Unsupported document language: {doc['id']}")
    for ext, count in exts.items():
        check(count >= 3, f"Expected >=3 .{ext} docs, got {count}")
    public = [d for d in manifest if d["classification"] == 0]
    check(len(public) >= 10, "Public documents must be sufficient for a bank-customer RAG demo")
    catalog = json.loads((ROOT / "policies/permissions.json").read_text(encoding="utf-8"))
    check("document.read" in catalog["roles"].get("bank-customer", []), "bank-customer public document capability missing")
    realm = json.loads((ROOT / "infra/keycloak/realm-virtual-bank.json").read_text(encoding="utf-8"))
    for user in realm["users"]:
        if "bank-customer" in user.get("realmRoles", []):
            check(user.get("attributes", {}).get("clearance") == ["public"],
                  "Bank customer must have public clearance: " + user["username"])



def security_regressions():
    """Static invariant checks only. Live JWT and SQL/RLS tests remain mandatory."""
    realm = json.loads((ROOT / "infra/keycloak/realm-virtual-bank.json").read_text(encoding="utf-8"))
    ingestion = next((c for c in realm["clients"] if c.get("clientId") == "svc-ingestion"), None)
    check(ingestion is not None, "Missing svc-ingestion client")
    if ingestion is not None:
        audience = any(
            mapper.get("protocolMapper") == "oidc-audience-mapper"
            and mapper.get("config", {}).get("included.client.audience") == "virtual-bank-api"
            for mapper in ingestion.get("protocolMappers", [])
        )
        check(audience, "svc-ingestion token must include virtual-bank-api audience")
    hr = next((u for u in realm["users"] if u.get("username") == "sophie.hr"), None)
    check(hr is not None and hr.get("attributes", {}).get("clearance") == ["strictly_confidential"],
          "HR persona cannot read strictly confidential HR documents")
    security_policy = ROOT / "src/VirtualBank.Application/Documents/Authorization/DocumentPolicies.cs"
    secure_search = ROOT / "src/VirtualBank.Application/Documents/SecureDocumentSearch.cs"
    check(security_policy.exists() and "class DocumentReadPolicy" in security_policy.read_text(encoding="utf-8"),
          "Application-level DocumentReadPolicy missing")
    if secure_search.exists():
        source = secure_search.read_text(encoding="utf-8")
        check("authorization.AuthorizeAsync" in source and "RecordPolicyRlsMismatch" in source,
              "Secure retrieval must re-authorize every RLS-returned document")
    offline = ROOT / "src/VirtualBank.Infrastructure/Assistant/Offline/OfflineAssistantRuntime.cs"
    check(offline.exists() and "async Task<string> CompleteAsync" in offline.read_text(encoding="utf-8"),
          "Offline runtime source missing")
    endpoints = ROOT / "src/VirtualBank.Presentation/Documents/DocumentEndpoints.cs"
    check(endpoints.exists() and "using Microsoft.AspNetCore.Builder;" in endpoints.read_text(encoding="utf-8"),
          "ASP.NET Core endpoint builder import missing")
    windows = ROOT / "scripts/windows"
    check(all((windows / name).is_file() for name in (
        "start-local.ps1", "INDEX_DOCUMENTS_WINDOWS.ps1", "RESET_TO_EFCORE_WINDOWS.ps1",
        "UPGRADE_EXISTING_WINDOWS.ps1", "VERIFY_ON_WINDOWS.ps1", "VERIFY_PUBLICATION_WINDOWS.ps1")),
        "Windows scripts must live under scripts/windows")
    check(not list(ROOT.glob("*.ps1")), "Root PowerShell scripts should be relocated to scripts/windows")


def documentation():
    """Diagrams used by the docs exist, and every relative link/image in README.md and docs/*.md resolves."""
    for name in ("architecture-overview", "security-rls", "rag-pipeline", "efcore-migrations"):
        check((ROOT / "docs" / "assets" / f"{name}.svg").is_file(), f"Missing diagram: docs/assets/{name}.svg")
    link = re.compile(r"\]\(((?!https?:|mailto:|#)[^)\s]+)\)")
    for md in [ROOT / "README.md", *sorted((ROOT / "docs").rglob("*.md"))]:
        for target in link.findall(md.read_text(encoding="utf-8")):
            path = (md.parent / target.split("#", 1)[0]).resolve()
            check(path.exists(), f"Broken link in {md.relative_to(ROOT)}: {target}")


if __name__ == "__main__":
    hygiene()
    snapshots()
    security_regressions()
    fixtures()
    documentation()
    if ERRORS:
        for e in ERRORS:
            print("FAIL", e, file=sys.stderr)
        sys.exit(1)
    print("PASS publication preflight: static security invariants, snapshots, hygiene, fixtures, documentation links.")
