import test from "node:test";
import assert from "node:assert/strict";
import { diffText } from "../src/lib/content-diff.ts";

function reconstruct(parts, omit) { return parts.filter(part => part.kind !== omit).map(part => part.text).join(""); }

test("unchanged text stays intact", () => {
  assert.deepEqual(diffText("Same description", "Same description"), [{ kind: "same", text: "Same description" }]);
});
test("numeric and multilingual edits are visible and both versions reconstruct exactly", () => {
  const before = "Kapazität: 750 ml.\nGehäuse aus Edelstahl.";
  const after = "Kapazität: 1000 ml.\nGehäuse aus Stahl.";
  const parts = diffText(before, after);
  assert.equal(reconstruct(parts, "added"), before);
  assert.equal(reconstruct(parts, "removed"), after);
  assert.ok(parts.some(part => part.kind === "removed" && part.text.includes("750")));
  assert.ok(parts.some(part => part.kind === "added" && part.text.includes("1000")));
});
test("inserted and removed highlights preserve order and whitespace", () => {
  const before = "Steel\n750 ml\nHand wash";
  const after = "Steel\n1000 ml\nStore dry";
  const parts = diffText(before, after);
  assert.equal(reconstruct(parts, "added"), before);
  assert.equal(reconstruct(parts, "removed"), after);
});
test("empty evidence-based highlights are a valid comparison", () => {
  assert.deepEqual(diffText("", ""), []);
  assert.deepEqual(diffText("Old feature", ""), [{ kind: "removed", text: "Old feature" }]);
  assert.deepEqual(diffText("", "New fact"), [{ kind: "added", text: "New fact" }]);
});
test("large content falls back without losing either version", () => {
  const before = "word ".repeat(1500), after = "changed ".repeat(1500);
  const parts = diffText(before, after);
  assert.deepEqual(parts.map(part => part.kind), ["removed", "added"]);
  assert.equal(reconstruct(parts, "added"), before);
  assert.equal(reconstruct(parts, "removed"), after);
});
test("HTML-like source text is retained as text for React to escape", () => {
  const before = "<script>old()</script>", after = "<script>new()</script>";
  const parts = diffText(before, after);
  assert.equal(reconstruct(parts, "added"), before);
  assert.equal(reconstruct(parts, "removed"), after);
});
