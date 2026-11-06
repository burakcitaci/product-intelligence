"""Bounded live contract checks using disposable fixtures in a chosen local API."""
from __future__ import annotations
import argparse
import json
import os
import time
import uuid
from pathlib import Path
from evaluate import Api, ApiError, save_report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default="http://127.0.0.1:5199")
    parser.add_argument("--output", type=Path, default=Path(".artifacts/evaluation/api-acceptance.json"))
    parser.add_argument("--max-seconds", type=int, default=240)
    args = parser.parse_args()
    if not 30 <= args.max_seconds <= 600: parser.error("max-seconds must be 30–600")
    api = Api(args.api_url, 140, time.monotonic() + args.max_seconds)
    report = {"version": 1, "status": "running", "apiUrl": args.api_url, "checks": [], "fixturePrefix": "ACCEPT-" + uuid.uuid4().hex[:8]}
    def check(condition, name):
        report["checks"].append({"name": name, "passed": bool(condition)})
        save_report(args.output, report)
        if not condition: raise AssertionError(name)
    def expect_status(status, method, route, body=None):
        try: api.request(method, route, body)
        except ApiError as error: return error.status == status
        return False
    def wait_job(job_id):
        while time.monotonic() < api.deadline:
            job = api.request("GET", "/api/generation-jobs/" + job_id)
            if job["status"] not in ("Queued", "Running"): return job
            time.sleep(.5)
        raise TimeoutError("Job deadline")
    try:
        api.request("POST", "/api/auth/login", {"email": os.environ["PRODUCTHUB_EVAL_EMAIL"], "password": os.environ["PRODUCTHUB_EVAL_PASSWORD"], "rememberMe": False})
        catalog = api.request("POST", "/api/catalogs", {"name": report["fixturePrefix"], "description": "Disposable acceptance fixtures"})
        product = api.request("POST", "/api/products", {"sku": report["fixturePrefix"], "name": "Acceptance thermostat", "brand": "Test brand", "catalogId": catalog["id"], "attributes": [{"name": "Capacity", "value": "750", "unit": "ml"}]})
        pid = product["id"]
        report.update(catalogId=catalog["id"], productId=pid)
        old = api.upload(pid, {"fileName": "old.md", "text": "Acceptance thermostat reference. Cleaning requires a soft dry cloth."})
        new = api.upload(pid, {"fileName": "new.md", "text": "Acceptance thermostat reference. Maintenance requires disconnecting the mains."})
        def search(mode): return api.request("POST", "/api/knowledge/search", {"query": "Acceptance thermostat", "productId": pid, "limit": 5, "mode": mode})
        before = search("Graph")
        repeated = search("Graph")
        check(before and all(r["retrievalVersion"].startswith("graph-v2") for r in before), "Graph API uses requested mode")
        check(all(r["graphCacheHit"] for r in repeated), "Identical fresh corpus reuses graph cache")
        revised = api.request("POST", f"/api/products/{pid}/documents/{old['id']}/supersede", {"replacementDocumentId": new["id"]})
        check(revised["supersededByDocumentId"] == new["id"] and revised["extractionVersion"] != "legacy", "Document supersession and extraction version persist")
        for mode in ("Naive", "Advanced", "Graph"):
            results = search(mode)
            check(results and all(r["documentId"] != old["id"] for r in results), mode + " excludes superseded vector evidence")
            if mode == "Graph": check(results[0]["graphRevision"] != before[0]["graphRevision"], "Supersession invalidates graph revision")
        old_snapshot = api.request("GET", "/api/documents/" + old["id"])
        check(old_snapshot["chunkCount"] > 0, "Superseded evidence remains available for audit")
        check(expect_status(409, "POST", f"/api/products/{pid}/documents/{new['id']}/supersede", {"replacementDocumentId": old["id"]}), "Supersession cycles are rejected")
        job_route = f"/api/products/{pid}/generation-jobs"
        command = {"language": "en", "retrievalMode": "None", "includeFacts": False}
        key = "cancel-" + uuid.uuid4().hex
        queued = api.request("POST", job_route, command, {"Idempotency-Key": key})
        replay = api.request("POST", job_route, command, {"Idempotency-Key": key})
        check(queued["id"] == replay["id"] and replay.get("idempotencyKey") == key, "Durable enqueue key replays one operation")
        api.request("POST", "/api/generation-jobs/" + queued["id"] + "/cancel")
        cancelled = wait_job(queued["id"])
        check(cancelled["status"] == "Cancelled", "Queued or running cancellation persists")
        ready = wait_job(api.request("POST", job_route, command, {"Idempotency-Key": "ready-" + uuid.uuid4().hex})["id"])
        check(ready["status"] == "Succeeded" and ready["draftId"], "Worker creates a durable ready draft")
        did = ready["draftId"]
        draft = api.request("GET", "/api/drafts/" + did)
        check(not draft["review"]["context"]["includeFacts"] and not draft["review"]["context"]["attributes"] and draft["review"]["requiresAcknowledgement"], "Facts-off generation records sparse-evidence review warnings")
        product = api.request("PUT", "/api/products/" + pid, {**product, "name": "Changed thermostat", "attributes": [{"name": "Capacity", "value": "900", "unit": "ml"}]})
        edited = api.request("PUT", "/api/drafts/" + did, {"title": "Acceptance thermostat", "shortDescription": draft["shortDescription"], "longDescription": draft["longDescription"], "highlights": draft["highlights"]})
        check(edited["review"]["context"]["name"] == "Acceptance thermostat" and any(c["field"] == "title" and c["status"] == "Supported" for c in edited["review"]["claims"]), "Editing preserves original evidence after product changes")
        check(expect_status(409, "POST", "/api/drafts/" + did + "/approve"), "Unacknowledged review warnings prevent approval")
        approved = api.request("POST", "/api/drafts/" + did + "/approve", {"acknowledgeWarnings": True})
        check(approved["review"]["warningsAcknowledged"] and approved["review"]["acknowledgedBy"] and approved["isCurrent"], "Explicit authenticated review acknowledgement persists")
        queue = api.request("GET", f"/api/catalogs/{catalog['id']}/review-queue?language=en&filter=stale")
        check(queue["counts"]["stale"] == 1 and queue["totalCount"] == 1 and queue["items"][0]["product"]["id"] == pid, "Review queue uses current server state and generation staleness")
        report["status"] = "passed"
    except Exception as error:
        report.update(status="failed", errorType=type(error).__name__)
    finally:
        save_report(args.output, report)
    print(json.dumps({"report": str(args.output), "status": report["status"], "passed": sum(c["passed"] for c in report["checks"]), "failed": sum(not c["passed"] for c in report["checks"])}))
    return 0 if report["status"] == "passed" else 1

if __name__ == "__main__": raise SystemExit(main())
