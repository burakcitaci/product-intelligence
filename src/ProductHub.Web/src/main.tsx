import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Check,
  CircleCheckBig,
  CircleX,
  ChevronDown,
  History,
  Menu,
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
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
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
import { DataTable } from "@/components/data-table";
import { AppShell } from "@/components/app-shell";
import { CatalogOverview } from "@/components/catalog-overview";
import { CatalogDetail } from "@/components/catalog-detail";
import { ProductSheet } from "@/components/product-sheet";
import { Toaster, toast } from "sonner";
import "./shadcn.css";
import "./styles.css";
import "./responsive.css";
import "./catalog.css";
import "./draft-editor.css";
import "./auth.css";
import "./calm-admin.css";
import "./dashboard.css";
import "./admin.css";

const API =
  import.meta.env.VITE_API_URL ??
  `${window.location.protocol}//${window.location.hostname}:5099`;
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
  requestedTopK?: number;
  retrievalStatus: string;
  retrievalDurationMs?: number;
  retrievedChunkCount: number;
  retrievedDocumentCount: number;
  retrievalSources: { documentId: string; fileName: string; citation: string; score: number }[];
  retrievalErrorCode?: string;
};
type KnowledgeDocument = {
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
type ModelAuditRow = {
  draft: Draft;
  version: string;
  run?: Generation;
  startedAt: string;
  model: string;
  promptVersion: string;
  latencyMs: number;
  tokens: number;
  runStatus: string;
  trace: string;
  grounding: string;
  topK: string;
  evidence: string;
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
  const [currentUser, setCurrentUser] = useState<
    CurrentUser | null | undefined
  >(undefined);
  const [catalogs, setCatalogs] = useState<Catalog[]>([]);
  const [catalogId, setCatalogId] = useState("");
  const [products, setProducts] = useState<Product[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [draftId, setDraftId] = useState("");
  const [generations, setGenerations] = useState<Record<string, Generation>>({});
  const [language, setLanguage] = useState<"de" | "en">("de");
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [dialog, setDialog] = useState<"catalog" | "product" | "facts" | "generate" | null>(null);
  const [useRag, setUseRag] = useState(false);
  const [topK, setTopK] = useState(5);
  const [knowledgeDocuments, setKnowledgeDocuments] = useState<KnowledgeDocument[]>([]);
  const [knowledgeFile, setKnowledgeFile] = useState<File | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(false);
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
    () => drafts.filter((d) => d.language.toLowerCase() === language),
    [drafts, language],
  );
  const draft =
    languageDrafts.find((d) => d.id === draftId) ?? languageDrafts[0];
  const filteredProducts = useMemo(() => {
    const term = query.trim().toLowerCase();
    return term
      ? products.filter((p) =>
          `${p.name} ${p.sku} ${p.brand}`.toLowerCase().includes(term),
        )
      : products;
  }, [products, query]);

  const loadCatalogs = async () => {
    const data = await request<Catalog[]>("/api/catalogs");
    setCatalogs(data);
    setCatalogId((id) => id && data.some((catalog) => catalog.id === id) ? id : "");
  };
  const loadProducts = async (forCatalog = catalogId) => {
    const data = await request<{ items: Product[] }>(
      `/api/products?page=1&pageSize=100${forCatalog ? `&catalogId=${forCatalog}` : ""}`,
    );
    setProducts(data.items);
    setSelectedId((id) => data.items.some((product) => product.id === id) ? id : "");
  };
  const loadDrafts = async (productId: string) => {
    const data = await request<Draft[]>(`/api/products/${productId}/drafts`);
    setDrafts(data);
    setGenerations({});
    setDraftId((id) =>
      data.some((x) => x.id === id) ? id : (data[0]?.id ?? ""),
    );
    const metadata = await Promise.allSettled(
      data.map(async (item) => {
        const run = await request<Generation>(
          `/api/drafts/${item.id}/generation`,
        );
        return [item.id, run] as const;
      }),
    );
    setGenerations(
      Object.fromEntries(
        metadata
          .filter(
            (result): result is PromiseFulfilledResult<readonly [string, Generation]> =>
              result.status === "fulfilled",
          )
          .map((result) => result.value),
      ),
    );
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
    setDraftId(languageDrafts[0]?.id ?? "");
    setEditingDraft(false);
  }, [languageDrafts, selectedId]);
  useEffect(() => setEditingDraft(false), [draft?.id]);

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
      setUseRag(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const deleteKnowledge = async (document: KnowledgeDocument) => {
    if (!selected || !window.confirm(`Remove ${document.fileName} from this product's knowledge?`))
      return;
    setBusy(true);
    setError("");
    try {
      await request<void>(
        `/api/products/${selected.id}/documents/${document.id}`,
        { method: "DELETE" },
      );
      await loadKnowledgeDocuments(selected.id);
      if (knowledgeDocuments.length <= 1) setUseRag(false);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const generate = async () => {
    if (!selected) return;
    setBusy(true);
    setError("");
    try {
      const created = await request<Draft>(
        `/api/products/${selected.id}/drafts/generate`,
        {
          method: "POST",
          headers: { "Idempotency-Key": crypto.randomUUID() },
          body: JSON.stringify({ language, useRag, topK }),
        },
      );
      await loadDrafts(selected.id);
      setDraftId(created.id);
      setDialog(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const review = async (action: "approve" | "reject") => {
    if (!draft) return;
    setBusy(true);
    setError("");
    try {
      const updated = await request<Draft>(
        `/api/drafts/${draft.id}/${action}`,
        { method: "POST" },
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
    } catch (e) {
      setError((e as Error).message);
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
    } catch (e) {
      setError((e as Error).message);
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
    } catch (e) {
      setError((e as Error).message);
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
    } catch (e) {
      setError((e as Error).message);
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
        return <Button size="sm" variant={isSelected ? "secondary" : "ghost"} disabled={isSelected} onClick={() => setDraftId(item.id)}>{isSelected ? "Viewing" : "View"}</Button>;
      },
    },
  ];
  const modelAuditData: ModelAuditRow[] = languageDrafts.map((item, index) => {
    const run = generations[item.id];
    return {
      draft: item,
      version: index === 0 ? "Latest" : `Version ${languageDrafts.length - index}`,
      run,
      startedAt: run?.startedAt ?? "",
      model: run ? `${run.provider} · ${run.model}` : "",
      promptVersion: run?.promptVersion ?? "",
      latencyMs: run?.durationMs ?? 0,
      tokens: (run?.inputTokens ?? 0) + (run?.outputTokens ?? 0),
      runStatus: run?.status ?? "Loading",
      trace: run?.correlationId ?? "",
      grounding: run?.groundingMode === "Rag" ? "RAG" : "Facts only",
      topK: run?.groundingMode === "Rag"
        ? `${run.requestedTopK ?? "—"} requested → ${run.retrievedChunkCount} returned`
        : "—",
      evidence: run?.groundingMode === "Rag"
        ? run.retrievalStatus === "Succeeded"
          ? `${run.retrievedChunkCount} chunks · ${run.retrievedDocumentCount} docs`
              + (run.retrievalSources?.length
                ? ` · ${[...new Set(run.retrievalSources.map((source) => source.fileName))].join(", ")}`
                : "")
          : run.retrievalErrorCode ?? run.retrievalStatus
        : "Not requested",
    };
  });
  const modelAuditColumns: ColumnDef<ModelAuditRow>[] = [
    { accessorKey: "version", header: "Draft", meta: { label: "Draft" } },
    { id: "runId", header: "Run ID", meta: { label: "Run ID" }, accessorFn: (row) => row.run?.id ?? "", cell: ({ row }) => <code>{row.original.run?.id.slice(0, 8) ?? "—"}</code> },
    { accessorKey: "startedAt", header: "Started", meta: { label: "Started" }, cell: ({ row }) => row.original.startedAt ? new Date(row.original.startedAt).toLocaleString([], { dateStyle: "medium", timeStyle: "short" }) : "Loading…" },
    { accessorKey: "model", header: "Model", meta: { label: "Model" }, cell: ({ row }) => row.original.model || "—" },
    { accessorKey: "promptVersion", header: "Prompt", meta: { label: "Prompt" }, cell: ({ row }) => row.original.promptVersion || "—" },
    { accessorKey: "grounding", header: "Grounding", meta: { label: "Grounding" }, cell: ({ row }) => <Badge variant={row.original.grounding === "RAG" ? "default" : "outline"}>{row.original.grounding}</Badge> },
    { accessorKey: "topK", header: "Top-K", meta: { label: "Top-K" } },
    { accessorKey: "evidence", header: "Evidence", meta: { label: "Evidence" }, cell: ({ row }) => {
      const run = row.original.run;
      return <span title={run?.retrievalSources?.map((x) => `${x.citation} (${x.score.toFixed(3)})`).join("\n")}>{row.original.evidence}</span>;
    } },
    { accessorKey: "latencyMs", header: "Latency", meta: { label: "Latency" }, cell: ({ row }) => row.original.latencyMs ? `${(row.original.latencyMs / 1000).toFixed(1)}s` : "—" },
    { accessorKey: "tokens", header: "Tokens", meta: { label: "Tokens" }, cell: ({ row }) => row.original.tokens || "—" },
    { accessorKey: "runStatus", header: "Run status", meta: { label: "Run status" }, cell: ({ row }) => row.original.run ? <Badge variant={row.original.runStatus === "Succeeded" ? "secondary" : row.original.runStatus === "Failed" ? "destructive" : "outline"}>{row.original.runStatus}</Badge> : "—" },
    { accessorKey: "trace", header: "Trace", meta: { label: "Trace" }, cell: ({ row }) => <code>{row.original.trace.slice(0, 12) || "—"}</code> },
    {
      id: "action", header: "Draft", meta: { label: "Draft action" }, enableSorting: false,
      cell: ({ row }) => {
        const item = row.original.draft;
        const isSelected = item.id === draft?.id;
        return <Button size="sm" variant={isSelected ? "secondary" : "ghost"} disabled={isSelected} onClick={() => setDraftId(item.id)}>{isSelected ? "Viewing" : "View"}</Button>;
      },
    },
  ];
  const logout = async () => {
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
        <CatalogOverview catalogs={catalogs} onCreate={() => setDialog("catalog")} onOpen={(id) => { setSelectedId(""); setQuery(""); setCatalogId(id); }} />
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
    return <AppShell user={currentUser} onNavigate={() => { setCatalogId(""); setSelectedId(""); }} onLogout={logout}>
      <div className="admin-surface">
        {error && <Alert className="m-6 border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}
        <CatalogDetail catalog={activeCatalog} products={products} query={query} onQueryChange={setQuery} onBack={() => setCatalogId("")} onOpenProduct={setSelectedId} onAddProduct={() => setDialog("product")} />
        <ProductSheet open={dialog === "product"} catalogName={activeCatalog.name} form={productForm} busy={busy} error={error} addAnother={addAnotherProduct} onOpenChange={(open) => { if (!open && !busy) setDialog(null); }} onFormChange={setProductForm} onAddAnotherChange={setAddAnotherProduct} onSubmit={createProduct} />
      </div>
    </AppShell>;

  return (
    <div className="app-shell dashboard-shell">
      <header className="dashboard-header">
        <Button
          variant="ghost"
          size="icon"
          className="sidebar-trigger"
          aria-label={sidebarOpen ? "Close navigation" : "Open navigation"}
          onClick={() => setSidebarOpen((open) => !open)}
        >
          {sidebarOpen ? <X /> : <Menu />}
        </Button>
        <div className="brand-mark">PI</div>
        <div>
          <p className="eyebrow">PIM workspace</p>
          <h1>Product Content Operations</h1>
        </div>
        <div className="account-menu">
          <span>
            <strong>{currentUser.displayName}</strong>
            <small>{currentUser.email}</small>
          </span>
          <Button variant="outline" size="sm" onClick={logout}>
            Sign out
          </Button>
        </div>
      </header>
      <main className="dashboard-layout">
        {sidebarOpen && (
          <button
            className="dashboard-overlay"
            aria-label="Close navigation"
            onClick={() => setSidebarOpen(false)}
          />
        )}
        <aside
          className={
            sidebarOpen ? "product-rail sidebar-open" : "product-rail"
          }
        >
          <div className="rail-title">
            <span>Catalogs</span>
            <Button
              variant="ghost"
              size="sm"
              className="add-link"
              onClick={() => setDialog("catalog")}
            >
              + New
            </Button>
          </div>
          <div className="catalog-list">
            {catalogs.map((catalog) => (
              <Button
                variant="ghost"
                key={catalog.id}
                className={
                  catalog.id === catalogId ? "catalog active" : "catalog"
                }
                onClick={() => {
                  setCatalogId(catalog.id);
                  setSidebarOpen(false);
                }}
              >
                <span>{catalog.name}</span>
                <strong>{catalog.productCount}</strong>
                <small>{catalog.description}</small>
              </Button>
            ))}
          </div>
          {activeCatalog && (
            <section className="catalog-product-scope">
              <div className="catalog-scope-head">
                <div>
                  <span>Active catalog</span>
                  <strong>{activeCatalog.name}</strong>
                </div>
                <Badge variant="secondary">
                  {activeCatalog.productCount} products
                </Badge>
                <p>{activeCatalog.description}</p>
              </div>
              <div className="rail-title products-title">
                <span>Products in catalog</span>
                <Button
                  variant="ghost"
                  size="sm"
                  className="add-link"
                  onClick={() => setDialog("product")}
                >
                  + Add
                </Button>
              </div>
              <div className="search">
                ⌕{" "}
                <Input
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder={`Search ${activeCatalog.name}`}
                  aria-label={`Search products in ${activeCatalog.name}`}
                />
              </div>
              <div className="product-tree">
                <div className="product-list">
                  {filteredProducts.map((product) => (
                    <Button
                      variant="ghost"
                      className={
                        product.id === selectedId ? "product active" : "product"
                      }
                      onClick={() => {
                        setSelectedId(product.id);
                        setSidebarOpen(false);
                      }}
                      key={product.id}
                    >
                      <span className="product-avatar">
                        {product.name.slice(0, 2).toUpperCase()}
                      </span>
                      <span>
                        <strong>{product.name}</strong>
                        <small>
                          {product.sku} · {product.brand}
                        </small>
                      </span>
                    </Button>
                  ))}
                  {filteredProducts.length === 0 && (
                    <p className="no-results">No products in this catalog</p>
                  )}
                </div>
              </div>
            </section>
          )}
        </aside>
        <section className="workspace dashboard-content">
          {error && (
            <Alert className="error-banner">
              <AlertDescription>{error}</AlertDescription>
              <Button
                variant="ghost"
                size="icon"
                aria-label="Dismiss error"
                onClick={() => setError("")}
              >
                ×
              </Button>
            </Alert>
          )}
          {!selected ? (
            <div className="empty">
              <h2>
                {activeCatalog
                  ? `${activeCatalog.name} is empty`
                  : "Create your first catalog"}
              </h2>
              <p>
                {activeCatalog
                  ? "Add a product with facts that AI can turn into review-ready product information."
                  : "Catalogs keep related products organized."}
              </p>
              {activeCatalog && (
                <Button
                  className="empty-action"
                  onClick={() => setDialog("product")}
                >
                  Add product
                </Button>
              )}
            </div>
          ) : (
            <>
              <div className="workspace-head">
                <div>
                  <nav className="entity-breadcrumb" aria-label="Product location">
                    <span>Catalogs</span>
                    <b>›</b>
                    <span>{activeCatalog?.name ?? "Uncatalogued"}</span>
                    <b>›</b>
                    <strong>{selected.name}</strong>
                  </nav>
                  <p className="eyebrow">Product record</p>
                  <h2>{selected.name}</h2>
                  <div className="product-record-meta">
                    <span><small>SKU</small>{selected.sku}</span>
                    <span><small>Brand</small>{selected.brand}</span>
                    <span><small>Catalog</small>{activeCatalog?.name ?? "Uncatalogued"}</span>
                    <span><small>Facts</small>{selected.attributes.length}</span>
                    <span><small>Updated</small>{new Date(selected.updatedAt).toLocaleDateString()}</span>
                  </div>
                </div>
                <Button type="button" variant="outline" onClick={openSourceFacts}>
                  <Pencil aria-hidden="true" />
                  Edit source facts
                </Button>
              </div>
              <div className="draft-toolbar">
                <div className="draft-toolbar-summary">
                  <History aria-hidden="true" />
                  <div>
                    <strong>Draft history</strong>
                    <span>
                      {languageDrafts.length === 0
                        ? `No ${language === "de" ? "German" : "English"} versions`
                        : `${languageDrafts.length} ${
                            language === "de" ? "German" : "English"
                          } ${
                            languageDrafts.length === 1 ? "version" : "versions"
                          }`}
                    </span>
                  </div>
                </div>
                <div className="draft-toolbar-actions">
                  <NativeSelect
                    value={language}
                    onChange={(e) => setLanguage(e.target.value as "de" | "en")}
                    aria-label="Draft language"
                  >
                    <option value="de">Deutsch</option>
                    <option value="en">English</option>
                  </NativeSelect>
                  <Button onClick={openGenerate} disabled={busy}>
                    <Sparkles aria-hidden="true" />
                    {busy ? "Generating…" : "New draft"}
                  </Button>
                </div>
              </div>
              {!draft ? (
                <div className="empty draft-empty">
                  <span>✦</span>
                  <h3>
                    No {language === "de" ? "German" : "English"} draft yet
                  </h3>
                  <p>
                    Generate a {language === "de" ? "German" : "English"}{" "}
                    description. Approval applies only to this language.
                  </p>
                </div>
              ) : (
                <>
                  <div className="content-stack">
                    <Collapsible className="facts-card">
                      <CollapsibleTrigger asChild>
                        <Button
                          variant="ghost"
                          className="facts-trigger"
                          type="button"
                        >
                          <span>
                            <strong>Source facts</strong>
                            <small>Grounding used to generate this draft</small>
                          </span>
                          <span className="fact-count">
                            {selected.attributes.length + 3} facts
                          </span>
                          <ChevronDown className="facts-chevron" aria-hidden="true" />
                        </Button>
                      </CollapsibleTrigger>
                      <CollapsibleContent className="facts-content">
                        <dl>
                          <dt>Product</dt>
                          <dd>{selected.name}</dd>
                          <dt>Brand</dt>
                          <dd>{selected.brand}</dd>
                          <dt>SKU</dt>
                          <dd>{selected.sku}</dd>
                          {selected.attributes.map((a) => (
                            <React.Fragment key={a.name}>
                              <dt>{a.name}</dt>
                              <dd>
                                {a.value}
                                {a.unit ? ` ${a.unit}` : ""}
                              </dd>
                            </React.Fragment>
                          ))}
                        </dl>
                        <div className="trust-note">
                          Only these supplied facts may inform generated content.
                        </div>
                      </CollapsibleContent>
                    </Collapsible>
                    <Card
                      className={
                        editingDraft ? "draft-card editing" : "draft-card"
                      }
                    >
                      <div className="draft-card-header">
                        <div className="draft-card-heading">
                          <span>AI draft</span>
                          <Badge className={statusClass(draft.status)}>
                            {statusName(draft.status)}
                          </Badge>
                          {draft.isCurrent && (
                            <Badge variant="secondary">Current</Badge>
                          )}
                        </div>
                        {pending && !editingDraft && (
                          <div className="draft-header-actions">
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={beginEditDraft}
                              disabled={busy}
                            >
                              <Pencil />
                              Edit
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() => review("reject")}
                              disabled={busy}
                            >
                              <X />
                              Reject
                            </Button>
                            <Button
                              size="sm"
                              onClick={() => review("approve")}
                              disabled={busy}
                            >
                              <Check />
                              Approve
                            </Button>
                          </div>
                        )}
                      </div>
                      <p className="language">
                        {draft.language === "de" ? "German" : "English"} content
                      </p>
                      {editingDraft ? (
                        <div className="draft-editor">
                          <Label className="form-field">
                            <span>Title</span>
                            <Input
                              maxLength={200}
                              value={draftForm.title}
                              onChange={(e) =>
                                setDraftForm((x) => ({
                                  ...x,
                                  title: e.target.value,
                                }))
                              }
                            />
                          </Label>
                          <Label className="form-field">
                            <span>Short description</span>
                            <Textarea
                              maxLength={500}
                              rows={3}
                              value={draftForm.shortDescription}
                              onChange={(e) =>
                                setDraftForm((x) => ({
                                  ...x,
                                  shortDescription: e.target.value,
                                }))
                              }
                            />
                          </Label>
                          <Label className="form-field">
                            <span>Long description</span>
                            <Textarea
                              maxLength={8000}
                              rows={7}
                              value={draftForm.longDescription}
                              onChange={(e) =>
                                setDraftForm((x) => ({
                                  ...x,
                                  longDescription: e.target.value,
                                }))
                              }
                            />
                          </Label>
                          <Label className="form-field">
                            <span>
                              Highlights <small>3–5, one per line</small>
                            </span>
                            <Textarea
                              rows={5}
                              value={draftForm.highlights}
                              onChange={(e) =>
                                setDraftForm((x) => ({
                                  ...x,
                                  highlights: e.target.value,
                                }))
                              }
                            />
                          </Label>
                          <div className="editor-actions">
                            <Button
                              variant="outline"
                              onClick={() => setEditingDraft(false)}
                              disabled={busy}
                            >
                              Cancel
                            </Button>
                            <Button onClick={saveDraft} disabled={busy}>
                              {busy ? "Saving…" : "Save changes"}
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <>
                          <h3>{draft.title}</h3>
                          <p className="short">{draft.shortDescription}</p>
                          <p>{draft.longDescription}</p>
                          <ul>
                            {draft.highlights.map((x) => (
                              <li key={x}>{x}</li>
                            ))}
                          </ul>
                        </>
                      )}
                    </Card>
                  </div>
                  <Tabs defaultValue="history" className="draft-data-tabs">
                    <TabsList>
                      <TabsTrigger value="history">
                        Draft history
                        <Badge variant="secondary">{languageDrafts.length}</Badge>
                      </TabsTrigger>
                      <TabsTrigger value="audit">Model audit</TabsTrigger>
                    </TabsList>
                    <TabsContent value="history">
                      <DataTable
                        columns={draftHistoryColumns}
                        data={draftHistoryData}
                        getRowId={(row) => row.draft.id}
                        renderSelectedActions={renderDraftSelectionActions}
                        searchPlaceholder="Filter draft history…"
                        emptyMessage="No draft versions found."
                      />
                    </TabsContent>
                    <TabsContent value="audit">
                      <DataTable
                        columns={modelAuditColumns}
                        data={modelAuditData}
                        getRowId={(row) => row.draft.id}
                        renderSelectedActions={renderDraftSelectionActions}
                        searchPlaceholder="Filter model runs…"
                        emptyMessage="No model runs found."
                      />
                    </TabsContent>
                  </Tabs>
                </>
              )}
            </>
          )}
        </section>
      </main>
      <Dialog
        open={dialog !== null}
        onOpenChange={(open) => !open && !busy && setDialog(null)}
      >
        <DialogContent className="max-h-[calc(100dvh-2rem)] max-w-2xl overflow-y-auto">
            {dialog === "catalog" ? (
              <form className="grid gap-4" onSubmit={createCatalog}>
                <DialogHeader>
                  <p className="eyebrow">Organization</p>
                  <DialogTitle>Create catalog</DialogTitle>
                  <DialogDescription>
                    Group related products into a focused collection.
                  </DialogDescription>
                </DialogHeader>
                <Label className="form-field">
                  <span>Name</span>
                  <Input
                    required
                    maxLength={120}
                    value={catalogForm.name}
                    onChange={(e) =>
                      setCatalogForm((x) => ({ ...x, name: e.target.value }))
                    }
                    placeholder="e.g. Outdoor Essentials"
                  />
                </Label>
                <Label className="form-field">
                  <span>Description</span>
                  <Textarea
                    maxLength={500}
                    value={catalogForm.description}
                    onChange={(e) =>
                      setCatalogForm((x) => ({
                        ...x,
                        description: e.target.value,
                      }))
                    }
                    placeholder="What belongs in this catalog?"
                  />
                </Label>
                <DialogFooter>
                  <Button type="button" variant="outline" onClick={() => setDialog(null)} disabled={busy}>
                    Cancel
                  </Button>
                  <Button disabled={busy}>
                    {busy ? "Creating…" : "Create catalog"}
                  </Button>
                </DialogFooter>
              </form>
            ) : dialog === "product" ? (
              <form className="grid gap-4" onSubmit={createProduct}>
                <DialogHeader>
                  <p className="eyebrow">{activeCatalog?.name}</p>
                  <DialogTitle>Add product</DialogTitle>
                  <DialogDescription>
                    Add trustworthy facts before generating product content.
                  </DialogDescription>
                </DialogHeader>
                <div className="grid gap-3 sm:grid-cols-2">
                  <Label className="form-field">
                    <span>SKU</span>
                    <Input
                      required
                      value={productForm.sku}
                      onChange={(e) =>
                        setProductForm((x) => ({ ...x, sku: e.target.value }))
                      }
                      placeholder="OUT-BTL-750"
                    />
                  </Label>
                  <Label className="form-field">
                    <span>Brand</span>
                    <Input
                      required
                      value={productForm.brand}
                      onChange={(e) =>
                        setProductForm((x) => ({ ...x, brand: e.target.value }))
                      }
                      placeholder="Northwind"
                    />
                  </Label>
                </div>
                <Label className="form-field">
                  <span>Product name</span>
                  <Input
                    required
                    value={productForm.name}
                    onChange={(e) =>
                      setProductForm((x) => ({ ...x, name: e.target.value }))
                    }
                    placeholder="Alpine Steel Bottle"
                  />
                </Label>
                <Label className="form-field">
                  <span>
                    Product facts <small>one per line, Name: Value</small>
                  </span>
                  <Textarea
                    rows={5}
                    value={productForm.attributes}
                    onChange={(e) =>
                      setProductForm((x) => ({
                        ...x,
                        attributes: e.target.value,
                      }))
                    }
                    placeholder={"Volume: 750 ml\nMaterial: Stainless steel"}
                  />
                </Label>
                <DialogFooter>
                  <Button type="button" variant="outline" onClick={() => setDialog(null)} disabled={busy}>
                    Cancel
                  </Button>
                  <Button disabled={busy}>
                    {busy ? "Adding…" : "Add product"}
                  </Button>
                </DialogFooter>
              </form>
            ) : dialog === "facts" ? (
              <form className="grid gap-4" onSubmit={updateSourceFacts}>
                <DialogHeader>
                  <p className="eyebrow">{selected?.sku}</p>
                  <DialogTitle>Edit source facts</DialogTitle>
                  <DialogDescription>
                    Maintain the verified product data used by facts-only and RAG generation.
                  </DialogDescription>
                </DialogHeader>
                <div className="grid gap-3 sm:grid-cols-2">
                  <Label className="form-field">
                    <span>Product name</span>
                    <Input required maxLength={200} value={sourceFactsForm.name} onChange={(event) => setSourceFactsForm((value) => ({ ...value, name: event.target.value }))} />
                  </Label>
                  <Label className="form-field">
                    <span>Brand</span>
                    <Input required maxLength={100} value={sourceFactsForm.brand} onChange={(event) => setSourceFactsForm((value) => ({ ...value, brand: event.target.value }))} />
                  </Label>
                </div>
                <Label className="form-field">
                  <span>Source facts <small>one per line: Name: Value | Unit</small></span>
                  <Textarea rows={9} value={sourceFactsForm.attributes} onChange={(event) => setSourceFactsForm((value) => ({ ...value, attributes: event.target.value }))} placeholder={"Capacity: 800 | ml\nMaterial: Borosilicate glass\nPower: 150 | W"} />
                </Label>
                <p className="text-sm text-muted-foreground">
                  Add or change a line to update a fact. Remove a line to delete that fact. The unit after <code>|</code> is optional.
                </p>
                <DialogFooter>
                  <Button type="button" variant="outline" onClick={() => setDialog(null)} disabled={busy}>Cancel</Button>
                  <Button disabled={busy}>{busy ? "Saving…" : "Save source facts"}</Button>
                </DialogFooter>
              </form>
            ) : dialog === "generate" ? (
              <div className="grid gap-5">
                <DialogHeader>
                  <p className="eyebrow">{selected?.sku} · {language.toUpperCase()}</p>
                  <DialogTitle>Generate product draft</DialogTitle>
                  <DialogDescription>
                    Choose whether Ollama uses only verified product facts or also retrieves product-scoped knowledge.
                  </DialogDescription>
                </DialogHeader>
                <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Grounding mode">
                  <Label className={!useRag ? "flex cursor-pointer items-start gap-3 rounded-lg border border-primary bg-primary/5 p-4 ring-1 ring-primary" : "flex cursor-pointer items-start gap-3 rounded-lg border bg-background p-4"}>
                    <input className="mt-0.5 size-4 accent-primary" type="radio" name="grounding" checked={!useRag} onChange={() => setUseRag(false)} />
                    <span className="grid gap-1"><strong>Facts only</strong><small className="font-normal text-muted-foreground">Use the structured product record. Fast and predictable.</small></span>
                  </Label>
                  <Label className={useRag ? "flex cursor-pointer items-start gap-3 rounded-lg border border-primary bg-primary/5 p-4 ring-1 ring-primary" : "flex cursor-pointer items-start gap-3 rounded-lg border bg-background p-4"}>
                    <input className="mt-0.5 size-4 accent-primary" type="radio" name="grounding" checked={useRag} onChange={() => setUseRag(true)} />
                    <span className="grid gap-1"><strong>Facts + knowledge (RAG)</strong><small className="font-normal text-muted-foreground">Retrieve relevant excerpts from this product’s uploaded documents.</small></span>
                  </Label>
                </div>
                {useRag && (
                  <Label className="grid gap-2 rounded-lg border bg-muted/50 p-3">
                    <span className="flex items-center justify-between gap-3"><strong>Top-K chunks</strong><small className="font-normal text-muted-foreground">1–20 · default 5</small></span>
                    <Input type="number" min={1} max={20} step={1} value={topK} onChange={(event) => setTopK(Math.min(20, Math.max(1, Number(event.target.value) || 1)))} />
                    <small className="font-normal text-muted-foreground">
                      Top-{topK} means Qdrant will add at most the {topK} most relevant {topK === 1 ? "chunk" : "chunks"} to the model prompt. If fewer matches exist, it returns fewer. Higher values increase context, tokens, and latency and may improve coverage, but too many chunks can add noise. Lower values are faster but may miss useful passages.
                    </small>
                  </Label>
                )}
                <div className="grid gap-3 rounded-lg border bg-card p-4 shadow-sm">
                  <div className="flex items-center justify-between gap-3">
                    <div className="grid gap-1"><strong>Product knowledge</strong><small className="font-normal text-muted-foreground">{knowledgeDocuments.length} uploaded document{knowledgeDocuments.length === 1 ? "" : "s"}</small></div>
                    <Badge variant="outline">Product scoped</Badge>
                  </div>
                  {knowledgeDocuments.length > 0 && (
                    <div className="grid max-h-32 gap-2 overflow-y-auto">
                      {knowledgeDocuments.map((document) => (
                        <div className="flex items-center gap-3 rounded-md border bg-muted/50 p-2" key={document.id}>
                          <span className="min-w-0 flex-1 truncate">{document.fileName}</span>
                          <small className="whitespace-nowrap font-normal text-muted-foreground">{document.chunkCount} chunks</small>
                          <Button type="button" size="icon" variant="ghost" disabled={busy} aria-label={`Remove ${document.fileName}`} onClick={() => deleteKnowledge(document)}><Trash2 aria-hidden="true" /></Button>
                        </div>
                      ))}
                    </div>
                  )}
                  <div className="grid gap-3 border-t pt-3">
                    <Label className="form-field">
                      <span>Upload product instructions</span>
                      <Input type="file" accept=".txt,.md,.pdf" onChange={(event) => setKnowledgeFile(event.target.files?.[0] ?? null)} />
                    </Label>
                    <Button type="button" variant="outline" disabled={busy || !knowledgeFile} onClick={uploadKnowledge}><Upload aria-hidden="true" />{busy ? "Processing…" : "Upload and index"}</Button>
                  </div>
                </div>
                <DialogFooter>
                  <Button type="button" variant="outline" onClick={() => setDialog(null)} disabled={busy}>Cancel</Button>
                  <Button type="button" disabled={busy || (useRag && knowledgeDocuments.length === 0)} onClick={generate}><Sparkles aria-hidden="true" />{busy ? "Generating…" : useRag ? "Generate with RAG" : "Generate from facts"}</Button>
                </DialogFooter>
              </div>
            ) : null}
        </DialogContent>
      </Dialog>
    </div>
  );
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
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </Label>
          <Label className="form-field">
            <span>Password</span>
            <Input
              type="password"
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
  const [catalogId, setCatalogId] = useState("");
  const [products, setProducts] = useState<StorefrontProduct[]>([]);
  const [language, setLanguage] = useState<"de" | "en">("de");
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
  return (
    <div className="storefront">
      <header>
        <div className="brand-mark">PI</div>
        <div>
          <p className="eyebrow">{text.catalog}</p>
          <h1>{text.collections}</h1>
        </div>
        <div className="language-switch" aria-label="Content language">
          <Button
            variant="ghost"
            className={language === "de" ? "active" : ""}
            onClick={() => setLanguage("de")}
          >
            DE
          </Button>
          <Button
            variant="ghost"
            className={language === "en" ? "active" : ""}
            onClick={() => setLanguage("en")}
          >
            EN
          </Button>
        </div>
        <div className="account-menu">
          <span>
            <strong>{user.displayName}</strong>
            <small>{text.viewer}</small>
          </span>
          <Button variant="outline" size="sm" onClick={onLogout}>
            {text.signout}
          </Button>
        </div>
      </header>
      <main>
        <nav className="storefront-tabs" aria-label="Catalogs">
          {catalogs.map((x) => (
            <Button
              variant="ghost"
              key={x.id}
              className={x.id === catalogId ? "active" : ""}
              onClick={() => setCatalogId(x.id)}
            >
              {x.name}
              <small>
                {x.productCount} {text.products}
              </small>
            </Button>
          ))}
        </nav>
        <section className="storefront-content">
          {error && (
            <Alert className="error-banner">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <div className="collection-head">
            <p className="eyebrow">
              {text.curated} · {language.toUpperCase()}
            </p>
            <h2>{active?.name}</h2>
            <p>{active?.description}</p>
          </div>
          <div className="storefront-grid">
            {products.map((product) => (
              <Card className="store-product" key={product.id}>
                <div className="store-product-art">
                  {product.name.slice(0, 2).toUpperCase()}
                </div>
                <p className="eyebrow">
                  {product.brand} · {product.sku}
                </p>
                <h3>{product.approvedContent?.title ?? product.name}</h3>
                <p>
                  {product.approvedContent?.shortDescription ?? text.waiting}
                </p>
                {product.approvedContent ? (
                  <ul>
                    {product.approvedContent.highlights.map((x) => (
                      <li key={x}>{x}</li>
                    ))}
                  </ul>
                ) : (
                  <span className="awaiting">{text.badge}</span>
                )}
                <details>
                  <summary>{text.facts}</summary>
                  <dl>
                    {product.attributes.map((a) => (
                      <React.Fragment key={a.name}>
                        <dt>{a.name}</dt>
                        <dd>
                          {a.value}
                          {a.unit ? ` ${a.unit}` : ""}
                        </dd>
                      </React.Fragment>
                    ))}
                  </dl>
                </details>
              </Card>
            ))}
          </div>
        </section>
      </main>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><App /><Toaster richColors position="top-right" /></React.StrictMode>);
