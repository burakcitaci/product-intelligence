import { test, expect } from "@playwright/test";

const catalogId = "11111111-1111-1111-1111-111111111111";
const productId = "22222222-2222-2222-2222-222222222222";
const currentId = "33333333-3333-3333-3333-333333333333";
const pendingId = "44444444-4444-4444-4444-444444444444";

async function mockWorkspace(page) {
  const product = { id: productId, sku: "AX-900", name: "Acceptance Bottle", brand: "Test brand", catalogId, attributes: [{ name: "Capacity", value: "750", unit: "ml" }], rowVersion: "AQ==", updatedAt: "2026-11-04T10:00:00Z" };
  const draft = (id, title, isCurrent, status, createdAt) => ({ id, productId, generationId: id, language: "de", title, shortDescription: title, longDescription: `${title}. Edelstahl.`, highlights: ["Edelstahl"], isCurrent, status, createdAt });
  const state = {
    product, jobs: [], approvals: [], superseded: false, failNextEnqueue: false, hideJobs: false, submissions: [],
    drafts: [draft(pendingId, "Kapazität 1000 ml", false, 0, "2026-11-04T12:00:00Z"), draft(currentId, "Kapazität 750 ml", true, 1, "2026-11-03T12:00:00Z")],
  };
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname, method = request.method();
    if (method === "GET" && (path === "/api/products" || path === `/api/products/${productId}`) && state.productGate) await state.productGate;
    const headers = { "access-control-allow-origin": "http://127.0.0.1:5197", "access-control-allow-credentials": "true", "access-control-allow-headers": "content-type,idempotency-key", "access-control-allow-methods": "GET,POST,PUT,DELETE,OPTIONS" };
    if (method === "OPTIONS") return route.fulfill({ status: 204, headers });
    let value;
    if (path === "/api/auth/me") value = { id: "admin-test", displayName: "Acceptance Admin", role: "Admin", email: "admin@example.test" };
    else if (path === "/api/catalogs") value = [{ id: catalogId, name: "Acceptance Catalog", description: "Browser acceptance fixtures", productCount: 1 }];
    else if (path.endsWith("/review-queue")) {
      const search = (url.searchParams.get("search") ?? "").toLowerCase();
      const matches = `${product.name} ${product.sku}`.toLowerCase().includes(search);
      const language = url.searchParams.get("language");
      const emptyFilter = url.searchParams.get("filter") === "missing-facts" || url.searchParams.get("filter") === "stale";
      const items = matches && !emptyFilter ? [{ product, pendingCount: language === "de" ? 1 : 0, missingLanguage: language !== "de", missingFacts: false, staleContent: false }] : [];
      value = { items, counts: { all: matches ? 1 : 0, pending: matches && language === "de" ? 1 : 0, missingLanguage: matches && language !== "de" ? 1 : 0, missingFacts: 0, stale: 0 }, page: 1, pageSize: 25, totalPages: items.length ? 1 : 0, totalCount: items.length };
    }
    else if (path === "/api/products") value = { items: [product], page: 1, pageSize: 100, totalCount: 1 };
    else if (path === `/api/products/${productId}`) value = product;
    else if (path.endsWith("/generations")) {
      const attempts = state.attempts ?? [];
      const status = url.searchParams.get("status"), language = url.searchParams.get("language");
      const items = attempts.filter(item => (!status || status === "all" || item.status.toLowerCase() === status.toLowerCase()) && (!language || language === "all" || item.language === language));
      value = { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: items.length ? 1 : 0 };
    }
    else if (path.endsWith("/generation-jobs")) {
      if (method === "POST") {
        const key = request.headers()["idempotency-key"];
        state.submissions.push(key);
        if (!state.jobs.some(job => job.idempotencyKey === key)) state.jobs.push({ id: "job-browser", productId, status: "Running", stage: "Generating", cancellationRequested: false, idempotencyKey: key });
        if (state.failNextEnqueue) { state.failNextEnqueue = false; state.hideJobs = true; return route.abort("failed"); }
        state.hideJobs = false;
      }
      value = method === "POST" ? state.jobs[0] : state.hideJobs ? [] : state.jobs;
    }
    else if (path.endsWith("/drafts")) value = state.drafts;
    else if (path.endsWith("/generation")) value = { id: "audit", model: "qwen3:8b", promptVersion: "product-description-v4", includeFacts: true, groundingMode: "AdvancedRag", retrievalMode: "Advanced", status: "Succeeded", startedAt: "2026-11-04T12:00:00Z", correlationId: "browser", retrievalSources: [], retrievalStatus: "Succeeded", retrievedChunkCount: 0, retrievedDocumentCount: 0 };
    else if (path.endsWith("/documents")) value = [{ id: "doc-old", fileName: "old.md", status: 1, createdAt: "2026-11-03T00:00:00Z", supersededByDocumentId: state.superseded ? "doc-new" : null }, { id: "doc-new", fileName: "new.md", status: 1, createdAt: "2026-11-04T00:00:00Z" }];
    else if (path.endsWith("/supersede")) { state.superseded = true; value = {}; }
    else if (path.endsWith("/approve") && method === "POST") {
      const body = request.postDataJSON(); state.approvals.push(body);
      state.drafts = state.drafts.map(d => d.id === pendingId ? { ...d, status: 1, review: d.review ? { ...d.review, warningsAcknowledged: true } : null } : d);
      value = state.drafts.find(d => d.id === pendingId);
    }
    else if (path.startsWith("/api/drafts/") && method === "PUT") {
      const id = path.split("/")[3], body = request.postDataJSON();
      state.drafts = state.drafts.map(d => d.id === id ? { ...d, ...body } : d);
      value = state.drafts.find(d => d.id === id);
    }
    else if (path.endsWith("/cancel")) { state.jobs = state.jobs.map(j => ({ ...j, status: "Cancelled", stage: "Cancelled" })); value = state.jobs[0]; }
    else return route.fulfill({ status: 404, json: { detail: `Unmocked API route ${method} ${path}` }, headers });
    return route.fulfill({ status: method === "POST" && path.endsWith("/generation-jobs") ? 202 : 200, json: value, headers });
  });
  return state;
}
async function openProduct(page) {
  await page.goto(`/?catalog=${catalogId}&product=${productId}&language=de`);
  await expect(page.getByRole("heading", { name: "Acceptance Bottle", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Kapazität 1000 ml", exact: true })).toBeVisible();
}

test("saved version comparison exposes numeric edits and keyboard controls", async ({ page }) => {
  await mockWorkspace(page); await openProduct(page);
  const summary = page.locator("summary").filter({ hasText: "Compare saved versions" });
  await summary.focus(); await page.keyboard.press("Enter"); await page.keyboard.press("Tab");
  await expect(page.getByLabel("Compare against")).toBeFocused();
  await expect(page.locator("del").filter({ hasText: "750" }).first()).toBeVisible();
  await expect(page.locator("ins").filter({ hasText: "1000" }).first()).toBeVisible();
  await expect(page.getByText(/Selected DE version:/)).toContainText("qwen3:8b");
});

test("dirty edits survive a declined navigation and save explicitly", async ({ page }) => {
  await mockWorkspace(page); await openProduct(page);
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByLabel("Title", { exact: true }).fill("Unpublished edit");
  await expect(page.getByText(/Unsaved changes ·/)).toBeVisible();
  await page.getByRole("button", { name: /^English/ }).click();
  const discard = page.getByRole("alertdialog", { name: "Discard unsaved changes?" });
  await expect(discard.getByRole("button", { name: "Keep editing", exact: true })).toBeFocused();
  await discard.getByRole("button", { name: "Keep editing", exact: true }).click();
  await expect(page.getByLabel("Title", { exact: true })).toHaveValue("Unpublished edit");
  await page.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Unpublished edit", exact: true })).toBeVisible();
});

test("queue filter and review language survive product navigation", async ({ page }) => {
  await mockWorkspace(page);
  await page.goto(`/?catalog=${catalogId}`);
  await page.getByRole("button", { name: /^Pending review/ }).click();
  await page.getByRole("button", { name: /Acceptance Bottle/ }).click();
  await expect(page.getByRole("heading", { name: "Acceptance Bottle", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Acceptance Catalog", exact: true }).click();
  await expect(page.getByRole("button", { name: /^Pending review/ })).toHaveAttribute("aria-pressed", "true");
});

test("generation reconnects after reload and preserves published content", async ({ page }) => {
  const state = await mockWorkspace(page); await openProduct(page);
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  await page.getByRole("button", { name: "Generate draft", exact: true }).click();
  await expect(page.getByRole("button", { name: "Cancel generation", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Kapazität 1000 ml", exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("heading", { name: "Acceptance Bottle", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Cancel generation", exact: true })).toBeVisible();
  state.jobs[0] = { ...state.jobs[0], status: "Succeeded", stage: "Ready", draftId: pendingId };
  await expect(page.getByRole("button", { name: "Cancel generation", exact: true })).toHaveCount(0);
  expect(state.drafts.find(d => d.id === currentId).isCurrent).toBe(true);
});

test("document revision controls remove a superseded source from active choices", async ({ page }) => {
  const state = await mockWorkspace(page); await openProduct(page);
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  await page.getByRole("combobox", { name: "Replacement for old.md", exact: true }).selectOption("doc-new");
  await page.getByRole("button", { name: "Supersede source", exact: true }).click();
  await expect(page.getByText("Superseded", { exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Replacement for old.md", exact: true })).toHaveCount(0);
  expect(state.superseded).toBe(true);
});

test("personal generation presets round trip within the browser", async ({ page }) => {
  await mockWorkspace(page); await openProduct(page);
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  await page.locator("summary").filter({ hasText: "Generation presets" }).click();
  await page.getByRole("textbox", { name: "New preset name", exact: true }).fill("Editorial review");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  // Details may remain open because the dialog preserves its tree.
  const preset = page.getByLabel("Apply a preset");
  if (!await preset.isVisible()) await page.locator("summary").filter({ hasText: "Generation presets" }).click();
  await expect(preset.locator("option", { hasText: "Editorial review" })).toHaveCount(1);
});

test("retrying a lost enqueue response reuses its persisted request identity", async ({ page }) => {
  const state = await mockWorkspace(page); await openProduct(page);
  state.failNextEnqueue = true;
  await page.getByRole("button", { name: "Generate", exact: true }).click();
  await page.getByRole("button", { name: "Generate draft", exact: true }).click();
  await expect(page.getByRole("dialog").getByText(/Could not confirm submission/)).toBeVisible();
  await page.getByRole("button", { name: "Generate draft", exact: true }).click();
  await expect(page.getByRole("button", { name: "Cancel generation", exact: true })).toBeVisible();
  expect(state.submissions).toHaveLength(2);
  expect(new Set(state.submissions).size).toBe(1);
  expect(state.jobs).toHaveLength(1);
});

test("review layout has no horizontal viewport overflow", async ({ page }) => {
  await mockWorkspace(page); await openProduct(page);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test("deep-link loading uses the shared workspace instead of legacy UI", async ({ page }) => {
  const state = await mockWorkspace(page);
  let release;
  state.productGate = new Promise(resolve => { release = resolve; });
  await page.goto(`/?catalog=${catalogId}&product=${productId}&language=de`);
  await expect(page.getByRole("status").filter({ hasText: "Loading workspace" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Product Content Operations", exact: true })).toHaveCount(0);
  release();
  await expect(page.getByRole("heading", { name: "Acceptance Bottle", exact: true })).toBeVisible();
});

test("failed attempts appear in model audit even when no draft was created", async ({ page }) => {
  const state = await mockWorkspace(page);
  state.drafts = [];
  state.jobs = [{ id: "4d45883b-job", productId, status: "Failed", stage: "Failed", errorMessage: "Ollama could not complete the generation request." }];
  state.attempts = [{ id: "4d45883b-job", jobId: "4d45883b-job", generationId: "failed-generation", draftId: null, language: "de", model: "qwen3:8b", retrievalMode: "Graph", includeFacts: false, requestedTopK: 5, status: "Failed", stage: "Failed", startedAt: "2026-11-04T12:00:00Z", completedAt: "2026-11-04T12:00:12Z", durationMs: 12000, errorCode: "generation-failed", errorMessage: "Ollama could not complete the generation request.", metadata: { provider: "Ollama", promptVersion: "product-description-v4", correlationId: "failure-trace", retrievalStatus: "Succeeded", retrievalDurationMs: 200, retrievedChunkCount: 1, retrievedDocumentCount: 1, retrievalSources: [] } }];
  await page.goto(`/?catalog=${catalogId}&product=${productId}&language=de`);
  await expect(page.getByRole("heading", { name: "Acceptance Bottle", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "View model audit", exact: true }).click();
  const audit = page.getByRole("region", { name: "Generation attempt audit", exact: true });
  await expect(audit.getByText("4d45883b", { exact: true })).toBeVisible();
  await expect(audit.getByText("Ollama could not complete the generation request.", { exact: true })).toBeVisible();
  await expect(audit.getByText("No draft", { exact: true })).toBeVisible();
  await expect(audit.getByText("Graph", { exact: true })).toBeVisible();
  await audit.getByLabel("Audit status", { exact: true }).selectOption("failed");
  await expect(audit.getByText("4d45883b", { exact: true })).toBeVisible();
  await audit.locator("summary").filter({ hasText: "Attempt details" }).click();
  await expect(audit.getByText("failure-trace", { exact: true })).toBeVisible();
  await expect(audit.getByText("product-description-v4", { exact: true })).toBeVisible();
});

test("flagged approval uses a shadcn alert dialog and acknowledges only on explicit approval", async ({ page }) => {
  const state = await mockWorkspace(page);
  state.drafts[0].review = { version: "claim-review-v2", requiresAcknowledgement: true, claims: [{ id: "quantity-check", field: "title", text: "Kapazität 1000 ml", status: "Conflict", reason: "Capacity differs from the supplied fact.", evidence: [] }] };
  let nativeDialogs = 0;
  page.on("dialog", async dialog => { nativeDialogs++; await dialog.dismiss(); });
  await openProduct(page);
  const approve = page.getByRole("button", { name: "Approve", exact: true });
  await approve.click();
  const confirmation = page.getByRole("alertdialog", { name: "Approve flagged draft?" });
  await expect(confirmation).toBeVisible();
  await expect(confirmation.getByRole("button", { name: "Keep reviewing", exact: true })).toBeFocused();
  await confirmation.getByRole("button", { name: "Keep reviewing", exact: true }).click();
  await expect(confirmation).toBeHidden();
  await expect(approve).toBeFocused();
  expect(state.approvals).toHaveLength(0);
  await approve.click();
  await confirmation.getByRole("button", { name: "Acknowledge & approve", exact: true }).click();
  await expect.poll(() => state.approvals.length).toBe(1);
  expect(state.approvals[0].acknowledgeWarnings).toBe(true);
  expect(nativeDialogs).toBe(0);
});
