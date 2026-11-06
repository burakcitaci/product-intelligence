# Review workspace

## Catalog review queue

The catalog page loads an administrator-only server queue, with search, review language, filters and 25-product pagination. Counts include the complete matching catalog rather than the first product page. Filters can overlap; counts represent products, while a row's pending badge counts that product's pending drafts in the selected language.

| Filter | Meaning |
| --- | --- |
| All products | All products in this catalog matching the name, SKU or brand search |
| Pending review | At least one PendingReview draft in the selected language |
| Missing language | No approved current/published draft in the selected language; a pending draft does not count as published |
| Missing facts | The product has no structured attributes |
| Stale content | The product UpdatedAt is later than the current published draft's generation StartedAt |

Staleness is a conservative timestamp signal for product changes. It does not perform semantic comparison of individual facts or detect reference-document changes. Missing published content is reported as missing language, not stale. Rejected or non-current drafts cannot satisfy the published-language check.

Search, language, selected filter and page persist in sessionStorage per account/catalog. They survive opening a product and returning during the browser session. The API clamps pages after a queue shrinks. Opening a row carries the review language into the product page and hydrates that product even if it was outside the initial 100-product fetch. Queue preferences do not publish or mutate products. Refresh re-reads server state; reopening the catalog also refreshes it.

GET /api/catalogs/{id}/review-queue accepts language=de|en, filter=all|pending|missing-language|missing-facts|stale, search (up to 200 characters), page (positive) and pageSize (1–100, default 25). It returns items, counts, totalCount, page, pageSize and totalPages. Counts apply to the chosen search/language before the active filter. Unknown catalogs return 404; invalid query settings return 400. Existing authorization is unchanged; this queue requires Admin.

The implementation loads matching product rows and small draft/generation summaries to calculate all counts, because attributes are stored through a JSON value converter. It avoids loading full draft bodies or issuing a request per product. Memory and query work still grow with the matching catalog; very large catalogs should use an indexed server projection for these counts. No new database migration is needed.

## Saved version comparison

Open a saved draft and expand Compare saved versions. The baseline defaults to the currently published version of the same language, or another saved version. Choose another baseline using the native select. The panel identifies language, version number/ID, date, model, retrieval/facts choices, prompt version and Top-K where recorded. Missing audit data is labelled unavailable.

Titles, short/long descriptions and ordered highlights show inline additions and deletions. Numbers, whitespace and multilingual characters are preserved. Colour is accompanied by insertion/deletion markup and a text legend. The comparison is read-only, uses saved content and never approves, publishes or applies an old version. It remains separate from the evidence review. With only one version, the UI explains that a second saved version is needed.

Token LCS is bounded to 1,200 tokens per side per field; longer fields use whole-field removal/addition to avoid an unbounded allocation. Empty highlight arrays are valid.

## Unsaved changes and background work

Editing a draft shows an Unsaved changes indicator when fields differ from the saved version. Switching language/version, leaving the product, signing out or cancelling edits requests confirmation before discarding modifications. Closing/reloading the page registers the browser's standard beforeunload guard; the browser controls whether a prompt can be displayed.

Background generation retains the existing saved draft and editor. Completion refreshes saved drafts without forcing a version switch while editing. Draft fetches are fenced by request sequence, so an older response cannot overwrite a newer request. Drafts are scoped to the selected product when navigating. These guards are in-memory protection, not autosave: confirm a discard or close the page without a browser prompt and unsaved input can be lost.

## Verification

Six Node tests exercise multilingual/numeric edits, exact reconstruction of both versions, ordered highlights, empty content, bounded large-field fallback and HTML-like text. Three focused queue tests exercise overlapping counts, candidates beyond the first 100 rows, paging and shrinking queues. A SQL integration test covers catalog/language scope, pending versus published states, stale generation time, validation and missing catalogs. Linux CI runs these suites and frontend compilation.

The frontend production build and .NET solution build succeed. Eighteen desktop/mobile acceptance checks now pass against the production build using controlled API fixtures, including keyboard focus, discard confirmation and viewport overflow. The 32-test SQL integration suite also passes. The final rebuilt unit-test binary remains subject to Windows application-control restrictions. No claim of a fully verified live deployment is made.


## Source revisions and reload recovery

Product knowledge now shows revision details and explicit Supersede source controls. Only newer completed active documents from the same product are offered. Superseded sources remain visible as historical, but do not enable retrieval by themselves. The source list is a reusable component.

Catalog/product/language are retained in the URL so a refresh restores the workspace and generation progress. Pending enqueue identities survive a lost response in session storage; retrying unchanged settings reuses the key. Delayed deep-link loading uses the shared workspace shell, and the obsolete legacy root fallback has been removed.

See [acceptance results](evaluation-results-2026-11-04.md) for current verification and model quality limits.


## Model audit includes failures

The audit lists generation attempts rather than only saved drafts. Open Model audit to inspect successful, failed, cancelled, queued and running attempts across the product's languages. Choose Failed to inspect errors with their original model/RAG/facts choices and timing. Jobs and generation records belonging to one operation are combined into one row. Requests that produced no content show No draft; successful attempts retain a View draft action and source/parameter audit.

The panel is present even before the first draft exists. Recent generations offers View model audit for failures/cancellations. Status/language filters and pagination are server-backed; refresh is available for older or externally initiated attempts. Historical errors are included from existing data, with standard explanations when only a machine error code was retained. No schema migration is needed for this enhancement.


## Confirmation dialogs

Approval of flagged claims, discarding unsaved edits and removing a knowledge source now use the shared shadcn AlertDialog implementation rather than window.confirm. Dialogs provide explicit action labels, default focus on the cancellation action, keyboard focus trapping and focus restoration on cancellation. Approval sends acknowledgeWarnings=true only after Acknowledge & approve; Keep reviewing leaves the draft unchanged. The browser-controlled beforeunload prompt for closing/reloading a dirty page is retained.

Frontend production build and all 22 desktop/mobile browser checks pass, including approval cancellation, focus restoration, explicit acknowledgement and absence of a native browser confirmation during approval.
