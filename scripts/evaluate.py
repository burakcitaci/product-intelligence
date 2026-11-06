"""Bounded local Product Hub evaluation. Standard library only; never publishes drafts."""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import json
import os
import platform
import re
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MODES = ("None", "Naive", "Advanced", "Graph")


class ApiError(Exception):
    def __init__(self, status, problem):
        super().__init__(f"HTTP {status}")
        self.status, self.problem = status, problem


class Api:
    def __init__(self, base_url, timeout, deadline):
        self.base_url = base_url.rstrip("/")
        self.timeout, self.deadline = timeout, deadline
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, method, path, body=None, headers=None, raw=None):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("Evaluation time budget reached")
        payload = raw if raw is not None else (
            json.dumps(body).encode("utf-8") if body is not None else None)
        request_headers = {"Content-Type": "application/json", **(headers or {})}
        request = urllib.request.Request(self.base_url + path, data=payload,
                                        headers=request_headers, method=method)
        try:
            with self.opener.open(request, timeout=min(self.timeout, remaining)) as response:
                content = response.read()
                return json.loads(content) if content else None
        except urllib.error.HTTPError as error:
            try:
                problem = json.loads(error.read())
            except (ValueError, UnicodeError):
                problem = {}
            error.close()
            raise ApiError(error.code, problem) from None

    def upload(self, product_id, document):
        boundary = "eval-" + uuid.uuid4().hex
        name = document["fileName"]
        if any(character in name for character in ('"', "\r", "\n")):
            raise ValueError("Invalid fixture filename")
        payload = (
            f'--{boundary}\r\nContent-Disposition: form-data; name="productId"\r\n\r\n'
            f'{product_id}\r\n--{boundary}\r\nContent-Disposition: form-data; '
            f'name="file"; filename="{name}"\r\nContent-Type: text/plain; charset=utf-8'
            f"\r\n\r\n{document['text']}\r\n--{boundary}--\r\n"
        ).encode("utf-8")
        result = self.request("POST", "/api/documents", raw=payload,
                              headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
        if result.get("status") not in (1, "Completed"):
            raise ValueError("Fixture document did not finish indexing")
        return result


def load_dataset(path):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    if data.get("version") != 1 or not data.get("cases") or not data.get("fixtures"):
        raise ValueError("Expected a version 1 dataset with fixtures and cases")
    ids = [case["id"] for case in data["cases"]]
    if len(set(ids)) != len(ids):
        raise ValueError("Case IDs must be unique")
    for case in data["cases"]:
        if case["fixture"] not in data["fixtures"] or case["language"] not in ("de", "en"):
            raise ValueError(f"Invalid fixture or language for {case['id']}")
        for group in ("requiredWithFacts", "forbidden"):
            for pattern in case.get(group, []):
                re.compile(pattern, re.IGNORECASE)
    return data


def select_runs(dataset, case_ids, languages, modes, facts, models, maximum):
    selected = [case for case in dataset["cases"]
                if (not case_ids or case["id"] in case_ids) and case["language"] in languages]
    unknown = set(case_ids) - {case["id"] for case in dataset["cases"]}
    if unknown:
        raise ValueError("Unknown case IDs: " + ", ".join(sorted(unknown)))
    all_runs = [
        {"case": case, "mode": mode, "includeFacts": include_facts, "model": model}
        for case in selected for model in models for include_facts in facts for mode in modes
    ]
    if not all_runs:
        raise ValueError("No evaluation cases selected")
    return all_runs[:maximum], len(all_runs)


def score(case, draft, metadata, mode, include_facts):
    if not isinstance(draft, dict) or not isinstance(metadata, dict):
        raise ValueError("Expected draft and metadata objects")
    fields = (("title", 200), ("shortDescription", 500), ("longDescription", 8000))
    contract_valid = all(isinstance(draft.get(name), str) and
                         0 < len(draft[name].strip()) <= limit for name, limit in fields)
    highlights = draft.get("highlights")
    highlight_items = highlights if isinstance(highlights, list) else []
    contract_valid = contract_valid and isinstance(highlights, list) and 0 <= len(highlights) <= 5
    contract_valid = contract_valid and all(isinstance(item, str) and
                                           0 < len(item.strip()) <= 300 for item in highlight_items)
    text = "\n".join([str(draft.get(name, "")) for name, _ in fields] +
                     [str(item) for item in highlight_items])
    required = case.get("requiredWithFacts", []) if include_facts else []
    missing = [pattern for pattern in required if not re.search(pattern, text, re.IGNORECASE)]
    forbidden = [pattern for pattern in case.get("forbidden", [])
                 if re.search(pattern, text, re.IGNORECASE)]
    sources = {source["fileName"] for source in metadata.get("retrievalSources", [])}
    expected = set(case.get("expectedSources", [])) if mode != "None" else set()
    review = draft.get("review") or {}
    statuses = ("Supported", "Unsupported", "Conflict", "Advisory", "Unverified")
    claim_counts = {status: sum(claim.get("status") == status for claim in review.get("claims", []))
                    for status in statuses} if review else None
    risk_gate_passed = not claim_counts or not (claim_counts["Conflict"] or claim_counts["Unsupported"])
    return {
        "scoringVersion": 2,
        "claimReviewAvailable": bool(review),
        "reviewRiskGatePassed": risk_gate_passed,
        "deterministicClaimCounts": claim_counts,
        "evidenceWarnings": review.get("warnings") or [],
        "requiresHumanAcknowledgement": review.get("requiresAcknowledgement"),
        "draftContractValid": bool(contract_valid),
        "factualCoverageProxy": (len(required) - len(missing)) / len(required) if required else None,
        "missingRequiredPatterns": missing,
        "prohibitedClaimMatches": forbidden,
        "sourceRecallAtK": len(expected & sources) / len(expected) if expected else None,
        "retrievedSources": sorted(sources),
        "humanEditorialAcceptance": None,
        "claimSupportRequiresHumanReview": True,
        "passed": bool(contract_valid and risk_gate_passed and not missing and not forbidden and
                       (not expected or expected <= sources)),
    }


def save_report(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    try:
        for attempt in range(5):
            try:
                temporary.replace(path)
                return
            except PermissionError:
                if attempt == 4:
                    raise
                # Windows readers/scanners can briefly deny atomic replacement. Retry
                # checkpoint I/O only; never repeat the API or model request.
                time.sleep(.025 * (2 ** attempt))
    finally:
        temporary.unlink(missing_ok=True)


def summarize(results):
    finished = [result for result in results if result.get("outcome") != "running"]
    measured = [result["checks"] for result in finished if "checks" in result]
    latencies = sorted(result["elapsedMs"] for result in finished if "elapsedMs" in result)
    return {
        "completed": len(finished),
        "deterministicClaimCounts": {
            status: sum((check.get("deterministicClaimCounts") or {}).get(status, 0) for check in measured)
            for status in ("Supported", "Unsupported", "Conflict", "Advisory", "Unverified")
        },
        "inputTokens": sum(result.get("metadata", {}).get("inputTokens") or 0 for result in finished),
        "outputTokens": sum(result.get("metadata", {}).get("outputTokens") or 0 for result in finished),
        "runsWithTokenUsage": sum(result.get("metadata", {}).get("inputTokens") is not None for result in finished),
        "invalidJsonFailures": sum(result.get("errorCode") == "invalid-json" for result in finished),
        "passed": sum(result.get("passed") is True for result in finished),
        "failed": sum(result.get("passed") is False for result in finished),
        "meanSourceRecallAtK": mean([check["sourceRecallAtK"] for check in measured
                                     if check["sourceRecallAtK"] is not None]),
        "meanFactualCoverageProxy": mean([check["factualCoverageProxy"] for check in measured
                                         if check["factualCoverageProxy"] is not None]),
        "p95WallLatencyMs": latencies[max(0, (95 * len(latencies) + 99) // 100 - 1)]
        if latencies else None,
    }


def mean(values):
    return sum(values) / len(values) if values else None


def execute(args, dataset, runs, output, api=None):
    run_id = uuid.uuid4().hex[:12]
    started = time.monotonic()
    deadline = started + args.max_seconds
    api = api or Api(args.api_url, args.timeout, deadline)
    report = {
        "version": 1, "runId": run_id, "startedAt": datetime.now(timezone.utc).isoformat(),
        "status": "running", "datasetSha256": hashlib.sha256(
            json.dumps(dataset, sort_keys=True, ensure_ascii=False).encode()).hexdigest(),
        "datasetSnapshot": dataset,
        "environment": {"hardware": args.hardware or platform.platform(),
                        "modelRevision": args.model_revision,
                        "modelRevisionVerified": False},
        "limits": {"maxRuns": args.max_runs, "maxSeconds": args.max_seconds,
                   "requestTimeoutSeconds": args.timeout},
        "results": [],
    }
    provisioned = {}
    current = None
    save_report(output, report)
    try:
        email, password = os.environ.get("PRODUCTHUB_EVAL_EMAIL"), os.environ.get("PRODUCTHUB_EVAL_PASSWORD")
        if not email or not password:
            raise ValueError("Set PRODUCTHUB_EVAL_EMAIL and PRODUCTHUB_EVAL_PASSWORD")
        api.request("POST", "/api/auth/login",
                    {"email": email, "password": password, "rememberMe": False})
        catalog = api.request("POST", "/api/catalogs",
                             {"name": f"Evaluation {run_id}", "description": "Disposable evaluation fixtures; never published."})
        report["catalogId"] = catalog["id"]
        for run in runs:
            if time.monotonic() >= deadline:
                report["status"] = "time-budget-reached"
                break
            case = run["case"]
            current = {
                "caseId": case["id"], "language": case["language"], "mode": run["mode"],
                "includeFacts": run["includeFacts"], "model": run["model"],
                "idempotencyKey": f"eval-{run_id}-{len(report['results'])}",
                "outcome": "running", "passed": False,
            }
            report["results"].append(current)
            save_report(output, report)
            attempt_start = time.monotonic()
            try:
                fixture_id = case["fixture"]
                fixture = dataset["fixtures"][fixture_id]
                if fixture_id not in provisioned:
                    product = api.request("POST", "/api/products", {
                        **fixture["product"], "sku": f"EVAL-{run_id}-{fixture_id}",
                        "catalogId": catalog["id"],
                    })
                    for document in fixture.get("documents", []):
                        api.upload(product["id"], document)
                    provisioned[fixture_id] = product["id"]
                current["productId"] = provisioned[fixture_id]
                # No automatic retries: a timeout can leave the server still generating.
                current["requestMayStillBeRunning"] = True
                save_report(output, report)
                draft = api.request("POST", f"/api/products/{current['productId']}/drafts/generate", {
                    "language": case["language"], "retrievalMode": run["mode"],
                    "includeFacts": run["includeFacts"], "model": run["model"], "topK": args.top_k,
                }, {"Idempotency-Key": current["idempotencyKey"]})
                current["requestMayStillBeRunning"] = False
                current["draftId"] = draft["id"]
                metadata = api.request("GET", f"/api/drafts/{draft['id']}/generation")
                current.update(outcome="generated", draft=draft, metadata=metadata)
                current["checks"] = score(case, draft, metadata, run["mode"], run["includeFacts"])
                expected_error = run["mode"] != "None" and case.get("expectNoContext", False)
                current["passed"] = current["checks"]["passed"] and not expected_error
            except ApiError as error:
                expected = (case.get("expectNoContext", False) and run["mode"] != "None"
                            and error.status == 503 and error.problem.get("errorCode") == "rag-no-context")
                current.update(outcome="expected-no-context" if expected else "api-error",
                               httpStatus=error.status, errorCode=error.problem.get("errorCode"),
                               requestMayStillBeRunning=False, passed=expected)
            except (TimeoutError, urllib.error.URLError, OSError) as error:
                current.update(outcome="transport-error-unknown", errorType=type(error).__name__, passed=False)
            except (ValueError, KeyError, TypeError) as error:
                current.update(outcome="invalid-response-or-fixture", errorType=type(error).__name__, passed=False)
            current["elapsedMs"] = round((time.monotonic() - attempt_start) * 1000)
            report["summary"] = summarize(report["results"])
            save_report(output, report)
        if report["status"] == "running":
            report["status"] = "completed"
    except KeyboardInterrupt:
        report["status"] = "interrupted"
        if current and current["outcome"] == "running":
            current["outcome"] = "interrupted"
    except (ValueError, ApiError, OSError, urllib.error.URLError) as error:
        report["status"] = "setup-failed"
        report["errorType"] = type(error).__name__  # Never persist credentials or raw server error bodies.
    finally:
        report["summary"] = summarize(report["results"])
        groups = {}
        for result in report["results"]:
            key = f"{result['mode']}/facts={result['includeFacts']}/model={result['model']}"
            groups.setdefault(key, []).append(result)
        report["comparisons"] = {key: summarize(values) for key, values in groups.items()}
        report["elapsedMs"] = round((time.monotonic() - started) * 1000)
        save_report(output, report)
    return report


def rescore_report(path, output):
    raw = path.read_bytes()
    report = json.loads(raw.decode("utf-8-sig"))
    dataset = report["datasetSnapshot"]
    cases = {case["id"]: case for case in dataset["cases"]}
    report["rescoredFrom"] = {"path": str(path), "sha256": hashlib.sha256(raw).hexdigest()}
    report["scoringVersion"] = 2
    for result in report["results"]:
        if result.get("outcome") != "generated": continue
        case = cases[result["caseId"]]
        result["checks"] = score(case, result["draft"], result["metadata"], result["mode"], result["includeFacts"])
        result["passed"] = result["checks"]["passed"] and not (case.get("expectNoContext", False) and result["mode"] != "None")
    report["summary"] = summarize(report["results"])
    groups = {}
    for result in report["results"]:
        key = f"{result['mode']}/facts={result['includeFacts']}/model={result['model']}"
        groups.setdefault(key, []).append(result)
    report["comparisons"] = {key: summarize(values) for key, values in groups.items()}
    save_report(output, report)
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", type=Path, default=ROOT / "tests/evaluation/generation-cases.json")
    parser.add_argument("--rescore-report", type=Path, help="Rescore a saved report offline without model or API calls")
    parser.add_argument("--dry-run", action="store_true", help="List bounded runs; no network, files, or model calls")
    parser.add_argument("--cases", default="", help="Comma-separated exact case IDs")
    parser.add_argument("--languages", default="de,en")
    parser.add_argument("--modes", default=",".join(MODES))
    parser.add_argument("--facts", choices=("on", "off", "both"), default="both")
    parser.add_argument("--models", default="qwen3:8b")
    parser.add_argument("--max-runs", type=int, default=4)
    parser.add_argument("--max-seconds", type=int, default=600)
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--top-k", type=int, default=5)
    parser.add_argument("--api-url", default="http://localhost:5099")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--hardware", default="")
    parser.add_argument("--model-revision", default="unknown", help="Record the Ollama model digest/revision manually")
    args = parser.parse_args(argv)
    if args.rescore_report:
        output = args.output or args.rescore_report.with_name(args.rescore_report.stem + "-rescored.json")
        if output.resolve() == args.rescore_report.resolve(): parser.error("Rescoring output must preserve the original report")
        try: report = rescore_report(args.rescore_report, output)
        except (KeyError, ValueError, OSError) as error: parser.error(str(error))
        print(json.dumps({"report": str(output), "status": report["status"], **report["summary"]}, indent=2))
        return 0 if not report["summary"]["failed"] else 1
    if not 1 <= args.max_runs <= 512 or not 1 <= args.top_k <= 20 or not 1 <= args.timeout <= 600 or not 1 <= args.max_seconds <= 86400:
        parser.error("Limits: max-runs 1â€“512, top-k 1â€“20, timeout 1â€“600, max-seconds 1â€“86400")
    canonical = {mode.lower(): mode for mode in MODES}
    try:
        modes = list(dict.fromkeys(canonical[value.strip().lower()] for value in args.modes.split(",")))
        languages = [value.strip() for value in args.languages.split(",")]
        if any(value not in ("de", "en") for value in languages):
            raise ValueError("Languages must be de,en")
        models = list(dict.fromkeys(value.strip() for value in args.models.split(",") if value.strip()))
        if not models:
            raise ValueError("At least one model is required")
        dataset = load_dataset(args.dataset)
        runs, total = select_runs(dataset, [value.strip() for value in args.cases.split(",") if value.strip()],
                                  languages, modes, [True, False] if args.facts == "both" else [args.facts == "on"],
                                  models, args.max_runs)
    except (ValueError, KeyError, OSError) as error:
        parser.error(str(error))
    if args.dry_run:
        print(json.dumps({"selected": len(runs), "available": total, "truncated": len(runs) < total,
                          "runs": [{**run, "case": run["case"]["id"]} for run in runs]}, ensure_ascii=False, indent=2))
        return 0
    output = args.output or ROOT / ".artifacts/evaluation" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:6] + ".json")
    report = execute(args, dataset, runs, output)
    print(json.dumps({"report": str(output), "status": report["status"], **report["summary"]}, indent=2))
    return 0 if report["status"] == "completed" and report["summary"]["failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
