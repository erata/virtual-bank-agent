#!/usr/bin/env python3
"""Live TR/DE/EN secure retrieval evaluation (stdlib only).

Prereqs: fresh migrated DB, successful document ingestion, real embedding service, live API/Keycloak.
Uses DEVELOPMENT-ONLY direct-grant client; not suitable for production authentication.
No keys/tokens are printed or stored. Exit nonzero on forbidden hits or insufficient Hit@K.

python tools/evaluate_rag.py --dry-run
python tools/evaluate_rag.py --api http://localhost:8081 --keycloak http://localhost:8080 --min-hit-rate .7
"""
import argparse
import collections
import json
import os
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
CASES_FILE = ROOT / "tests/evaluation/rag_golden.json"


def manifest_oracle():
    """Independent expected-access oracle from *seed fixtures*, not from API/SQL output.

    Only valid for a fresh demo database before runtime ACL administration changes.
    """
    docs = {d["id"]: d for d in json.loads((ROOT / "sample-docs/manifest.json").read_text(encoding="utf-8"))}
    realm = json.loads((ROOT / "infra/keycloak/realm-virtual-bank.json").read_text(encoding="utf-8"))
    users = {u["username"]: u for u in realm["users"]}
    permissions = json.loads((ROOT / "policies/permissions.json").read_text(encoding="utf-8"))["roles"]

    def can_read(persona, doc_id):
        user = users[persona]
        roles = set(user.get("realmRoles", []))
        if "document.read" not in {perm for role in roles for perm in permissions.get(role, [])}:
            return False
        doc = docs[doc_id]
        levels = {"public": 0, "internal": 1, "confidential": 2, "strictly_confidential": 3}
        attrs = user.get("attributes", {})
        clearance = levels.get(attrs.get("clearance", ["public"])[0], 0)
        if doc["classification"] > clearance:
            return False
        if doc.get("branch") and doc["branch"] != attrs.get("branch", [None])[0]:
            return False
        groups = set()
        for group in user.get("groups", []):
            parts = [p for p in group.split("/") if p]
            groups.update("/" + "/".join(parts[:i]) for i in range(1, len(parts)+1))

        def matches(entry):
            kind, val = entry["subjectKind"], entry["subjectValue"]
            return ((kind == "public" and val == "*") or
                    (kind == "user" and val == user["id"]) or
                    (kind == "role" and val in roles) or
                    (kind == "group" and val in groups))
        entries = doc["acl"]
        return not any(e["effect"] == "deny" and matches(e) for e in entries) and \
            any(e["effect"] == "allow" and matches(e) for e in entries)

    return docs, users, can_read


def load_cases():
    data = json.loads(CASES_FILE.read_text(encoding="utf-8"))
    manifest, users, may_read = manifest_oracle()
    seen = set()
    for case in data["cases"]:
        assert case["id"] not in seen, f"Duplicate case: {case['id']}"
        seen.add(case["id"])
        assert case["language"] in {"tr", "en", "de"}
        assert case["persona"] in users, case["id"]
        assert set(case["expectedAny"] + case["forbidden"]).issubset(manifest), case["id"]
        assert not set(case["expectedAny"]) & set(case["forbidden"]), case["id"]
        for doc in case["expectedAny"]:
            assert may_read(case["persona"], doc), f"Expected document not authorized: {case['id']} {doc}"
        for doc in case["forbidden"]:
            assert not may_read(case["persona"], doc), f"Forbidden document is actually authorized: {case['id']} {doc}"
        if case.get("expectedStatus") is not None:
            assert case["expectedStatus"] in (403,), case["id"]
            assert not case["expectedAny"], case["id"]
    assert {c["language"] for c in data["cases"]} == {"tr", "en", "de"}
    assert len(data["cases"]) >= 30
    return data


def http_json(url, method="GET", payload=None, headers=None):
    body = None if payload is None else urlencode(payload).encode("utf-8")
    req = Request(url, method=method, data=body, headers=headers or {})
    try:
        with urlopen(req, timeout=55) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except HTTPError as ex:
        return ex.code, {"httpError": ex.code}


def token(kc_url, persona, password):
    code, response = http_json(
        kc_url.rstrip("/") + "/realms/virtual-bank/protocol/openid-connect/token",
        method="POST",
        payload={"grant_type": "password", "client_id": "virtual-bank-cli",
                 "username": persona, "password": password},
        headers={"Content-Type": "application/x-www-form-urlencoded"})
    if code != 200 or "access_token" not in response:
        raise RuntimeError(f"Keycloak refused test persona {persona} (HTTP {code})")
    return response["access_token"]


def extract_hits(value):
    if not isinstance(value, list):
        raise ValueError(f"Expected a JSON array of authorized search hits, got {type(value).__name__}")
    return [item["documentId"] for item in value]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Only validate labels and independent fixture ACL expectations")
    parser.add_argument("--api", default="http://localhost:8081")
    parser.add_argument("--keycloak", default="http://localhost:8080")
    parser.add_argument("--min-hit-rate", type=float, default=.70, help="Minimum positive-query Hit@K")
    parser.add_argument("--min-mrr", type=float, default=0.0, help="Optional measured MRR@K quality threshold")
    args = parser.parse_args()
    data = load_cases()
    manifest, users, may_read = manifest_oracle()
    if args.dry_run:
        print(f"PASS: {len(data['cases'])} TR/DE/EN fixture cases refer to valid, ACL-consistent document IDs")
        print("NOTE: no live embeddings, ranking, leakage or authorization behavior measured in dry-run")
        return 0
    password = os.environ.get("VIRTUALBANK_DEMO_PASSWORD", "VirtualBank-Dev-2026!")
    tokens = {}
    matches = collections.Counter()
    total = collections.Counter()
    per_language_rr = collections.Counter()
    coverage_sum = collections.Counter()
    unauthorized_hits = 0
    forbidden_hits = 0
    error_count = 0
    version_good = 0
    version_total = 0
    for case in data["cases"]:
        who = case["persona"]
        if who not in tokens:
            tokens[who] = token(args.keycloak, who, password)
        url = args.api.rstrip("/") + "/api/documents/search?" + urlencode(
            {"query": case["query"], "topK": data["topK"]})
        code, payload = http_json(url, headers={"Authorization": "Bearer " + tokens[who]})
        expected_status = case.get("expectedStatus", 200)
        if code != expected_status:
            print(f"ERROR {case['id']}: HTTP {code}, expected {expected_status}")
            error_count += 1
            continue
        if code == 403:
            print(f"OK   {case['id']}: policy denied HTTP 403 as expected")
            continue
        try:
            ids = extract_hits(payload)
        except (ValueError, KeyError) as ex:
            print(f"ERROR {case['id']}: unexpected API shape ({ex})")
            error_count += 1
            continue
        unlisted = [id for id in ids if id not in manifest]
        unexpected = [id for id in ids if id in manifest and not may_read(who, id)]
        unauthorized_hits += len(unlisted) + len(unexpected)
        leaked = set(ids) & set(case["forbidden"])
        forbidden_hits += len(leaked)
        if case["expectedAny"]:
            total[case["language"]] += 1
            positions = [idx+1 for idx,id in enumerate(ids) if id in case["expectedAny"]]
            matches[case["language"]] += bool(positions)
            per_language_rr[case["language"]] += (1/min(positions) if positions else 0)
            coverage_sum[case["language"]] += len(set(ids) & set(case["expectedAny"])) / len(case["expectedAny"])
        # Temporal confusion is reported separately: first hit should ideally be the intended version.
        if case.get("targetVersion") and case["expectedAny"]:
            version_total += 1
            version_good += bool(ids and ids[0] in case["expectedAny"])
        flag = "LEAK" if leaked or unlisted or unexpected else "OK  "
        print(f"{flag} {case['id']}: {ids}; forbidden={sorted(leaked)}; unauthorized={unexpected+unlisted}")
    positives = sum(total.values())
    hit_rate = sum(matches.values()) / positives if positives else 0
    mrr = sum(per_language_rr.values()) / positives if positives else 0
    coverage = sum(coverage_sum.values()) / positives if positives else 0
    for language in sorted(total):
        print(f"Hit@{data['topK']} ({language}): {matches[language]}/{total[language]}, "
              f"MRR@{data['topK']}={per_language_rr[language]/total[language]:.3f}, "
              f"known-ID coverage={coverage_sum[language]/total[language]:.3f}")
    print(f"Overall Hit@{data['topK']}: {hit_rate:.1%} | MRR@{data['topK']}: {mrr:.3f} | "
          f"known-ID coverage@{data['topK']}: {coverage:.3f}")
    print(f"Temporal Top-1: {version_good}/{version_total}; "
          f"unauthorized/unlisted hits: {unauthorized_hits} | forbidden hits: {forbidden_hits} | errors: {error_count}")
    if forbidden_hits or unauthorized_hits or error_count or hit_rate < args.min_hit_rate or mrr < args.min_mrr:
        print("FAIL live RAG evaluation — investigate ACL, ranking, indexing and model choice", file=sys.stderr)
        return 1
    print("PASS measured retrieval gate against the freshly seeded fixture ACL; not proof of zero LLM hallucination")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (URLError, RuntimeError, AssertionError) as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        sys.exit(1)
