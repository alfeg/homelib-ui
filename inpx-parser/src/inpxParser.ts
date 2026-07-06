import { unzipSync } from "fflate";

const ROW_DELIMITER = "\x04";
const GENRE_DELIMITER = ":";
const utf8Decoder = new TextDecoder("utf-8");

let cp1251Decoder: TextDecoder | null = null;
try {
  cp1251Decoder = new TextDecoder("windows-1251");
} catch {
  cp1251Decoder = null;
}

export interface ParsedBook {
  id: number;
  title: string;
  genre: string;
  genreCodes: string[];
  authors: string;
  series: string;
  seriesNo: string;
  lang: string;
  file: string;
  ext: string;
  archiveFile: string;
  date: string;
}

export interface ParsedMetadata {
  description: string;
  version: string;
  totalBooks: number;
}

export interface ParseResult {
  metadata: ParsedMetadata;
  books: ParsedBook[];
  datasetSignature: string;
}

export interface StreamingParseResult {
  metadata: ParsedMetadata;
  datasetSignature: string;
  totalBooks: number;
  totalBatches: number;
}

export type ParseProgressCallback = (
  phase: string,
  processed: number,
  total: number,
  percent: number,
) => void;
export type ParseBooksBatchCallback = (
  books: ParsedBook[],
) => Promise<void> | void;

function isPromiseLike(value: unknown): value is Promise<unknown> {
  return !!value && typeof (value as { then?: unknown }).then === "function";
}

function decodeText(bytes: Uint8Array): string {
  if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0) return "";

  // Encoding is uniform within a file, so a prefix sample is enough to
  // detect CP1251 mojibake without regex-scanning multi-MB entries.
  const MOJIBAKE_SAMPLE_LENGTH = 65536;
  const looksLikeMojibake = (text: string) => {
    if (!text) return false;
    const sample =
      text.length > MOJIBAKE_SAMPLE_LENGTH
        ? text.slice(0, MOJIBAKE_SAMPLE_LENGTH)
        : text;
    const artifacts = sample.match(/[РС][\u0080-\u00bf]/g);
    if (!artifacts || artifacts.length === 0) return false;
    return artifacts.length / sample.length > 0.01;
  };

  try {
    const utf8Text = utf8Decoder.decode(bytes);
    if (!looksLikeMojibake(utf8Text)) return utf8Text;
  } catch {
    /* ignored */
  }

  if (cp1251Decoder) {
    try {
      return cp1251Decoder.decode(bytes);
    } catch {
      /* ignored */
    }
  }

  try {
    return utf8Decoder.decode(bytes);
  } catch {
    return "";
  }
}

function normalizeAuthors(rawAuthors: string): string {
  if (!rawAuthors) return "";
  return rawAuthors
    .split(":")
    .map((a) => a.trim())
    .filter(Boolean)
    .map((a) => {
      const [surname = "", firstName = "", middleName = ""] = a
        .split(",")
        .map((p) => p.trim());
      return [firstName, middleName, surname].filter(Boolean).join(" ");
    })
    .filter(Boolean)
    .join(", ");
}

function normalizeString(value: unknown): string {
  return typeof value === "string" ? value : String(value ?? "");
}

function normalizeId(value: unknown, fallbackId: number): number {
  const parsed = Number.parseInt(normalizeString(value), 10);
  return Number.isFinite(parsed) ? parsed : fallbackId;
}

function normalizeGenreCodes(rawGenre: string): string[] {
  return normalizeString(rawGenre)
    .split(GENRE_DELIMITER)
    .map((c) => normalizeString(c).trim())
    .filter(Boolean);
}

function mapBook(
  fields: string[],
  archiveFile: string,
  fallbackId: number,
): ParsedBook {
  const rawGenre = normalizeString(fields[1]);
  return {
    id: normalizeId(fields[7], fallbackId),
    title: normalizeString(fields[2]),
    genre: rawGenre,
    genreCodes: normalizeGenreCodes(rawGenre),
    authors: normalizeAuthors(normalizeString(fields[0])),
    series: normalizeString(fields[3]),
    seriesNo: normalizeString(fields[4]),
    lang: normalizeString(fields[11]),
    file: normalizeString(fields[5]),
    ext: normalizeString(fields[9]),
    archiveFile: normalizeString(archiveFile),
    date: normalizeString(fields[10]),
  };
}

function toArchiveFile(entryName: string): string {
  const fileName = entryName.split("/").pop() ?? entryName;
  return fileName.replace(/\.inp$/i, ".zip");
}

function updateSigHash(hash: number, value: string): number {
  let h = hash >>> 0;
  for (let i = 0; i < value.length; i++) {
    h ^= value.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

// Hashes the same character sequence as the previous
// `${id}|${title}|...` template literal (FNV-1a is byte-sequential), so
// signatures stay stable across versions — without allocating a per-book
// concatenated string.
function updateSigHashBook(hash: number, book: ParsedBook): number {
  let h = updateSigHash(hash, String(book.id));
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.title);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.authors);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.series);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.lang);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.genre);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.file);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.archiveFile);
  h = updateSigHash(h, "|");
  h = updateSigHash(h, book.ext);
  return h;
}

// Byte-level equivalent of splitting on \r?\n + \x04 and checking that
// field[5] is non-empty. \n, \r and \x04 are single bytes in both UTF-8 and
// CP1251, so this needs no text decoding and allocates nothing.
function countValidBooksInBytes(bytes: Uint8Array): number {
  let count = 0;
  let delimiters = 0;
  let lineHasContent = false;
  let fileFieldHasContent = false;

  for (let i = 0; i < bytes.length; i++) {
    const b = bytes[i];
    if (b === 0x0a) {
      if (lineHasContent && fileFieldHasContent) count++;
      delimiters = 0;
      lineHasContent = false;
      fileFieldHasContent = false;
    } else if (b !== 0x0d) {
      lineHasContent = true;
      if (b === 0x04) delimiters++;
      else if (delimiters === 5) fileFieldHasContent = true;
    }
  }
  if (lineHasContent && fileFieldHasContent) count++;
  return count;
}

export function parseInpxBuffer(
  buffer: ArrayBuffer,
  onProgress?: ParseProgressCallback,
): ParseResult {
  const archive = unzipSync(new Uint8Array(buffer));
  const entryNames = Object.keys(archive);
  const metadata: ParsedMetadata = {
    description: "",
    version: "",
    totalBooks: 0,
  };
  const inpEntries = entryNames.filter((n) => n.toLowerCase().endsWith(".inp"));
  const totalEntries = inpEntries.length;

  if (archive["collection.info"]) {
    metadata.description = decodeText(archive["collection.info"]).trim();
  }
  if (archive["version.info"]) {
    metadata.version = decodeText(archive["version.info"]).trim();
  }

  let sigHash = updateSigHash(
    2166136261,
    `${metadata.description}|${metadata.version}`,
  );

  onProgress?.("parsing", 0, totalEntries, totalEntries ? 0 : 100);

  const books: ParsedBook[] = [];
  let fallbackId = 1;

  for (let ei = 0; ei < inpEntries.length; ei++) {
    const entryName = inpEntries[ei];
    const text = decodeText(archive[entryName]);
    const archiveFile = toArchiveFile(entryName);
    const lines = text.split(/\r?\n/);

    for (const line of lines) {
      if (!line) continue;
      const fields = line.split(ROW_DELIMITER);
      if (!fields.length || !fields[5]) continue;

      const book = mapBook(fields, archiveFile, fallbackId++);
      books.push(book);
      sigHash = updateSigHashBook(sigHash, book);
    }

    const pct = totalEntries
      ? Math.round(((ei + 1) / totalEntries) * 100)
      : 100;
    onProgress?.("parsing", ei + 1, totalEntries, pct);
  }

  metadata.totalBooks = books.length;

  return {
    metadata,
    books,
    datasetSignature: `${books.length}:${sigHash.toString(16)}`,
  };
}

export async function parseInpxBufferStreaming(
  buffer: ArrayBuffer,
  onBooksBatch: ParseBooksBatchCallback,
  onProgress?: ParseProgressCallback,
  batchSize = 1000,
): Promise<StreamingParseResult> {
  const archive = unzipSync(new Uint8Array(buffer));
  const entryNames = Object.keys(archive);
  const metadata: ParsedMetadata = {
    description: "",
    version: "",
    totalBooks: 0,
  };
  const inpEntries = entryNames.filter((n) => n.toLowerCase().endsWith(".inp"));

  if (archive["collection.info"]) {
    metadata.description = decodeText(archive["collection.info"]).trim();
  }
  if (archive["version.info"]) {
    metadata.version = decodeText(archive["version.info"]).trim();
  }

  let sigHash = updateSigHash(
    2166136261,
    `${metadata.description}|${metadata.version}`,
  );

  // Pre-count valid rows so parsing progress can be reported per total books.
  // Runs on raw bytes — no decoding or line splitting on this pass.
  let totalBooksExpected = 0;
  for (let ei = 0; ei < inpEntries.length; ei++) {
    totalBooksExpected += countValidBooksInBytes(archive[inpEntries[ei]]);
  }

  onProgress?.("parsing", 0, totalBooksExpected, totalBooksExpected ? 0 : 100);

  let fallbackId = 1;
  let totalBooks = 0;
  let totalBatches = 0;
  let batch: ParsedBook[] = [];
  let lastReportedPercent = totalBooksExpected ? 0 : 100;
  let lastReportedProcessed = 0;

  const resolvedBatchSize = Number.isFinite(batchSize)
    ? Math.max(1, Math.floor(batchSize))
    : 1000;

  const flushBatch = async () => {
    if (!batch.length) {
      return;
    }

    const chunk = batch;
    batch = [];
    totalBatches++;

    const callbackResult = onBooksBatch(chunk);
    if (isPromiseLike(callbackResult)) {
      await callbackResult;
    }
  };

  for (let ei = 0; ei < inpEntries.length; ei++) {
    const entryName = inpEntries[ei];
    const text = decodeText(archive[entryName]);
    const archiveFile = toArchiveFile(entryName);
    const lines = text.split(/\r?\n/);

    for (const line of lines) {
      if (!line) continue;
      const fields = line.split(ROW_DELIMITER);
      if (!fields.length || !fields[5]) continue;

      const book = mapBook(fields, archiveFile, fallbackId++);
      totalBooks++;
      sigHash = updateSigHashBook(sigHash, book);
      batch.push(book);
      if (batch.length >= resolvedBatchSize) {
        await flushBatch();
      }

      // Report only on whole-percent changes: callers forward progress as
      // postMessage, and per-book calls flood the consumer at library scale.
      const pct = totalBooksExpected
        ? Math.round((totalBooks / totalBooksExpected) * 100)
        : 100;
      if (pct !== lastReportedPercent) {
        lastReportedPercent = pct;
        lastReportedProcessed = totalBooks;
        onProgress?.("parsing", totalBooks, totalBooksExpected, pct);
      }
    }
  }

  await flushBatch();

  // Guarantee a final report with exact totals: percent throttling can stop
  // short of the last rows, and the byte-level pre-count may disagree with
  // the parsed row count on edge cases.
  if (lastReportedPercent !== 100 || lastReportedProcessed !== totalBooks) {
    onProgress?.("parsing", totalBooks, totalBooks, 100);
  }

  metadata.totalBooks = totalBooks;

  return {
    metadata,
    datasetSignature: `${totalBooks}:${sigHash.toString(16)}`,
    totalBooks,
    totalBatches,
  };
}
