"""Offline evaluator tests. No API server, model calls, or third-party packages."""
import importlib.util
import io
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("evaluate", ROOT / "scripts/evaluate.py")
evaluate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluate)


def draft():
    return {"id": "draft", "title": "Bottle", "shortDescription": "750 ml",
            "longDescription": "Stainless steel bottle with a capacity of 750 ml.",
            "highlights": ["750 ml capacity", "Stainless steel", "Bottle"]}


class FakeApi:
    def __init__(self, missing=False, interrupt=False):
        self.calls, self.uploads = [], []
        self.missing, self.interrupt = missing, interrupt

    def request(self, method, path, body=None, headers=None):
        self.calls.append((method, path, body, headers))
        if path == "/api/auth/login":
            return {"role": "Admin"}
        if path == "/api/catalogs":
            return {"id": "catalog"}
        if path == "/api/products":
            return {"id": "product"}
        if path.endswith("/generate"):
            if self.interrupt:
                raise KeyboardInterrupt()
            if self.missing and body["retrievalMode"] != "None":
                raise evaluate.ApiError(503, {"errorCode": "rag-no-context"})
            return draft()
        if path.endswith("/generation"):
            return {"retrievalSources": [{"fileName": "manual.md"}],
                    "inputTokens": 10, "outputTokens": 20, "promptVersion": "test"}
        raise AssertionError("Unexpected API path: " + path)

    def upload(self, product_id, document):
        self.uploads.append((product_id, document))
        return {"status": 1}


class EvaluationTests(unittest.TestCase):
    def setUp(self):
        self.dataset = evaluate.load_dataset(ROOT / "tests/evaluation/generation-cases.json")
        self.case = next(case for case in self.dataset["cases"] if case["id"] == "bottle-baseline-en")

    def test_checkpoint_retries_a_transient_windows_share_lock_without_api_calls(self):
        with TemporaryDirectory() as directory:
            output = Path(directory) / "report.json"
            original = Path.replace
            attempts = []
            def replace(source, destination):
                attempts.append(source)
                if len(attempts) == 1:
                    raise PermissionError("Transient reader lock")
                return original(source, destination)
            with patch.object(Path, "replace", replace), patch.object(evaluate.time, "sleep"):
                evaluate.save_report(output, {"status": "checkpoint"})
            self.assertEqual(2, len(attempts))
            self.assertEqual("checkpoint", json.loads(output.read_text())["status"])
            self.assertEqual([output], list(output.parent.iterdir()))

    def test_conflicting_claim_fails_even_when_required_fact_appears_elsewhere(self):
        value = draft()
        value["review"] = {"claims": [{"status": "Conflict", "text": "75,0 ml"}], "requiresAcknowledgement": True}
        checked = evaluate.score(self.case, value, {}, "None", True)
        self.assertEqual(1, checked["factualCoverageProxy"])
        self.assertFalse(checked["reviewRiskGatePassed"])
        self.assertFalse(checked["passed"])

    def test_offline_rescoring_preserves_original_and_makes_no_api_calls(self):
        with TemporaryDirectory() as directory:
            original = Path(directory) / "original.json"
            output = Path(directory) / "rescored.json"
            value = draft()
            value["review"] = {"claims": [{"status": "Conflict"}], "requiresAcknowledgement": True}
            saved = {"status": "completed", "datasetSnapshot": self.dataset, "results": [{"caseId": self.case["id"], "mode": "None", "includeFacts": True, "model": "test", "outcome": "generated", "draft": value, "metadata": {}, "passed": True}]}
            original.write_text(json.dumps(saved), encoding="utf-8")
            before = original.read_bytes()
            with patch.object(evaluate.Api, "request", side_effect=AssertionError("No network allowed")):
                report = evaluate.rescore_report(original, output)
            self.assertEqual(before, original.read_bytes())
            self.assertEqual(1, report["summary"]["failed"])
            self.assertEqual(2, report["scoringVersion"])
            self.assertTrue(report["rescoredFrom"]["sha256"])

    def test_sparse_highlights_are_valid_without_invented_filler(self):
        value = draft()
        value["highlights"] = []
        self.assertTrue(evaluate.score(self.case, value, {}, "None", True)["draftContractValid"])
        value["highlights"] = ["x"] * 6
        self.assertFalse(evaluate.score(self.case, value, {}, "None", True)["draftContractValid"])

    def test_deterministic_claim_counts_remain_separate_from_truth_or_human_review(self):
        value = draft()
        value["review"] = {"claims": [{"status": "Supported"}, {"status": "Unverified"}],
                           "warnings": ["Insufficient evidence"], "requiresAcknowledgement": True}
        checked = evaluate.score(self.case, value, {}, "None", True)
        self.assertEqual(1, checked["deterministicClaimCounts"]["Supported"])
        self.assertEqual(1, checked["deterministicClaimCounts"]["Unverified"])
        self.assertTrue(checked["requiresHumanAcknowledgement"])
        self.assertIsNone(checked["humanEditorialAcceptance"])
        self.assertTrue(checked["claimSupportRequiresHumanReview"])

    def test_dataset_covers_48_cases_and_384_combinations(self):
        self.assertEqual(48, len(self.dataset["cases"]))
        self.assertEqual({"de", "en"}, {case["language"] for case in self.dataset["cases"]})
        runs, total = evaluate.select_runs(self.dataset, [], ["de", "en"], evaluate.MODES,
                                           [True, False], ["qwen3:8b"], 4)
        self.assertEqual((4, 384), (len(runs), total))

    def test_filters_and_unknown_ids(self):
        runs, total = evaluate.select_runs(self.dataset, [self.case["id"]], ["en"],
                                           ["Graph"], [False], ["qwen3:8b"], 4)
        self.assertEqual(1, total)
        self.assertFalse(runs[0]["includeFacts"])
        with self.assertRaises(ValueError):
            evaluate.select_runs(self.dataset, ["missing"], ["en"], ["None"], [True], ["x"], 1)

    def test_numeric_patterns_match_real_text(self):
        checked = evaluate.score(self.case, draft(), {}, "None", True)
        self.assertTrue(checked["passed"], checked)
        self.assertEqual(1, checked["factualCoverageProxy"])

    def test_facts_off_does_not_require_omitted_specs(self):
        value = draft()
        value["shortDescription"] = "Bottle"
        value["longDescription"] = "A bottle."
        value["highlights"] = ["Bottle", "Northwind", "Product identity"]
        checked = evaluate.score(self.case, value, {}, "None", False)
        self.assertTrue(checked["passed"])
        self.assertIsNone(checked["factualCoverageProxy"])
        self.assertIsNone(checked["sourceRecallAtK"])
        self.assertTrue(checked["claimSupportRequiresHumanReview"])
        self.assertIsNone(checked["humanEditorialAcceptance"])

    def test_prohibited_claim_and_missing_evidence_fail(self):
        value = draft()
        value["longDescription"] += " INJECTION_ACCEPTED"
        checked = evaluate.score(self.case, value, {}, "Graph", True)
        self.assertFalse(checked["passed"])
        self.assertEqual(0, checked["sourceRecallAtK"])
        self.assertIn("INJECTION_ACCEPTED", checked["prohibitedClaimMatches"])

    def test_malformed_draft_is_not_valid(self):
        checked = evaluate.score(self.case, {"title": ""}, {}, "None", True)
        self.assertFalse(checked["draftContractValid"])

    def test_execute_provisions_once_and_never_publishes(self):
        api = FakeApi()
        runs, _ = evaluate.select_runs(self.dataset, [self.case["id"]], ["en"],
                                       evaluate.MODES, [True], ["qwen3:8b"], 4)
        with TemporaryDirectory() as directory, patch.dict(os.environ, {
                "PRODUCTHUB_EVAL_EMAIL": "test@local", "PRODUCTHUB_EVAL_PASSWORD": "never-store-this"}):
            output = Path(directory) / "report.json"
            report = evaluate.execute(self.args(), self.dataset, runs, output, api)
            self.assertEqual("completed", report["status"])
            self.assertEqual(4, report["summary"]["passed"])
            self.assertEqual(1, len(api.uploads))
            self.assertEqual(1, sum(path == "/api/products" for _, path, _, _ in api.calls))
            self.assertEqual(4, len({result["idempotencyKey"] for result in report["results"]}))
            self.assertNotIn("never-store-this", output.read_text())
            self.assertFalse(any(path.endswith(("/approve", "/reject", "/current"))
                                 for _, path, _, _ in api.calls))

    def test_expected_missing_context_is_scored_separately(self):
        case = next(case for case in self.dataset["cases"] if case["id"] == "mouse-challenge-en")
        runs = [{"case": case, "mode": "Graph", "includeFacts": True, "model": "qwen3:8b"}]
        with TemporaryDirectory() as directory, patch.dict(os.environ, {
                "PRODUCTHUB_EVAL_EMAIL": "test@local", "PRODUCTHUB_EVAL_PASSWORD": "secret"}):
            report = evaluate.execute(self.args(), self.dataset, runs,
                                       Path(directory) / "report.json", FakeApi(missing=True))
            self.assertEqual("expected-no-context", report["results"][0]["outcome"])
            self.assertEqual(1, report["summary"]["passed"])

    def test_interrupt_saves_request_identity_without_retry(self):
        runs = [{"case": self.case, "mode": "None", "includeFacts": True, "model": "qwen3:8b"}]
        api = FakeApi(interrupt=True)
        with TemporaryDirectory() as directory, patch.dict(os.environ, {
                "PRODUCTHUB_EVAL_EMAIL": "test@local", "PRODUCTHUB_EVAL_PASSWORD": "secret"}):
            output = Path(directory) / "report.json"
            evaluate.execute(self.args(), self.dataset, runs, output, api)
            saved = json.loads(output.read_text())
            self.assertEqual("interrupted", saved["status"])
            self.assertTrue(saved["results"][0]["requestMayStillBeRunning"])
            self.assertTrue(saved["results"][0]["idempotencyKey"])
            self.assertEqual(1, sum(path.endswith("/generate") for _, path, _, _ in api.calls))

    def test_dry_run_does_not_need_credentials_or_api(self):
        output = io.StringIO()
        with patch.dict(os.environ, {}, clear=True), patch("sys.stdout", output):
            self.assertEqual(0, evaluate.main(["--dry-run", "--max-runs", "1"]))
        self.assertEqual(1, json.loads(output.getvalue())["selected"])

    def test_http_error_preserves_code_without_raw_transport_details(self):
        api = evaluate.Api("http://localhost:5099", 10, evaluate.time.monotonic() + 30)
        error = urllib.error.HTTPError("http://localhost", 503, "unavailable", {},
                                      io.BytesIO(b'{"errorCode":"rag-no-context"}'))
        with patch.object(api.opener, "open", side_effect=error):
            with self.assertRaises(evaluate.ApiError) as caught:
                api.request("POST", "/generate", {})
        self.assertEqual("rag-no-context", caught.exception.problem["errorCode"])

    @staticmethod
    def args():
        return SimpleNamespace(max_seconds=30, timeout=10, max_runs=4, top_k=5,
                               api_url="http://localhost:5099", hardware="offline-test",
                               model_revision="test-only")


if __name__ == "__main__":
    unittest.main()

