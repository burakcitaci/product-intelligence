import { DraftComparison } from "@/components/draft-comparison";
import {
  EvidenceReview,
  type ClaimReviewReport,
} from "@/components/evidence-review";
import { Check, Pencil, RefreshCw, Sparkles, X } from "lucide-react";

import { PageLayout, SectionHeader } from "@/components/workspace";
import { PageHeader } from "@/components/page-header";
import { StatusBadge } from "@/components/status-badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";

type Attribute = { name: string; value: string; unit?: string };
type ProductView = {
  sku: string;
  name: string;
  brand: string;
  attributes: Attribute[];
  updatedAt: string;
};
type DraftView = {
  review?: ClaimReviewReport | null;
  id: string;
  language: string;
  title: string;
  shortDescription: string;
  longDescription: string;
  highlights: string[];
  status: number | string;
  isCurrent: boolean;
  createdAt: string;
};
type DraftForm = {
  title: string;
  shortDescription: string;
  longDescription: string;
  highlights: string;
};

function labelForStatus(status: number | string) {
  const value =
    typeof status === "string"
      ? status
      : (["Pending review", "Approved", "Rejected"][status] ??
        "Pending review");
  return value === "Pending review" || value === "PendingReview"
    ? "In review"
    : value;
}

export function GroundedText({
  text,
  mappings = [],
}: {
  text: string;
  mappings?: Array<{ phrase: string; factNumber: number }>;
}) {
  if (!mappings.length) return <>{text}</>;
  const pattern = new RegExp(
    `(${mappings.map((item) => item.phrase.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})`,
    "gi",
  );
  return (
    <>
      {text.split(pattern).map((part, index) => {
        const match = mappings.find(
          (item) => item.phrase.toLowerCase() === part.toLowerCase(),
        );
        return match ? (
          <mark
            className="rounded bg-indigo-100 px-0.5 text-indigo-950 dark:bg-indigo-950 dark:text-indigo-100"
            key={`${part}-${index}`}
          >
            {part}
            <sup className="ml-0.5">{match.factNumber}</sup>
          </mark>
        ) : (
          part
        );
      })}
    </>
  );
}

export function ProductDetail({
  draftAudits,
  dirty,
  progress,
  product,
  catalogName,
  language,
  drafts,
  languageDrafts,
  draft,
  draftTables,
  editing,
  form,
  generating,
  loading,
  busy,
  error,
  onBack,
  onLanguageChange,
  onEditFacts,
  onNewDraft,
  onEditDraft,
  onCancelEdit,
  onFormChange,
  onSaveDraft,
  onReview,
}: {
  dirty?: boolean;
  draftAudits?: Record<
    string,
    {
      model: string;
      promptVersion: string;
      retrievalMode?: string;
      groundingMode: string;
      includeFacts?: boolean;
      requestedTopK?: number;
    }
  >;
  progress?: React.ReactNode;
  product: ProductView;
  catalogName: string;
  language: "de" | "en";
  drafts: DraftView[];
  languageDrafts: DraftView[];
  draft?: DraftView;
  draftTables?: React.ReactNode;
  editing: boolean;
  form: DraftForm;
  generating: boolean;
  loading: boolean;
  busy: boolean;
  error: string;
  onBack: () => void;
  onLanguageChange: (language: "de" | "en") => void;
  onEditFacts: () => void;
  onNewDraft: () => void;
  onEditDraft: () => void;
  onCancelEdit: () => void;
  onFormChange: (form: DraftForm) => void;
  onSaveDraft: () => void;
  onReview: (action: "approve" | "reject") => void;
}) {
  const pending = draft && labelForStatus(draft.status) === "In review";
  const version = draft
    ? languageDrafts.length -
      languageDrafts.findIndex((item) => item.id === draft.id)
    : 0;
  return (
    <PageLayout>
      <PageHeader
        breadcrumbs={[
          "Catalogs",
          { label: catalogName, onClick: onBack },
          product.name,
        ]}
        title={product.name}
        subtitle="Review source facts and manage generated product content."
        action={{
          label: "Edit source facts",
          onClick: onEditFacts,
          icon: <Pencil />,
        }}
      />
      <div className="review-metadata">
        {[
          ["SKU", product.sku],
          ["Brand", product.brand],
          ["Updated", new Date(product.updatedAt).toLocaleDateString()],
        ].map(([label, value]) => (
          <span key={label}>
            {label}
            <strong>{value}</strong>
          </span>
        ))}
      </div>
      <div className="mt-5 flex flex-col gap-3 border-b pb-4 sm:flex-row sm:items-center sm:justify-between">
        <div
          className="language-options"
          role="group"
          aria-label="Draft language"
        >
          {(["de", "en"] as const).map((code) => {
            const latest = drafts.find(
              (item) => item.language.toLowerCase() === code,
            );
            return (
              <button
                type="button"
                aria-pressed={language === code}
                key={code}
                onClick={() => onLanguageChange(code)}
              >
                <strong>{code === "de" ? "Deutsch" : "English"}</strong>
                <span className="ml-2 text-xs text-muted-foreground">
                  {latest ? labelForStatus(latest.status) : "No draft"}
                </span>
              </button>
            );
          })}
        </div>
        {!draft && (
          <Button onClick={onNewDraft} disabled={generating}>
            <Sparkles />
            {generating ? "Generating…" : "New draft"}
          </Button>
        )}
      </div>
      {progress}
      <div className="product-review-grid">
        <Card className="gap-0 p-4 shadow-none">
          <SectionHeader
            title="Source facts"
            description="Verified product specifications"
            action={
              <Badge variant="outline">{product.attributes.length}</Badge>
            }
          />
          <ol className="source-facts">
            {product.attributes.map((fact, index) => (
              <li className="source-fact" key={`${fact.name}-${index}`}>
                <span className="source-fact-number">
                  {String(index + 1).padStart(2, "0")}
                </span>
                <strong>{fact.name}</strong>
                <span className="source-fact-value">
                  {fact.value}
                  {fact.unit ? ` ${fact.unit}` : ""}
                </span>
              </li>
            ))}
          </ol>
          {product.attributes.length === 0 && (
            <p className="py-6 text-center text-xs text-muted-foreground">
              No source facts supplied.
            </p>
          )}
          <p className="mt-4 border-t pt-3 text-xs leading-5 text-muted-foreground">
            Source facts are the foundation for generated content. Add product
            knowledge to provide additional context.
          </p>
        </Card>
        <Card className="gap-0 p-0 shadow-none">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b p-4">
            <div className="flex items-center gap-2">
              <div>
                <h2 className="font-semibold">AI draft</h2>
                <p className="text-xs text-muted-foreground">
                  {draft
                    ? `Version ${version}`
                    : language === "de"
                      ? "German"
                      : "English"}
                </p>
              </div>
              {draft && (
                <StatusBadge
                  status={
                    labelForStatus(draft.status) as
                      | "In review"
                      | "Approved"
                      | "Rejected"
                  }
                />
              )}
              {draft?.isCurrent && <StatusBadge status="Current" />}
            </div>
            {draft && !editing && (
              <div className="flex flex-wrap gap-2">
                {pending && (
                  <Button size="sm" variant="ghost" onClick={onEditDraft}>
                    <Pencil />
                    Edit
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy || generating}
                  onClick={onNewDraft}
                >
                  <RefreshCw />
                  Generate
                </Button>
                {pending && (
                  <>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => onReview("reject")}
                      disabled={busy}
                    >
                      <X />
                      Reject
                    </Button>
                    <Button
                      size="sm"
                      onClick={() => onReview("approve")}
                      disabled={busy}
                    >
                      <Check />
                      Approve
                    </Button>
                  </>
                )}
              </div>
            )}
          </div>
          <div className="p-4">
            {error && (
              <Alert className="mb-5 border-destructive/40 text-destructive">
                <AlertDescription className="flex items-center justify-between gap-4">
                  <span>{error}</span>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={busy || generating}
                    onClick={onNewDraft}
                  >
                    Retry
                  </Button>
                </AlertDescription>
              </Alert>
            )}
            {(generating || loading) && !draft ? (
              <div className="grid gap-3" aria-live="polite">
                <div className="flex items-center gap-3">
                  <span className="size-2 animate-pulse rounded-full bg-primary" />
                  <span className="text-sm font-medium">
                    {generating
                      ? "Generating grounded product content…"
                      : "Loading draft…"}
                  </span>
                </div>
                <Skeleton className="h-8 w-2/3" />
                <Skeleton className="h-20 w-full" />
                <Skeleton className="h-28 w-full" />
                <div className="grid grid-cols-2 gap-3">
                  <Skeleton className="h-12" />
                  <Skeleton className="h-12" />
                </div>
              </div>
            ) : !draft ? (
              <div className="grid min-h-48 place-items-center text-center">
                <div>
                  <Sparkles className="mx-auto mb-3 size-8 text-primary" />
                  <h3 className="font-semibold">
                    No {language === "de" ? "German" : "English"} draft
                  </h3>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Generate content from the verified source facts.
                  </p>
                </div>
              </div>
            ) : editing ? (
              <div className="grid gap-3">
                {dirty && (
                  <p
                    role="status"
                    className="rounded border border-amber-300 bg-amber-50 p-2 text-xs text-amber-950"
                  >
                    Unsaved changes · Save before leaving this draft.
                  </p>
                )}
                <Label className="grid gap-2">
                  Title
                  <Input
                    value={form.title}
                    onChange={(event) =>
                      onFormChange({ ...form, title: event.target.value })
                    }
                  />
                </Label>
                <Label className="grid gap-2">
                  Short description
                  <Textarea
                    rows={3}
                    value={form.shortDescription}
                    onChange={(event) =>
                      onFormChange({
                        ...form,
                        shortDescription: event.target.value,
                      })
                    }
                  />
                </Label>
                <Label className="grid gap-2">
                  Long description
                  <Textarea
                    rows={8}
                    value={form.longDescription}
                    onChange={(event) =>
                      onFormChange({
                        ...form,
                        longDescription: event.target.value,
                      })
                    }
                  />
                </Label>
                <Label className="grid gap-2">
                  Highlights{" "}
                  <small className="font-normal text-muted-foreground">
                    one per line
                  </small>
                  <Textarea
                    rows={5}
                    value={form.highlights}
                    onChange={(event) =>
                      onFormChange({ ...form, highlights: event.target.value })
                    }
                  />
                </Label>
                <div className="flex justify-end gap-2">
                  <Button variant="outline" onClick={onCancelEdit}>
                    Cancel
                  </Button>
                  <Button onClick={onSaveDraft} disabled={busy}>
                    {busy ? "Saving…" : "Save changes"}
                  </Button>
                </div>
              </div>
            ) : (
              <article className="draft-content">
                <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {draft.language === "de" ? "German" : "English"} content
                </p>
                <h3 className="mt-2 text-lg font-semibold tracking-tight">
                  {draft.title}
                </h3>
                <p className="mt-3 text-sm font-medium leading-6">
                  <GroundedText text={draft.shortDescription} />
                </p>
                <p className="mt-4 whitespace-pre-line text-sm leading-6 text-muted-foreground">
                  <GroundedText text={draft.longDescription} />
                </p>
                <ul className="mt-4 grid gap-2">
                  {draft.highlights.map((highlight) => (
                    <li
                      className="flex items-start gap-2 rounded-md bg-muted/50 px-3 py-2 text-xs leading-5"
                      key={highlight}
                    >
                      <Check
                        className="mt-0.5 size-3.5 shrink-0 text-primary"
                        aria-hidden="true"
                      />
                      {highlight}
                    </li>
                  ))}
                </ul>
                <DraftComparison
                  key={draft.id}
                  selected={draft}
                  drafts={languageDrafts}
                  audits={draftAudits}
                />
                <EvidenceReview review={draft.review} />
              </article>
            )}
          </div>
        </Card>
      </div>
      {draftTables}
    </PageLayout>
  );
}
