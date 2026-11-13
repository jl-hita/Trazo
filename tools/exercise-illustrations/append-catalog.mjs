// Appends the exercises of catalog-entries.mjs that are missing from the catalog seed (one JSON object per line).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { entries } from './catalog-entries.mjs';
import { exercises } from './exercises.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const file = path.resolve(here, '../../src/Trazo.Infrastructure/Seeds/exercise-catalog-additions.json');
const text = fs.readFileSync(file, 'utf8');
const existing = new Set(JSON.parse(text).map((x) => x.id));

const illustrated = new Set(exercises.map((x) => x.id));
const missingImages = entries.filter((x) => !illustrated.has(x.id)).map((x) => x.id);
const missingEntries = [...illustrated].filter((id) => !entries.some((x) => x.id === id) && !existing.has(id));
if (missingImages.length || missingEntries.length) throw new Error(`Mismatch. No image: ${missingImages}. No entry: ${missingEntries}`);

const fresh = entries.filter((x) => !existing.has(x.id));
if (!fresh.length) { console.log('Nothing to add.'); process.exit(0); }
const eol = text.includes('\r\n') ? '\r\n' : '\n';
const body = text.replace(/\s*\]\s*$/, '');
const out = body + ',' + eol + fresh.map((x) => JSON.stringify(x)).join(',' + eol) + eol + ']' + eol;
JSON.parse(out);
fs.writeFileSync(file, out);
console.log(`Added ${fresh.length}: ${fresh.map((x) => x.id).join(', ')}`);
