export type DiffPart = { kind: "same" | "added" | "removed"; text: string };

// Bounded token LCS preserves whitespace, numbers and multilingual words. Large
// fields fall back to whole-field replacement rather than allocating an unbounded matrix.
export function diffText(before: string, after: string): DiffPart[] {
  if (before === after) return before ? [{ kind: "same", text: before }] : [];
  const tokenize = (value: string) => value.match(/\s+|[\p{L}\p{N}]+|[^\s\p{L}\p{N}]/gu) ?? [];
  const left = tokenize(before), right = tokenize(after);
  if (left.length > 1200 || right.length > 1200) return [
    ...(before ? [{ kind: "removed" as const, text: before }] : []),
    ...(after ? [{ kind: "added" as const, text: after }] : []),
  ];
  const width = right.length + 1;
  const matrix = new Uint16Array((left.length + 1) * width);
  for (let i = left.length - 1; i >= 0; i--) for (let j = right.length - 1; j >= 0; j--)
    matrix[i * width + j] = left[i] === right[j] ? matrix[(i + 1) * width + j + 1] + 1 : Math.max(matrix[(i + 1) * width + j], matrix[i * width + j + 1]);
  const parts: DiffPart[] = [];
  function push(kind: DiffPart["kind"], text: string) {
    const previous = parts[parts.length - 1];
    if (previous?.kind === kind) previous.text += text; else parts.push({ kind, text });
  }
  let i = 0, j = 0;
  while (i < left.length || j < right.length) {
    if (i < left.length && j < right.length && left[i] === right[j]) { push("same", left[i++]); j++; }
    else if (i < left.length && (j === right.length || matrix[(i + 1) * width + j] >= matrix[i * width + j + 1])) push("removed", left[i++]);
    else push("added", right[j++]);
  }
  return parts;
}
