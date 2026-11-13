// Generates every TRAZO brand asset from the Manrope font (SIL OFL 1.1) converted to outlines.
// Usage: node generate.mjs <outDir>
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import sharp from 'sharp';

const require = createRequire(import.meta.url);
const opentype = require('opentype.js');
const out = path.resolve(process.argv[2] ?? 'out');
fs.mkdirSync(out, { recursive: true });

const C = {
  blue: '#0B6BFF', blueOnDark: '#3D8BFF', navy: '#0B1F44', teal: '#00D4B4',
  white: '#FFFFFF', claim: '#8FB0E8', tint: '#EAF1FE'
};
const SIZE = 200;
const LETTER_SPACING = -0.015;

const fontFile = require.resolve('@fontsource/manrope/files/manrope-latin-600-normal.woff');
const claimFontFile = require.resolve('@fontsource/manrope/files/manrope-latin-500-normal.woff');
const toArrayBuffer = (b) => b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength);
const font = opentype.parse(toArrayBuffer(fs.readFileSync(fontFile)));
const claimFont = opentype.parse(toArrayBuffer(fs.readFileSync(claimFontFile)));

const n = (v) => String(Math.round(v * 100) / 100);
const cmdsToD = (cmds) => cmds.map((c) => {
  switch (c.type) {
    case 'M': return `M${n(c.x)} ${n(c.y)}`;
    case 'L': return `L${n(c.x)} ${n(c.y)}`;
    case 'C': return `C${n(c.x1)} ${n(c.y1)} ${n(c.x2)} ${n(c.y2)} ${n(c.x)} ${n(c.y)}`;
    case 'Q': return `Q${n(c.x1)} ${n(c.y1)} ${n(c.x)} ${n(c.y)}`;
    case 'Z': return 'Z';
    default: throw new Error('command ' + c.type);
  }
}).join('');
const xform = (cmds, s, tx, ty) => cmds.map((c) => {
  const o = { type: c.type };
  for (const k of ['x', 'x1', 'x2']) if (c[k] !== undefined) o[k] = c[k] * s + tx;
  for (const k of ['y', 'y1', 'y2']) if (c[k] !== undefined) o[k] = c[k] * s + ty;
  return o;
});
const bboxOf = (...cmdLists) => {
  const p = new opentype.Path();
  p.commands = cmdLists.flat();
  return p.getBoundingBox();
};

// ---- geometry ---------------------------------------------------------------------------
const glyphs = font.getPaths('Trazo', 0, 0, SIZE, { kerning: true, letterSpacing: LETTER_SPACING });
const tCmds = glyphs[0].commands;
const restCmds = glyphs.slice(1).flatMap((g) => g.commands);
const tb = bboxOf(tCmds);
const h = tb.y2 - tb.y1;
// The swash is a thin tapered stroke over the T, built relative to that T's own box.
const buildSwash = (box) => {
  const u = box.x2 - box.x1, hh = box.y2 - box.y1;
  const P = (ru, rh) => [box.x1 + ru * u, box.y1 + rh * hh];
  const [x0, y0] = P(-0.14, -0.15);
  const a = P(0.3, -0.215), b = P(0.82, -0.27), e = P(1.3, -0.4);
  const c = P(0.86, -0.17), d = P(0.34, -0.1), z = P(-0.14, -0.125);
  return [
    { type: 'M', x: x0, y: y0 },
    { type: 'C', x1: a[0], y1: a[1], x2: b[0], y2: b[1], x: e[0], y: e[1] },
    { type: 'C', x1: c[0], y1: c[1], x2: d[0], y2: d[1], x: z[0], y: z[1] },
    { type: 'Z' }
  ];
};
const swashCmds = buildSwash(tb);
// Monogram: same T but drawn heavier so it holds up at favicon and launcher sizes.
const heavyFile = require.resolve('@fontsource/manrope/files/manrope-latin-800-normal.woff');
const heavyFont = opentype.parse(toArrayBuffer(fs.readFileSync(heavyFile)));
const mTCmds = heavyFont.getPath('T', 0, 0, SIZE, { kerning: true }).commands;
const mSwashCmds = buildSwash(bboxOf(mTCmds));

const fit = (cmds, bb, pad) => ({
  x: bb.x1 - pad, y: bb.y1 - pad, w: bb.x2 - bb.x1 + 2 * pad, h: bb.y2 - bb.y1 + 2 * pad
});
const wordBB = bboxOf(tCmds, restCmds, swashCmds);
const monoBB = bboxOf(mTCmds, mSwashCmds);
const PAD = h * 0.14;

const wordmarkSvg = (tColor, textColor) => {
  const v = fit([], wordBB, PAD);
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${n(v.x)} ${n(v.y)} ${n(v.w)} ${n(v.h)}" role="img" aria-label="Trazo"><title>Trazo</title><path fill="${C.teal}" d="${cmdsToD(swashCmds)}"/><path fill="${tColor}" d="${cmdsToD(tCmds)}"/><path fill="${textColor}" d="${cmdsToD(restCmds)}"/></svg>\n`;
};
const monogramInner = (tColor, swashColor, s, cx, cy) => {
  const tx = cx - ((monoBB.x1 + monoBB.x2) / 2) * s;
  const ty = cy - ((monoBB.y1 + monoBB.y2) / 2) * s;
  return `<path fill="${swashColor}" d="${cmdsToD(xform(mSwashCmds, s, tx, ty))}"/><path fill="${tColor}" d="${cmdsToD(xform(mTCmds, s, tx, ty))}"/>`;
};
const monoW = monoBB.x2 - monoBB.x1, monoH = monoBB.y2 - monoBB.y1;
const monoDiag = Math.hypot(monoW, monoH);
const scaleForDiag = (diag) => diag / monoDiag;

const monogramSvg = (tColor) => {
  const v = fit([], monoBB, PAD);
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${n(v.x)} ${n(v.y)} ${n(v.w)} ${n(v.h)}" role="img" aria-label="Trazo"><title>Trazo</title><path fill="${C.teal}" d="${cmdsToD(swashCmds)}"/><path fill="${tColor}" d="${cmdsToD(tCmds)}"/></svg>\n`;
};
const tileSvg = (size, rxRatio, diagRatio, bg = C.navy) => {
  const rx = size * rxRatio;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}"><rect width="${size}" height="${size}" rx="${n(rx)}" fill="${bg}"/>${monogramInner(C.blueOnDark, C.teal, scaleForDiag(size * diagRatio), size / 2, size / 2)}</svg>`;
};

const write = (rel, data) => {
  const f = path.join(out, rel);
  fs.mkdirSync(path.dirname(f), { recursive: true });
  fs.writeFileSync(f, data);
  return f;
};
const png = async (rel, svg, w, h2 = w, opaque = false) => {
  fs.mkdirSync(path.dirname(path.join(out, rel)), { recursive: true });
  let img = sharp(Buffer.from(svg), { density: 384 }).resize(w, h2);
  if (opaque) img = img.flatten({ background: C.navy });
  await img.png({ compressionLevel: 9 }).toFile(path.join(out, rel));
};

// ---- brand/ SVG masters -----------------------------------------------------------------
write('brand/trazo-logo-light.svg', wordmarkSvg(C.blue, C.navy));
write('brand/trazo-logo-dark.svg', wordmarkSvg(C.blueOnDark, C.white));
write('brand/trazo-monogram-light.svg', monogramSvg(C.blue));
write('brand/trazo-monogram-dark.svg', monogramSvg(C.blueOnDark));
write('brand/trazo-app-icon.svg', tileSvg(1024, 0, 0.6));

// ---- Angular public/ --------------------------------------------------------------------
write('angular/favicon.svg', tileSvg(64, 0.2, 0.72));
write('angular/logo-light.svg', wordmarkSvg(C.blue, C.navy));
write('angular/logo-dark.svg', wordmarkSvg(C.blueOnDark, C.white));
write('angular/monogram.svg', monogramSvg(C.blueOnDark));
await png('angular/apple-touch-icon.png', tileSvg(512, 0, 0.6), 180, 180, true);
await png('angular/icon-192.png', tileSvg(512, 0.2, 0.7), 192);
await png('angular/icon-512.png', tileSvg(512, 0.2, 0.7), 512);
await png('angular/icon-maskable-512.png', tileSvg(512, 0, 0.6), 512, 512, true);
write('angular/site.webmanifest', JSON.stringify({
  name: 'TRAZO Administration', short_name: 'TRAZO', start_url: '/', display: 'standalone',
  background_color: C.navy, theme_color: C.navy,
  icons: [
    { src: 'icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
    { src: 'icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
    { src: 'icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
  ]
}, null, 2) + '\n');
// favicon.ico with PNG-compressed 16/32/48 images
{
  const sizes = [16, 32, 48];
  const bufs = [];
  for (const s of sizes) bufs.push(await sharp(Buffer.from(tileSvg(512, 0.2, 0.7)), { density: 384 }).resize(s, s).png({ compressionLevel: 9 }).toBuffer());
  const head = Buffer.alloc(6 + 16 * sizes.length);
  head.writeUInt16LE(0, 0); head.writeUInt16LE(1, 2); head.writeUInt16LE(sizes.length, 4);
  let offset = head.length;
  sizes.forEach((s, i) => {
    const o = 6 + i * 16;
    head.writeUInt8(s, o); head.writeUInt8(s, o + 1); head.writeUInt8(0, o + 2); head.writeUInt8(0, o + 3);
    head.writeUInt16LE(1, o + 4); head.writeUInt16LE(32, o + 6);
    head.writeUInt32LE(bufs[i].length, o + 8); head.writeUInt32LE(offset, o + 12);
    offset += bufs[i].length;
  });
  write('angular/favicon.ico', Buffer.concat([head, ...bufs]));
}

// ---- Android ----------------------------------------------------------------------------
{
  // 108dp adaptive canvas; the OS shows a 72dp window and masks to a 66dp safe circle.
  const s = scaleForDiag(60);
  const tx = 54 - ((monoBB.x1 + monoBB.x2) / 2) * s;
  const ty = 54 - ((monoBB.y1 + monoBB.y2) / 2) * s;
  const swash = cmdsToD(xform(mSwashCmds, s, tx, ty));
  const t = cmdsToD(xform(mTCmds, s, tx, ty));
  const vec = (body) => `<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android"\n    android:width="108dp"\n    android:height="108dp"\n    android:viewportWidth="108"\n    android:viewportHeight="108">\n${body}</vector>\n`;
  write('android/res/drawable/ic_launcher_foreground.xml', vec(
    `    <path android:fillColor="${C.teal}" android:pathData="${swash}"/>\n    <path android:fillColor="${C.blueOnDark}" android:pathData="${t}"/>\n`));
  write('android/res/drawable/ic_launcher_monochrome.xml', vec(
    `    <path android:fillColor="#FFFFFFFF" android:pathData="${swash}"/>\n    <path android:fillColor="#FFFFFFFF" android:pathData="${t}"/>\n`));
  write('android/res/values/ic_launcher_background.xml',
    `<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">${C.navy}</color>\n</resources>\n`);
  const adaptive = `<?xml version="1.0" encoding="utf-8"?>\n<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n    <background android:drawable="@color/ic_launcher_background"/>\n    <foreground android:drawable="@drawable/ic_launcher_foreground"/>\n    <monochrome android:drawable="@drawable/ic_launcher_monochrome"/>\n</adaptive-icon>\n`;
  write('android/res/mipmap-anydpi-v26/ic_launcher.xml', adaptive);
  write('android/res/mipmap-anydpi-v26/ic_launcher_round.xml', adaptive);

  // In-app wordmark (dark and light variants) as vector drawables for Compose/XML.
  const wv = fit([], wordBB, PAD);
  const wordVec = (tColor, textColor) => `<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android"\n    android:width="${n(wv.w / 4)}dp"\n    android:height="${n(wv.h / 4)}dp"\n    android:viewportWidth="${n(wv.w)}"\n    android:viewportHeight="${n(wv.h)}">\n    <group android:translateX="${n(-wv.x)}" android:translateY="${n(-wv.y)}">\n        <path android:fillColor="${C.teal}" android:pathData="${cmdsToD(swashCmds)}"/>\n        <path android:fillColor="${tColor}" android:pathData="${cmdsToD(tCmds)}"/>\n        <path android:fillColor="${textColor}" android:pathData="${cmdsToD(restCmds)}"/>\n    </group>\n</vector>\n`;
  write('android/res/drawable/trazo_wordmark_light.xml', wordVec(C.blue, C.navy));
  write('android/res/drawable/trazo_wordmark_dark.xml', wordVec(C.blueOnDark, C.white));
}

// ---- Google Play ------------------------------------------------------------------------
await png('play/icon-512.png', tileSvg(1024, 0, 0.6), 512, 512, true);
{
  const W = 1024, H = 500;
  const wb = fit([], wordBB, 0);
  const ws = 560 / (wb.w);
  const wtx = W / 2 - ((wordBB.x1 + wordBB.x2) / 2) * ws;
  const wty = 190 - ((wordBB.y1 + wordBB.y2) / 2) * ws;
  const claimPath = claimFont.getPath('ENTRENA · MEJORA · AVANZA', 0, 0, 22, { letterSpacing: 0.32 });
  const cb = claimPath.getBoundingBox();
  const ctx = W / 2 - (cb.x1 + cb.x2) / 2;
  const cty = 372;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}"><rect width="${W}" height="${H}" fill="${C.navy}"/>` +
    `<path fill="${C.teal}" d="${cmdsToD(xform(swashCmds, ws, wtx, wty))}"/>` +
    `<path fill="${C.blueOnDark}" d="${cmdsToD(xform(tCmds, ws, wtx, wty))}"/>` +
    `<path fill="${C.white}" d="${cmdsToD(xform(restCmds, ws, wtx, wty))}"/>` +
    `<path fill="${C.claim}" d="${cmdsToD(xform(claimPath.commands, 1, ctx, cty))}"/></svg>`;
  await png('play/feature-graphic-1024x500.png', svg, W, H, true);
}

const info = { capHeight: h, monoDiag, files: fs.readdirSync(out) };
console.log(JSON.stringify(info));
