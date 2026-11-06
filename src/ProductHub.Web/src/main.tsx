import { ConfirmationProvider, useConfirmation } from "@/components/confirmation-provider";
import { useGenerationJobs, jobIsActive } from "@/hooks/use-generation-jobs";
import { KnowledgeDocuments, documentIsReady, type KnowledgeDocumentView } from "@/components/knowledge-documents";
import { GenerationPresets } from "@/components/generation-presets";
import { GenerationAudit } from "@/components/generation-audit";
import { GenerationProgress } from "@/components/generation-progress";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Check,
  CircleCheckBig,
  CircleX,
  Pencil,
  Pin,
  Sparkles,
  Trash2,
  Upload,
  X,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { NativeSelect } from "@/components/ui/native-select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { GenerationSettings, type RetrievalMode } from "@/components/generation-settings";
import { DataTable } from "@/components/data-table";
import { AppShell } from "@/components/app-shell";
import { CatalogOverview } from "@/components/catalog-overview";
import { CatalogDetail } from "@/components/catalog-detail";
import { ProductSheet } from "@/components/product-sheet";
import { ProductDetail } from "@/components/product-detail";
import { ViewerDashboard } from "@/components/viewer-dashboard";
import { Toaster, toast } from "sonner";
import "./shadcn.css";
import "./styles.css";
import "./auth.css";
import "./admin.css";
import "./workspace.css";

const API =
  import.meta.env.VITE_API_URL ??
  `${window.location.protocol}//${window.location.hostname}:5099`;
const AI_MODELS = ["qwen3:8b", "llama3.1:8b"] as const;
type Attribute = { name: string; value: string; unit?: string };
type Catalog = {
  id: string;
  name: string;
  description: string;
  productCount: number;
};
type Product = {
  id: string;
  sku: string;
  name: string;
  brand: string;
  catalogId?: string;
  categoryId?: string;
  attributes: Attribute[];
  updatedAt: string;
  rowVersion: string;
};
type Draft = {
  review?: import("@/components/evidence-review").ClaimReviewReport | null;
  id: string;
  productId: string;
  language: string;
  title: string;
  shortDescription: string;
  longDescription: string;
  highlights: string[];
  status: number | string;
  isCurrent: boolean;
  createdAt: string;
  generationId: string;
};
type Generation = {
  id: string;
  provider: string;
  model: string;
  promptVersion: string;
  durationMs?: number;
  inputTokens?: number;
  outputTokens?: number;
  correlationId: string;
  startedAt: string;
  completedAt?: string;
  status: string;
  errorCode?: string;
  groundingMode: string;
  retrievalMode?: RetrievalMode;
  includeFacts?: boolean;
  requestedTopK?: number;
  retrievalStatus: string;
  retrievalDurationMs?: number;
  retrievedChunkCount: number;
  retrievedDocumentCount: number;
  retrievalSources: { documentId: string; fileName: string; citation: string; score: number; graphHops?: number | null; graphPath?: string[] | null; documentRevision?: string | null; chunkRevision?: string | null; retrievalVersion?: string | null; denseCandidateCount?: number; lexicalCandidateCount?: number; graphRevision?: string | null; graphCacheHit?: boolean | null; graphConcepts?: string[] | null }[];
  retrievalErrorCode?: string;
};
type KnowledgeDocument = KnowledgeDocumentView & {
  id: string;
  productId: string;
  fileName: string;
  status: number | string;
  chunkCount: number;
};
type DraftHistoryRow = {
  draft: Draft;
  version: string;
  title: string;
  createdAt: string;
  status: string;
};
type DraftForm = {
  title: string;
  shortDescription: string;
  longDescription: string;
  highlights: string;
};
type CurrentUser = {
  id: string;
  email: string;
  displayName: string;
  role: "Admin" | "User";
};
type StorefrontProduct = Product & {
  approvedContent?: {
    title: string;
    shortDescription: string;
    longDescription: string;
    highlights: string[];
    language: string;
  };
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    ...init,
    credentials: "include",
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(
      problem?.detail ?? `${response.status} ${response.statusText}`,
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json();
}
const statusName = (status: Draft["status"]) =>
  typeof status === "string"
    ? status
    : (["Pending review", "Approved", "Rejected"][status] ?? "Unknown");
const statusClass = (status: Draft["status"]) =>
  statusName(status).toLowerCase().replace(" ", "-");

const parseAttributes = (value: string): Attribute[] =>
  value
    .split("\n")
    .map<Attribute | null>((line) => {
      const separator = line.indexOf(":");
      if (separator < 1) return null;
      const name = line.slice(0, separator).trim();
      const remainder = line.slice(separator + 1).trim();
      const unitSeparator = remainder.lastIndexOf("|");
      const attributeValue = (
        unitSeparator >= 0 ? remainder.slice(0, unitSeparator) : remainder
      ).trim();
      const unit = unitSeparator >= 0 ? remainder.slice(unitSeparator + 1).trim() : "";
      return name && attributeValue
        ? { name, value: attributeValue, ...(unit ? { unit } : {}) }
        : null;
    })
    .filter((item): item is Attribute => item !== null);

const formatAttributes = (attributes: Attribute[]) =>
  attributes
    .map(
      (attribute) =>
        `${attribute.name}: ${attribute.value}${attribute.unit ? ` | ${attribute.unit}` : ""}`,
    )
    .join("\n");

function App() {
  const confirm = useConfirmation();
  const [currentUser, setCurrentUser] = useState<
    CurrentUser | null | undefined
  >(undefined);
  const [catalogs, setCatalogs] = useState<Catalog[]>([]);
  const [catalogId, setCatalogId] = useState(() => new URLSearchParams(location.search).get("catalog") ?? "");
  const [products, setProducts] = useState<Product[]>([]);
  const [selectedId, setSelectedId] = useState(() => new URLSearchParams(location.search).get("product") ?? "");
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [draftId, setDraftId] = useState("");
  const [draftTab, setDraftTab] = useState("history");
  const [generations, setGenerations] = useState<Record<string, Generation>>({});
  const [language, setLanguage] = useState<"de" | "en">(() => new URLSearchParams(location.search).get("language") === "en" ? "en" : "de");
  const [busy, setBusy] = useState(false);

  const [loadingCatalogs, setLoadingCatalogs] = useState(false);
  const [loadingProducts, setLoadingProducts] = useState(false);
  const [loadingDrafts, setLoadingDrafts] = useState(false);
  const [error, setError] = useState("");
  const [dialog, setDialog] = useState<"catalog" | "product" | "facts" | "generate" | null>(null);
  const [retrievalMode, setRetrievalMode] = useState<RetrievalMode>("None");
  const [includeFacts, setIncludeFacts] = useState(true);
  const useRag = retrievalMode !== "None";
  const [topK, setTopK] = useState(5);
  const [model, setModel] = useState<(typeof AI_MODELS)[number]>(AI_MODELS[0]);
  const [knowledgeDocuments, setKnowledgeDocuments] = useState<KnowledgeDocument[]>([]);
  const hasReadyKnowledge = knowledgeDocuments.some(documentIsReady);
  const [knowledgeFile, setKnowledgeFile] = useState<File | null>(null);
  const [editingDraft, setEditingDraft] = useState(false);
  const [draftForm, setDraftForm] = useState<DraftForm>({
    title: "",
    shortDescription: "",
    longDescription: "",
    highlights: "",
  });
  const [catalogForm, setCatalogForm] = useState({ name: "", description: "" });
  const [productForm, setProductForm] = useState({
    sku: "",
    name: "",
    brand: "",
    attributes: "Material: Stainless steel\nColor: Forest green",
  });
  const [addAnotherProduct, setAddAnotherProduct] = useState(false);
  const [sourceFactsForm, setSourceFactsForm] = useState({
    name: "",
    brand: "",
    attributes: "",
  });
  const activeCatalog = catalogs.find((x) => x.id === catalogId);
  const selected = products.find((p) => p.id === selectedId);
  const languageDrafts = useMemo(
    () => drafts.filter((d) => d.productId === selectedId && d.language.toLowerCase() === language),
    [drafts, language, selectedId],
  );
  const draft =
    languageDrafts.find((d) => d.id === draftId) ?? languageDrafts[0];
  const dirty = Boolean(editingDraft && draft && (draftForm.title !== draft.title || draftForm.shortDescription !== draft.shortDescription || draftForm.longDescription !== draft.longDescription || draftForm.highlights !== draft.highlights.join("\n")));
  const canLeaveDraft = async () => !dirty || await confirm({ title: "Discard unsaved changes?", description: "Your edits to this draft have not been saved. Discard them to continue, or keep editing.", actionLabel: "Discard changes", cancelLabel: "Keep editing", destructive: true });
  const navigate = async (action: () => void) => { if (await canLeaveDraft()) { setEditingDraft(false); action(); } };
  useEffect(() => {
    if (!dirty) return;
    const protect = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", protect);
    return () => window.removeEventListener("beforeunload", protect);
  }, [dirty]);
  useEffect(() => {
    const url = new URL(location.href);
    for (const [key, value] of [["catalog", catalogId], ["product", selectedId], ["language", language]]) {
      if (value) url.searchParams.set(key, value); else url.searchParams.delete(key);
    }
    history.replaceState(null, "", url);
  }, [catalogId, selectedId, language]);
  useEffect(() => {
    if (currentUser?.role !== "Admin" || !selectedId || !catalogId || products.some(product => product.id === selectedId)) return;
    let disposed = false;
    request<Product>(`/api/products/${encodeURIComponent(selectedId)}`).then(product => {
      if (disposed) return;
      if (product.catalogId !== catalogId) { setSelectedId(""); setError("This product is not in the selected catalog."); return; }
      setProducts(items => [product, ...items.filter(item => item.id !== product.id)]);
    }).catch(error => { if (!disposed) { setSelectedId(""); setError((error as Error).message); } });
    return () => { disposed = true; };
  }, [selectedId, catalogId, currentUser?.role, products]);
  const loadCatalogs = async () => {
    setLoadingCatalogs(true);
    try {
      const data = await request<Catalog[]>("/api/catalogs");
      setCatalogs(data);
      setCatalogId((id) => id && data.some((catalog) => catalog.id === id) ? id : "");
    } finally {
      setLoadingCatalogs(false);
    }
  };
  const loadProducts = async (forCatalog = catalogId) => {
    setLoadingProducts(true);
    try {
      const data = await request<{ items: Product[] }>(
        `/api/products?page=1&pageSize=100${forCatalog ? `&catalogId=${forCatalog}` : ""}`,
      );
      setProducts(data.items);
    } finally {
      setLoadingProducts(false);
    }
  };
  const draftRequest = useRef(0);
  const loadDrafts = async (productId: string) => {
    const requestId = ++draftRequest.current;
    setLoadingDrafts(true);
    try {
      const data = await request<Draft[]>(`/api/products/${productId}/drafts`);
      if (requestId !== draftRequest.current) return;
      setDrafts(data);
      setGenerations({});
      setDraftId((id) => data.some((x) => x.id === id) ? id : (data[0]?.id ?? ""));
      const metadata = await Promise.allSettled(data.map(async (item) => {
        const run = await request<Generation>(`/api/drafts/${item.id}/generation`);
        return [item.id, run] as const;
      }));
      if (requestId !== draftRequest.current) return;
      setGenerations(Object.fromEntries(metadata.filter((result): result is PromiseFulfilledResult<readonly [string, Generation]> => result.status === "fulfilled").map((result) => result.value)));
    } finally {
      if (requestId === draftRequest.current) setLoadingDrafts(false);
    }
  };
  useEffect(() => {
    request<CurrentUser>("/api/auth/me")
      .then(setCurrentUser)
      .catch(() => setCurrentUser(null));
  }, []);
  useEffect(() => {
    if (currentUser?.role === "Admin")
      loadCatalogs().catch((e) => setError(e.message));
  }, [currentUser?.role]);
  useEffect(() => {
    if (currentUser?.role === "Admin" && catalogId)
      loadProducts(catalogId).catch((e) => setError(e.message));
  }, [catalogId, currentUser?.role]);
  useEffect(() => {
    if (currentUser?.role === "Admin" && selectedId)
      loadDrafts(selectedId).catch((e) => setError(e.message));
  }, [selectedId, currentUser?.role]);
  useEffect(() => {
    setDraftId(id => drafts.some(draft => draft.id === id && draft.productId === selectedId && draft.language.toLowerCase() === language) ? id : "");
    setEditingDraft(false);
  }, [language, selectedId]);
  useEffect(() => setEditingDraft(false), [draft?.id]);

  const workspaceRef = useRef({ productId: selectedId, editing: editingDraft });
  useEffect(() => { workspaceRef.current = { productId: selectedId, editing: editingDraft }; }, [selectedId, editingDraft]);
  const { jobs, connectionError: jobConnectionError, enqueue, cancel: cancelJob } = useGenerationJobs(API, currentUser?.role === "Admin" ? selectedId : "", (job) => {
    if (workspaceRef.current.productId === job.productId) {
      void loadDrafts(job.productId).then(() => {
        const current = workspaceRef.current;
        if (job.draftId && !current.editing && current.productId === job.productId) setDraftId(job.draftId);
      }).catch(error => toast.error((error as Error).message));
    }
    toast.success("Draft generated and ready for review");
  });
  const generating = jobs.some(jobIsActive);
  const loadKnowledgeDocuments = async (productId: string) => {
    setKnowledgeDocuments(
      await request<KnowledgeDocument[]>(`/api/products/${productId}/documents`),
    );
  };
  const openGenerate = async () => {
    if (!selected) return;
    setDialog("generate");
    setError("");
    try {
      await loadKnowledgeDocuments(selected.id);
    } catch (e) {
      setError((e as Error).message);
    }
  };
  const uploadKnowledge = async () => {
    if (!selected || !knowledgeFile) return;
    setBusy(true);
    setError("");
    try {
      const data = new FormData();
      data.append("file", knowledgeFile);
      data.append("productId", selected.id);
      const response = await fetch(`${API}/api/documents`, {
        method: "POST",
        credentials: "include",
        body: data,
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? "Knowledge upload failed.");
      }
      await loadKnowledgeDocuments(selected.id);
      setKnowledgeFile(null);
      toast.success("Knowledge document uploaded");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Knowledge upload failed");
    } finally {
      setBusy(false);
    }
  };
  const deleteKnowledge = async (document: KnowledgeDocumentView) => {
    if (!selected || !await confirm({ title: "Remove knowledge source?", description: `Remove ${document.fileName} from this product’s knowledge? Existing draft evidence snapshots will be retained.`, actionLabel: "Remove source", destructive: true }))
      return;
    setBusy(true);
    setError("");
    try {
      await request<void>(
        `/api/products/${selected.id}/documents/${document.id}`,
        { method: "DELETE" },
      );
      await loadKnowledgeDocuments(selected.id);
      toast.success("Knowledge document removed");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not remove document");
    } finally {
      setBusy(false);
    }
  };
  const supersedeKnowledge = async (documentId: string, replacementDocumentId: string) => {
    if (!selected) return;
    setBusy(true); setError("");
    try {
      await request(`/api/products/${selected.id}/documents/${documentId}/supersede`, { method: "POST", body: JSON.stringify({ replacementDocumentId }) });
      await loadKnowledgeDocuments(selected.id);
      toast.success("Old source superseded; future retrieval uses active revisions");
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  };
  const generate = async () => {
    if (!selected || generating) return;
    setBusy(true); setError("");
    try {
      await enqueue({ language, retrievalMode, includeFacts, topK, model });
      setDialog(null);
      toast.success("Generation queued");
    } catch (error) { setError((error as Error).message); toast.error("Could not queue generation"); }
    finally { setBusy(false); }
  };
  const review = async (action: "approve" | "reject") => {
    if (!draft) return;
    const acknowledgeWarnings = action === "approve" && Boolean((draft.review?.requiresAcknowledgement ?? draft.review?.claims.some(claim => claim.status !== "Supported")));
    if (acknowledgeWarnings && !await confirm({ title: "Approve flagged draft?", description: `“${draft.title}” contains claims that need review. By approving, you acknowledge that you inspected the saved evidence and accept this version.`, actionLabel: "Acknowledge & approve", cancelLabel: "Keep reviewing" })) return;
    setBusy(true);
    setError("");
    try {
      const updated = await request<Draft>(
        `/api/drafts/${draft.id}/${action}`,
        { method: "POST", body: action === "approve" ? JSON.stringify({ acknowledgeWarnings }) : undefined },
      );
      setDrafts((items) =>
        items.map((x) =>
          x.id === updated.id
            ? updated
            : updated.isCurrent && x.language === updated.language
              ? { ...x, isCurrent: false }
              : x,
        ),
      );
      toast.success(action === "approve" ? "Draft approved" : "Draft rejected");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Draft review failed");
    } finally {
      setBusy(false);
    }
  };
  const makeCurrent = async (draftToPublish: Draft) => {
    setBusy(true);
    setError("");
    try {
      const updated = await request<Draft>(
        `/api/drafts/${draftToPublish.id}/current`,
        { method: "POST" },
      );
      setDrafts((items) =>
        items.map((x) =>
          x.id === updated.id
            ? updated
            : x.productId === updated.productId &&
                x.language === updated.language
              ? { ...x, isCurrent: false }
              : x,
        ),
      );
      setDraftId(updated.id);
      toast.success("Current draft updated");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not update current draft");
    } finally {
      setBusy(false);
    }
  };
  const reviewMany = async (
    draftsToReview: Draft[],
    action: "approve" | "reject",
    clearSelection: () => void,
  ) => {
    if (draftsToReview.length === 0) return;
    if (action === "approve" && draftsToReview.some(item => (item.review?.requiresAcknowledgement ?? item.review?.claims.some(claim => claim.status !== "Supported")))) {
      toast.error("Open drafts with claim warnings and approve them individually after inspecting the evidence.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await Promise.all(
        draftsToReview.map((item) =>
          request<Draft>(`/api/drafts/${item.id}/${action}`, {
            method: "POST",
          }),
        ),
      );
      if (selected) await loadDrafts(selected.id);
      clearSelection();
    } catch (e) {
      setError((e as Error).message);
      if (selected) await loadDrafts(selected.id).catch(() => undefined);
    } finally {
      setBusy(false);
    }
  };
  const beginEditDraft = () => {
    if (!draft) return;
    setDraftForm({
      title: draft.title,
      shortDescription: draft.shortDescription,
      longDescription: draft.longDescription,
      highlights: draft.highlights.join("\n"),
    });
    setEditingDraft(true);
  };
  const saveDraft = async () => {
    if (!draft) return;
    setBusy(true);
    setError("");
    const highlights = draftForm.highlights
      .split("\n")
      .map((x) => x.trim())
      .filter(Boolean);
    try {
      const updated = await request<Draft>(`/api/drafts/${draft.id}`, {
        method: "PUT",
        body: JSON.stringify({ ...draftForm, highlights }),
      });
      setDrafts((items) =>
        items.map((x) => (x.id === updated.id ? updated : x)),
      );
      setEditingDraft(false);
      toast.success("Draft saved");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not save draft");
    } finally {
      setBusy(false);
    }
  };
  const createCatalog = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const created = await request<Catalog>("/api/catalogs", {
        method: "POST",
        body: JSON.stringify(catalogForm),
      });
      await loadCatalogs();
      setCatalogId(created.id);
      setCatalogForm({ name: "", description: "" });
      setDialog(null);
      toast.success("Catalog created");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not create catalog");
    } finally {
      setBusy(false);
    }
  };
  const createProduct = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    const attributes = parseAttributes(productForm.attributes);
    try {
      const created = await request<Product>("/api/products", {
        method: "POST",
        body: JSON.stringify({
          ...productForm,
          catalogId,
          categoryId: null,
          attributes,
        }),
      });
      await loadProducts(catalogId);
      await loadCatalogs();
      setProductForm({ sku: "", name: "", brand: "", attributes: "" });
      toast.success("Product added");
      if (addAnotherProduct) {
        setSelectedId("");
      } else {
        setSelectedId(created.id);
        setDialog(null);
      }
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not add product");
    } finally {
      setBusy(false);
    }
  };
  const openSourceFacts = () => {
    if (!selected) return;
    setSourceFactsForm({
      name: selected.name,
      brand: selected.brand,
      attributes: formatAttributes(selected.attributes),
    });
    setDialog("facts");
  };
  const updateSourceFacts = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!selected) return;
    setBusy(true);
    setError("");
    try {
      const updated = await request<Product>(`/api/products/${selected.id}`, {
        method: "PUT",
        body: JSON.stringify({
          name: sourceFactsForm.name,
          brand: sourceFactsForm.brand,
          categoryId: selected.categoryId ?? null,
          attributes: parseAttributes(sourceFactsForm.attributes),
          rowVersion: selected.rowVersion,
        }),
      });
      setProducts((items) => items.map((item) => (item.id === updated.id ? updated : item)));
      setDialog(null);
      toast.success("Source facts updated");
    } catch (e) {
      setError((e as Error).message);
      toast.error("Could not update source facts");
    } finally {
      setBusy(false);
    }
  };
  const pending =
    draft && statusName(draft.status).toLowerCase().includes("pending");
  const renderDraftSelectionActions = (
    rows: Array<{ draft: Draft }>,
    clearSelection: () => void,
  ) => {
    const selectedDrafts = rows.map((row) => row.draft);
    const approvableDrafts = selectedDrafts.filter((item) =>
      statusName(item.status).toLowerCase().includes("pending"),
    );
    const rejectableDrafts = selectedDrafts.filter((item) =>
      ["Pending review", "Approved"].includes(statusName(item.status)),
    );
    const onlyDraft = selectedDrafts.length === 1 ? selectedDrafts[0] : null;
    const singleApproved =
      onlyDraft && statusName(onlyDraft.status) === "Approved"
        ? onlyDraft
        : null;

    return (
      <>
        {rejectableDrafts.length > 0 && (
          <Button
            size="sm"
            variant="outline"
            className="tablecn-reject-action"
            disabled={busy}
            onClick={() => reviewMany(rejectableDrafts, "reject", clearSelection)}
          >
            <CircleX />
            Reject{rejectableDrafts.length > 1 ? ` (${rejectableDrafts.length})` : ""}
          </Button>
        )}
        {approvableDrafts.length > 0 && (
          <Button
            size="sm"
            disabled={busy}
            onClick={() => reviewMany(approvableDrafts, "approve", clearSelection)}
          >
            <CircleCheckBig />
            Approve{approvableDrafts.length > 1 ? ` (${approvableDrafts.length})` : ""}
          </Button>
        )}
        {singleApproved && (
          <Button
            size="sm"
            disabled={busy || singleApproved.isCurrent}
            onClick={async () => {
              await makeCurrent(singleApproved);
              clearSelection();
            }}
          >
            <Pin />
            {singleApproved.isCurrent ? "Current" : "Make current"}
          </Button>
        )}
      </>
    );
  };
  const draftHistoryData: DraftHistoryRow[] = languageDrafts.map((item, index) => ({
    draft: item,
    version: index === 0 ? "Latest" : `Version ${languageDrafts.length - index}`,
    title: item.title,
    createdAt: item.createdAt,
    status: statusName(item.status),
  }));
  const draftHistoryColumns: ColumnDef<DraftHistoryRow>[] = [
    { accessorKey: "version", header: "Version", meta: { label: "Version" }, cell: ({ row }) => <span className="font-medium">{row.original.version}</span> },
    { accessorKey: "title", header: "Title", meta: { label: "Title" } },
    { accessorKey: "createdAt", header: "Created", meta: { label: "Created" }, cell: ({ row }) => new Date(row.original.createdAt).toLocaleString([], { dateStyle: "medium", timeStyle: "short" }) },
    { accessorKey: "status", header: "Status", meta: { label: "Status" }, cell: ({ row }) => <Badge variant="outline" className={statusClass(row.original.draft.status)}>{row.original.status}</Badge> },
    {
      id: "published", header: "Published", meta: { label: "Published" }, enableSorting: false,
      cell: ({ row }) => {
        const item = row.original.draft;
        if (item.isCurrent)
          return <Badge className="current-status">Current</Badge>;
        if (row.original.status !== "Approved") return <span className="not-published">—</span>;
        return <Button size="sm" variant="outline" disabled={busy} onClick={() => makeCurrent(item)}>Make current</Button>;
      },
    },
    {
      id: "action", header: "Action", meta: { label: "Action" }, enableSorting: false,
      cell: ({ row }) => {
        const item = row.original.draft;
        const isSelected = item.id === draft?.id;
        return <Button size="sm" variant={isSelected ? "secondary" : "ghost"} disabled={isSelected} onClick={() => navigate(() => setDraftId(item.id))}>{isSelected ? "Viewing" : "View"}</Button>;
      },
    },
  ];
  const logout = async () => {
    if (!await canLeaveDraft()) return;
    setEditingDraft(false);
    await fetch(`${API}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });
    setCurrentUser(null);
  };
  if (currentUser === undefined)
    return <div className="auth-loading">Loading Product Intelligence…</div>;
  if (currentUser === null) return <Login onLogin={setCurrentUser} />;
  if (currentUser.role !== "Admin")
    return <Storefront user={currentUser} onLogout={logout} />;

  if (!catalogId)
    return <AppShell user={currentUser} onNavigate={() => setCatalogId("")} onLogout={logout}>
      <div className="admin-surface">
        {error && <Alert className="m-6 border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}
        <CatalogOverview catalogs={catalogs} loading={loadingCatalogs} onCreate={() => setDialog("catalog")} onOpen={(id) => { setSelectedId(""); setCatalogId(id); }} />
        <Dialog open={dialog === "catalog"} onOpenChange={(open) => !open && !busy && setDialog(null)}>
          <DialogContent>
            <form className="grid gap-5" onSubmit={createCatalog}>
              <DialogHeader><DialogTitle>Create catalog</DialogTitle><DialogDescription>Group related products into a focused collection.</DialogDescription></DialogHeader>
              <Label className="grid gap-2"><span>Name</span><Input required maxLength={120} value={catalogForm.name} onChange={(event) => setCatalogForm((value) => ({ ...value, name: event.target.value }))} placeholder="Outdoor Essentials" /></Label>
              <Label className="grid gap-2"><span>Description</span><Textarea maxLength={500} value={catalogForm.description} onChange={(event) => setCatalogForm((value) => ({ ...value, description: event.target.value }))} placeholder="What belongs in this catalog?" /></Label>
              {error && <Alert className="border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}
              <DialogFooter><Button type="button" variant="outline" disabled={busy} onClick={() => setDialog(null)}>Cancel</Button><Button disabled={busy}>{busy ? "Creating…" : "Create catalog"}</Button></DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </AppShell>;

  if (!selectedId && activeCatalog)
    return <AppShell user={currentUser} onNavigate={() => navigate(() => { setCatalogId(""); setSelectedId(""); })} onLogout={logout}>
      <div className="admin-surface">
        {error && <Alert className="m-6 border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}
        <CatalogDetail key={`${currentUser.id}:${activeCatalog.id}`} api={API} userId={currentUser.id} catalog={activeCatalog} refreshKey={products.map(product => `${product.id}:${product.updatedAt}`).join("|")} onBack={() => setCatalogId("")} onOpenProduct={(product, reviewLanguage) => { setProducts(items => [product, ...items.filter(item => item.id !== product.id)]); setLanguage(reviewLanguage); setSelectedId(product.id); }} onAddProduct={() => setDialog("product")} />
        <ProductSheet open={dialog === "product"} catalogName={activeCatalog.name} form={productForm} busy={busy} error={error} addAnother={addAnotherProduct} onOpenChange={(open) => { if (!open && !busy) setDialog(null); }} onFormChange={setProductForm} onAddAnotherChange={setAddAnotherProduct} onSubmit={createProduct} />
      </div>
    </AppShell>;

  if (selected && activeCatalog)
    return <AppShell user={currentUser} onNavigate={() => navigate(() => { setCatalogId(""); setSelectedId(""); })} onLogout={logout}>
      <div className="admin-surface">
        <ProductDetail
          dirty={dirty}
          draftAudits={generations}
          progress={<GenerationProgress onOpenAudit={() => setDraftTab("audit")} jobs={jobs} error={jobConnectionError} onCancel={(id) => { void cancelJob(id).catch(error => toast.error((error as Error).message)); }} />}
          product={selected}
          catalogName={activeCatalog.name}
          language={language}
          drafts={drafts.filter(item => item.productId === selected.id)}
          languageDrafts={languageDrafts}
          draft={draft}
          draftTables={
            <Tabs value={draftTab} onValueChange={setDraftTab} className="mt-4">
              <TabsList>
                <TabsTrigger value="history">Draft history <Badge variant="secondary">{languageDrafts.length}</Badge></TabsTrigger>
                <TabsTrigger value="audit">Model audit</TabsTrigger>
              </TabsList>
              <TabsContent value="history" className="mt-3">
                <DataTable columns={draftHistoryColumns} data={draftHistoryData} getRowId={(row) => row.draft.id} renderSelectedActions={renderDraftSelectionActions} searchPlaceholder="Filter draft history…" emptyMessage="No draft versions found." />
              </TabsContent>
              <TabsContent value="audit" className="mt-3">
                <GenerationAudit key={selected.id} api={API} productId={selected.id} refreshKey={jobs.map(job => `${job.id}:${job.status}:${job.stage}`).join("|")} selectedDraftId={draft?.id} onViewDraft={(id, targetLanguage) => navigate(() => { setLanguage(targetLanguage === "en" ? "en" : "de"); setDraftId(id); })} />
              </TabsContent>
            </Tabs>
          }
          editing={editingDraft}
          form={draftForm}
          generating={generating}
          loading={loadingDrafts}
          busy={busy}
          error={error}
          onBack={() => navigate(() => setSelectedId(""))}
          onLanguageChange={value => navigate(() => setLanguage(value))}
          onEditFacts={openSourceFacts}
          onNewDraft={openGenerate}
          onEditDraft={beginEditDraft}
          onCancelEdit={() => navigate(() => setEditingDraft(false))}
          onFormChange={setDraftForm}
          onSaveDraft={saveDraft}
          onReview={review}
        />
        <Dialog open={dialog === "facts"} onOpenChange={(open) => !open && !busy && setDialog(null)}><DialogContent><form className="grid gap-5" onSubmit={updateSourceFacts}><DialogHeader><DialogTitle>Edit source facts</DialogTitle><DialogDescription>Maintain the verified specifications used for generation.</DialogDescription></DialogHeader><div className="grid gap-4 sm:grid-cols-2"><Label className="grid gap-2">Product name<Input required value={sourceFactsForm.name} onChange={(event) => setSourceFactsForm((value) => ({ ...value, name: event.target.value }))} /></Label><Label className="grid gap-2">Brand<Input required value={sourceFactsForm.brand} onChange={(event) => setSourceFactsForm((value) => ({ ...value, brand: event.target.value }))} /></Label></div><Label className="grid gap-2">Source facts <small className="font-normal text-muted-foreground">one per line: Name: Value | Unit</small><Textarea rows={10} value={sourceFactsForm.attributes} onChange={(event) => setSourceFactsForm((value) => ({ ...value, attributes: event.target.value }))} /></Label>{error && <Alert className="border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}<DialogFooter><Button type="button" variant="outline" onClick={() => setDialog(null)}>Cancel</Button><Button disabled={busy}>{busy ? "Saving…" : "Save source facts"}</Button></DialogFooter></form></DialogContent></Dialog>
        <Dialog open={dialog === "generate"} onOpenChange={(open) => !open && !busy && setDialog(null)}><DialogContent className="max-h-[calc(100dvh-2rem)] max-w-2xl overflow-y-auto"><div className="grid gap-5"><DialogHeader><DialogTitle>New {language === "de" ? "German" : "English"} draft</DialogTitle><DialogDescription>Choose the model and grounding source for this generation.</DialogDescription></DialogHeader><Label className="grid gap-2">Model<NativeSelect className="w-full" value={model} onChange={(event) => setModel(event.target.value as (typeof AI_MODELS)[number])}>{AI_MODELS.map((item) => <option key={item} value={item}>{item}</option>)}</NativeSelect></Label><GenerationPresets userId={currentUser.id} settings={{ model, retrievalMode, includeFacts, topK }} onApply={value => { if (AI_MODELS.includes(value.model as (typeof AI_MODELS)[number])) setModel(value.model as (typeof AI_MODELS)[number]); setRetrievalMode(value.retrievalMode); setIncludeFacts(value.includeFacts); setTopK(value.topK); }} /><GenerationSettings includeFacts={includeFacts} onIncludeFactsChange={setIncludeFacts} retrievalMode={retrievalMode} onRetrievalModeChange={setRetrievalMode} topK={topK} onTopKChange={setTopK} hasKnowledge={hasReadyKnowledge} disabled={busy} /><div className="grid gap-3 rounded-md border p-4"><div className="flex items-center justify-between"><strong>Product knowledge</strong><Badge variant="outline">{knowledgeDocuments.length} documents</Badge></div><KnowledgeDocuments documents={knowledgeDocuments} busy={busy} onRemove={deleteKnowledge} onSupersede={supersedeKnowledge} /><Label className="grid gap-2">Upload instructions<Input type="file" accept=".txt,.md,.pdf" onChange={(event) => setKnowledgeFile(event.target.files?.[0] ?? null)} /></Label><Button type="button" variant="outline" disabled={busy || !knowledgeFile} onClick={uploadKnowledge}><Upload />Upload and index</Button></div>{error && <Alert className="border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}<DialogFooter><Button type="button" variant="outline" onClick={() => setDialog(null)}>Cancel</Button><Button type="button" disabled={busy || (useRag && !hasReadyKnowledge)} onClick={generate}><Sparkles />Generate draft</Button></DialogFooter></div></DialogContent></Dialog>
      </div>
    </AppShell>;

  return <AppShell user={currentUser} onNavigate={() => navigate(() => { setCatalogId(""); setSelectedId(""); })} onLogout={logout}>
    <div className="mx-auto grid min-h-64 max-w-xl content-center gap-4 p-6 text-center">
      {error ? <Alert className="border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert> : <p role="status" aria-live="polite" className="text-sm text-muted-foreground">Loading workspace…</p>}
      <Button variant="outline" onClick={() => navigate(() => { setCatalogId(""); setSelectedId(""); })}>Back to catalogs</Button>
    </div>
  </AppShell>;
}

function Login({ onLogin }: { onLogin: (user: CurrentUser) => void }) {
  const [email, setEmail] = useState("admin@producthub.local");
  const [password, setPassword] = useState("Admin123!");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      onLogin(
        await request<CurrentUser>("/api/auth/login", {
          method: "POST",
          body: JSON.stringify({ email, password, rememberMe: true }),
        }),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const useDemo = (role: "admin" | "viewer") => {
    setEmail(`${role}@producthub.local`);
    setPassword(role === "admin" ? "Admin123!" : "Viewer123!");
  };
  return (
    <main className="login-page">
      <section className="login-intro">
        <div className="brand-mark">PI</div>
        <p className="eyebrow">Product Intelligence Hub</p>
        <h1>Better product content starts with trustworthy facts.</h1>
        <p>
          Admins prepare and approve AI-assisted product descriptions. Catalog
          users see only the finished, approved result.
        </p>
      </section>
      <Card className="login-card">
        <p className="eyebrow">Welcome back</p>
        <h2>Sign in</h2>
        <p>Use a local demo account to enter the workspace.</p>
        {error && (
          <Alert className="error-banner">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <form onSubmit={submit}>
          <Label className="form-field">
            <span>Email</span>
            <Input
              type="email" autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </Label>
          <Label className="form-field">
            <span>Password</span>
            <Input
              type="password" autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Label>
          <Button disabled={busy}>
            {busy ? "Signing in…" : "Sign in"}
          </Button>
        </form>
        <div className="demo-accounts">
          <Button variant="outline" onClick={() => useDemo("admin")}>
            <strong>Admin demo</strong>
            <small>Manage catalogs and content</small>
          </Button>
          <Button variant="outline" onClick={() => useDemo("viewer")}>
            <strong>Viewer demo</strong>
            <small>Browse approved products</small>
          </Button>
        </div>
      </Card>
    </main>
  );
}

function Storefront({
  user,
  onLogout,
}: {
  user: CurrentUser;
  onLogout: () => void;
}) {
  const [catalogs, setCatalogs] = useState<Catalog[]>([]);
  const [catalogId, setCatalogId] = useState(() => new URLSearchParams(location.search).get("catalog") ?? "");
  const [products, setProducts] = useState<StorefrontProduct[]>([]);
  const [language, setLanguage] = useState<"de" | "en">(() => new URLSearchParams(location.search).get("language") === "en" ? "en" : "de");
  const [error, setError] = useState("");
  useEffect(() => {
    request<Catalog[]>("/api/catalogs")
      .then((data) => {
        setCatalogs(data);
        setCatalogId(data[0]?.id ?? "");
      })
      .catch((e) => setError(e.message));
  }, []);
  useEffect(() => {
    if (catalogId)
      request<StorefrontProduct[]>(
        `/api/storefront/catalogs/${catalogId}/products?language=${language}`,
      )
        .then(setProducts)
        .catch((e) => setError(e.message));
  }, [catalogId, language]);
  const active = catalogs.find((x) => x.id === catalogId);
  const text =
    language === "de"
      ? {
          catalog: "Produktkatalog",
          collections: "Northwind Kollektionen",
          viewer: "Katalogansicht",
          signout: "Abmelden",
          products: "Produkte",
          curated: "Ausgewählte Kollektion",
          waiting:
            "Für dieses Produkt gibt es noch keine freigegebene deutsche Beschreibung.",
          badge: "Deutsche Beschreibung fehlt",
          facts: "Produktdaten",
        }
      : {
          catalog: "Product catalog",
          collections: "Northwind collections",
          viewer: "Catalog viewer",
          signout: "Sign out",
          products: "products",
          curated: "Curated collection",
          waiting:
            "This product does not yet have an approved English description.",
          badge: "English description missing",
          facts: "Product facts",
        };
  return <ViewerDashboard
    user={user}
    catalogs={catalogs}
    catalogId={catalogId}
    activeCatalog={active}
    products={products}
    language={language}
    error={error}
    text={text}
    onCatalogChange={setCatalogId}
    onLanguageChange={setLanguage}
    onLogout={onLogout}
  />;
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><ConfirmationProvider><App /></ConfirmationProvider><Toaster richColors position="top-right" /></React.StrictMode>);
