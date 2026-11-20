import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';

export function findExplicitAny(source, fileName = 'source.ts') {
  const sourceFile = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true);
  const matches = [];
  const visit = node => {
    if (node.kind === ts.SyntaxKind.AnyKeyword) {
      const position = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
      matches.push({ line: position.line + 1, column: position.character + 1 });
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return matches;
}

async function collectTypeScriptFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await collectTypeScriptFiles(fullPath));
    else if (entry.isFile() && entry.name.endsWith('.ts') && !entry.name.endsWith('.d.ts')) files.push(fullPath);
  }
  return files;
}

async function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const files = await collectTypeScriptFiles(path.join(root, 'src'));
  const violations = [];
  for (const file of files) {
    const source = await readFile(file, 'utf8');
    for (const match of findExplicitAny(source, file)) {
      violations.push(`${path.relative(root, file)}:${match.line}:${match.column} explicit any`);
    }
  }
  if (violations.length) {
    process.stderr.write(`Explicit any types are not allowed in the admin frontend:\n${violations.join('\n')}\n`);
    process.exitCode = 1;
  } else {
    process.stdout.write(`No explicit any types found in ${files.length} TypeScript files.\n`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) await main();
