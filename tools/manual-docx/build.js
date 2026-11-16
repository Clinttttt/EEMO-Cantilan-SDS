// Builds the printable Word copy of the user manual from its Markdown, so the two never say different things.
// Usage: npm install && node build.js [output.docx]   (default: docs/manual/STALLTRACK_USER_MANUAL.docx)
// Screenshots: put PNGs in docs/manual/screenshots and reference them in the manual as ![Caption](screenshots/name.png).
// shortcut: reads only the Markdown the manual uses (headings, paragraphs, lists, tables, bold, italic, code, links, images);
// extend the parser if the manual starts using more.
const fs = require("fs");
const path = require("path");
const {
  AlignmentType, BorderStyle, Document, Footer, HeadingLevel, ImageRun, LevelFormat, Packer, PageNumber,
  Paragraph, ShadingType, Table, TableCell, TableRow, TextRun, WidthType,
} = require("docx");

const manualDir = path.resolve(__dirname, "../../docs/manual");
const source = path.join(manualDir, "STALLTRACK_USER_MANUAL.md");
const output = path.resolve(process.argv[2] ?? path.join(manualDir, "STALLTRACK_USER_MANUAL.docx"));
const FONT = "Arial";
const CONTENT_WIDTH = 9360; // Letter (short bond) with 1-inch margins, in DXA
const border = { style: BorderStyle.SINGLE, size: 4, color: "B8C2CC" };
const borders = { top: border, bottom: border, left: border, right: border };

// **bold**, *italic*, `code` and [text](link) become runs; a link keeps its text only, because paper cannot follow it.
function runs(text, base = {}) {
  return text.split(/(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`|\[[^\]]+\]\([^)]+\))/).filter(Boolean).map((part) => {
    if (part.startsWith("**")) return new TextRun({ ...base, text: part.slice(2, -2), bold: true });
    if (part.startsWith("`")) return new TextRun({ ...base, text: part.slice(1, -1), font: "Consolas" });
    if (part.startsWith("[")) return new TextRun({ ...base, text: part.slice(1, part.indexOf("]")) });
    if (part.length > 2 && part.startsWith("*")) return new TextRun({ ...base, text: part.slice(1, -1), italics: true });
    return new TextRun({ ...base, text: part });
  });
}

const cells = (line) => line.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());

function table(rows) {
  const [head, ...body] = rows;
  // Each column gets room in proportion to its longest text, never less than a sixth of the page.
  const longest = head.map((_, i) => Math.max(...rows.map((r) => (r[i] ?? "").length)));
  const floor = CONTENT_WIDTH / 6;
  const raw = longest.map((n) => Math.max(n, 1));
  const total = raw.reduce((a, b) => a + b, 0);
  let widths = raw.map((n) => Math.max(floor, Math.round((n / total) * CONTENT_WIDTH)));
  const scale = CONTENT_WIDTH / widths.reduce((a, b) => a + b, 0);
  widths = widths.map((w) => Math.floor(w * scale));
  widths[widths.length - 1] += CONTENT_WIDTH - widths.reduce((a, b) => a + b, 0);

  const row = (values, header) => new TableRow({
    tableHeader: header,
    cantSplit: true,
    children: values.map((value, i) => new TableCell({
      borders,
      width: { size: widths[i], type: WidthType.DXA },
      shading: header ? { fill: "E8EEF4", type: ShadingType.CLEAR, color: "auto" } : undefined,
      margins: { top: 60, bottom: 60, left: 100, right: 100 },
      children: [new Paragraph({ children: runs(value ?? "", { size: 19, bold: header || undefined }) })],
    })),
  });
  return new Table({
    width: { size: CONTENT_WIDTH, type: WidthType.DXA },
    columnWidths: widths,
    rows: [row(head, true), ...body.map((r) => row(r, false))],
  });
}

function image(caption, file) {
  const full = path.resolve(manualDir, file);
  if (!fs.existsSync(full)) throw new Error(`Screenshot not found: ${full}`);
  const data = fs.readFileSync(full);
  if (data.toString("ascii", 1, 4) !== "PNG") throw new Error(`Screenshots must be PNG: ${full}`);
  const [w, h] = [data.readUInt32BE(16), data.readUInt32BE(20)];
  const width = Math.min(w, 624); // 6.5 inches at 96 dpi
  return [
    new Paragraph({ alignment: AlignmentType.CENTER, keepNext: true, spacing: { before: 120 },
      children: [new ImageRun({ type: "png", data, transformation: { width, height: Math.round((h * width) / w) }, altText: { title: caption, description: caption, name: caption } })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 200 }, children: [new TextRun({ text: caption, italics: true, size: 18 })] }),
  ];
}

const lines = fs.readFileSync(source, "utf8").replace(/\r/g, "").split("\n");
const body = [];
let numberedList = 0;
for (let i = 0; i < lines.length; i++) {
  const line = lines[i];
  if (!line.trim()) continue;
  let m;
  if ((m = line.match(/^# (.+)/))) {
    body.push(new Paragraph({ heading: HeadingLevel.TITLE, children: [new TextRun(m[1].replace(/^StallTrack user manual$/, "StallTrack User Manual"))] }));
  } else if ((m = line.match(/^## (.+)/))) {
    body.push(new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(m[1])] }));
  } else if ((m = line.match(/^!\[([^\]]*)\]\(([^)]+)\)/))) {
    body.push(...image(m[1], m[2]));
  } else if (line.startsWith("|")) {
    const rows = [];
    for (; i < lines.length && lines[i].startsWith("|"); i++) if (!/^\|[\s|:-]+\|$/.test(lines[i].trim())) rows.push(cells(lines[i]));
    i--;
    body.push(table(rows), new Paragraph({ children: [] }));
  } else if ((m = line.match(/^- (.+)/))) {
    body.push(new Paragraph({ numbering: { reference: "bullets", level: 0 }, spacing: { after: 80 }, children: runs(m[1]) }));
  } else if ((m = line.match(/^\d+\. (.+)/))) {
    if (!/^\d+\. /.test(lines[i - 1] ?? "")) numberedList++; // a new list starts again at 1
    body.push(new Paragraph({ numbering: { reference: "steps", level: 0, instance: numberedList }, spacing: { after: 80 }, children: runs(m[1]) }));
  } else {
    body.push(new Paragraph({ spacing: { after: 160 }, children: runs(line) }));
  }
}

const doc = new Document({
  creator: "StallTrack",
  title: "StallTrack User Manual",
  styles: {
    default: { document: { run: { font: FONT, size: 22 }, paragraph: { spacing: { line: 276 } } } },
    paragraphStyles: [
      { id: "Title", name: "Title", basedOn: "Normal", run: { font: FONT, size: 40, bold: true, color: "1F3550" }, paragraph: { spacing: { before: 0, after: 240 }, keepNext: true } },
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true, run: { font: FONT, size: 28, bold: true, color: "1F3550" }, paragraph: { spacing: { before: 360, after: 140 }, keepNext: true, outlineLevel: 0 } },
    ],
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
      { reference: "steps", levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
    ],
  },
  sections: [{
    properties: { page: { size: { width: 12240, height: 15840 }, margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } } },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ children: ["Page ", PageNumber.CURRENT], size: 18 })] })] }) },
    children: body,
  }],
});

// Without the ZIP's folder entries: Word has refused a file that has them when opened through a cloud co-editing service.
Packer.toBuffer(doc)
  .then((buffer) => require("jszip").loadAsync(buffer, { createFolders: false }))
  .then((zip) => {
    // Only the folder records: zip.remove() on a folder would take its files with it.
    Object.keys(zip.files).filter((name) => zip.files[name].dir).forEach((name) => delete zip.files[name]);
    return zip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" });
  })
  .then((buffer) => {
    fs.writeFileSync(output, buffer);
    console.log(`Wrote ${output}`);
  });
