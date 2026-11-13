// Renders every exercise in exercises.mjs.
//   node generate.mjs                 write WebP seed images (Seeds/exercise-images/{id}/{0,1}.webp)
//   node generate.mjs --sheet [ids]   write contact sheets to ./out for visual review (no seed output)
import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { exercises } from './exercises.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const seedRoot = path.resolve(here, '../../src/Trazo.Infrastructure/Seeds/exercise-images');
const args = process.argv.slice(2);
const sheet = args.includes('--sheet');
const only = args.filter((a) => !a.startsWith('--'));
const selected = only.length ? exercises.filter((e) => only.includes(e.id)) : exercises;

const render = (svg) => sharp(Buffer.from(svg)).png().toBuffer();

if (sheet) {
  fs.mkdirSync(path.join(here, 'out'), { recursive: true });
  const perSheet = 6, cell = 300;
  for (let s = 0; s * perSheet < selected.length; s++) {
    const group = selected.slice(s * perSheet, (s + 1) * perSheet);
    const composites = [];
    for (let i = 0; i < group.length; i++)
      for (let k = 0; k < 2; k++) {
        const png = await sharp(await render(group[i].frames[k]())).resize(cell, cell).toBuffer();
        composites.push({ input: png, left: (i % 2) * cell * 2 + k * cell, top: Math.floor(i / 2) * cell });
      }
    const file = path.join(here, 'out', `sheet-${s + 1}.png`);
    await sharp({ create: { width: cell * 4, height: cell * Math.ceil(group.length / 2), channels: 3, background: '#fff' } })
      .composite(composites).png().toFile(file);
    console.log(file, group.map((g) => g.id).join(', '));
  }
} else {
  for (const exercise of selected) {
    const folder = path.join(seedRoot, exercise.id);
    fs.mkdirSync(folder, { recursive: true });
    for (let k = 0; k < exercise.frames.length; k++) {
      const webp = await sharp(await render(exercise.frames[k]())).webp({ quality: 88 }).toBuffer();
      fs.writeFileSync(path.join(folder, `${k}.webp`), webp);
    }
    console.log(exercise.id);
  }
}
