// Responsibility: Compare TypeScript source declarations with the required public-interface headers.
// Public interface:
// (none)
import ts from 'typescript';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] ?? '');
const describe = process.argv[3] === '--describe';
const updateHeaders = process.argv[3] === '--update-headers';
if (!process.argv[2] || process.argv.length > 4 || process.argv[3] && !describe && !updateHeaders) {
  console.error('usage: node check.mjs SOURCE_ROOT [--describe | --update-headers]'); process.exit(2);
}
const banned = new Set(['contracts', 'shared', 'utils', 'helpers', 'common', 'misc', 'types', 'models']);
const bannedScratch = new Set(['tmp', 'temp', 'scratch']);
const nextAppRouterStems = new Set([
  'page', 'layout', 'loading', 'error', 'global-error', 'not-found',
  'template', 'default', 'route', 'opengraph-image', 'twitter-image',
  'icon', 'apple-icon', 'sitemap', 'robots', 'manifest',
]);
const nextRootEntryStems = new Set(['middleware', 'proxy', 'instrumentation', 'instrumentation-client']);
const inventory = {};
let failed = false, count = 0;
const unsafeFiles = new Set();
function diagnostic(file, node, id, message) {
  if (id === 'TS-COVERAGE' || id === 'TS-PARSE') unsafeFiles.add(path.resolve(file));
  const line = node && typeof node.getStart === 'function' ? node.getSourceFile().getLineAndCharacterOfPosition(node.getStart()).line + 1 : 1;
  console.error(`${file}:${line} ${id} ${message}`); failed = true;
}
function exported(node) { return node.modifiers?.some(m => m.kind === ts.SyntaxKind.ExportKeyword); }
function nameOf(node) { return node.name?.getText() ?? ''; }
function signature(node, name) { return `${name}(${node.parameters.map(p => p.name.getText()).join(', ')})`; }
function updatedHeader(original, tree) {
  const source = original.replaceAll('\r\n', '\n');
  // Only replace a recognized tree. Never invent responsibility text or consume other comments.
  const header = /^(\/\/ Responsibility: [^\n]+\n\/\/ Public interface:\n)((?:\/\/ (?:[| ]*(?:\+-- |`-- ).*|\(none\))\n)+)/.exec(source);
  if (!header || !header[1].split('\n')[0].slice(19).trim()) return original;
  const originalLines = original.match(/[^\n]*\n|[^\n]+$/g) ?? [];
  const headerLineCount = header[0].split('\n').length - 1;
  const ending = originalLines[0].endsWith('\r\n') ? '\r\n' : '\n';
  return originalLines.slice(0, 2).join('') + tree.map(line => `// ${line}${ending}`).join('') + originalLines.slice(headerLineCount).join('');
}
function unwrapExpression(expr) {
  while (expr && (ts.isAsExpression(expr) || ts.isTypeAssertionExpression?.(expr) || ts.isSatisfiesExpression?.(expr) || ts.isParenthesizedExpression(expr))) {
    expr = expr.expression;
  }
  return expr;
}
function isBehavioralOrDynamicObjectLiteral(node) {
  return node.properties.some(p => {
    if (ts.isSpreadAssignment(p) || ts.isMethodDeclaration(p) || ts.isGetAccessorDeclaration(p) || ts.isSetAccessorDeclaration(p)) return true;
    if (ts.isPropertyAssignment(p)) {
      const init = unwrapExpression(p.initializer);
      if (init && (ts.isArrowFunction(init) || ts.isFunctionExpression(init))) return true;
    }
    if (p.name && ts.isComputedPropertyName(p.name)) return true;
    return false;
  });
}
function publicMembers(node) {
  if (ts.isObjectLiteralExpression(node)) {
    if (isBehavioralOrDynamicObjectLiteral(node)) {
      diagnostic(node.getSourceFile().fileName, node, 'TS-COVERAGE', 'object-literal public surfaces require explicit review');
    }
    return [];
  }
  if (ts.isUnionTypeNode(node) || ts.isIntersectionTypeNode(node)) return node.types.flatMap(publicMembers);
  if (ts.isParenthesizedTypeNode(node)) return publicMembers(node.type);
  if (ts.isTypeReferenceNode(node) || ts.isTypeQueryNode(node) || ts.isMappedTypeNode(node) ||
      ts.isConditionalTypeNode(node) || ts.isIndexedAccessTypeNode(node)) {
    diagnostic(node.getSourceFile().fileName, node, 'TS-COVERAGE', 'referenced or computed public members require explicit review');
    return [];
  }
  if (ts.isFunctionTypeNode(node)) return [signature(node, 'call')];
  if (!node.members) return [];
  return node.members.flatMap(m => {
    if (m.modifiers?.some(x => x.kind === ts.SyntaxKind.PrivateKeyword || x.kind === ts.SyntaxKind.ProtectedKeyword) || m.name && ts.isPrivateIdentifier(m.name)) return [];
    if (ts.isConstructorDeclaration(m)) {
      return [signature(m, 'new'), ...m.parameters.filter(p => p.modifiers?.some(x =>
        x.kind === ts.SyntaxKind.PublicKeyword || x.kind === ts.SyntaxKind.ReadonlyKeyword) &&
        !p.modifiers?.some(x => x.kind === ts.SyntaxKind.PrivateKeyword || x.kind === ts.SyntaxKind.ProtectedKeyword)).map(nameOf)];
    }
    if (ts.isCallSignatureDeclaration(m)) return [signature(m, 'call')];
    if (ts.isIndexSignatureDeclaration(m)) return [signature(m, 'index')];
    const name = nameOf(m);
    if (!name) return [];
    if (ts.isGetAccessorDeclaration(m) || ts.isSetAccessorDeclaration(m)) return [name];
    const callable = m.type && ts.isFunctionTypeNode(m.type) ? m.type :
      m.initializer && (ts.isArrowFunction(m.initializer) || ts.isFunctionExpression(m.initializer)) ? m.initializer : null;
    return [m.parameters ? signature(m, name) : callable ? signature(callable, name) : name];
  });
}
function walk(directory) {
  const dirLower = path.basename(directory).toLowerCase();
  if (banned.has(dirLower) || (directory !== root && bannedScratch.has(dirLower))) diagnostic(directory, null, 'TS-STRUCTURE', 'forbidden catch-all or scratch folder');
  for (const item of fs.readdirSync(directory, {withFileTypes: true})) {
    const file = path.join(directory, item.name);
    if (item.isSymbolicLink()) { diagnostic(file, null, 'TS-COVERAGE', 'symlinks are not scanned'); continue; }
    if (item.isDirectory()) { if (!item.name.startsWith('.') && !['node_modules','dist'].includes(item.name)) walk(file); continue; }
    if (!/\.tsx?$/.test(file)) continue;
    count++;
    const original = fs.readFileSync(file, 'utf8');
    let source = original.replaceAll('\r\n', '\n');
    const trimmedSource = source.endsWith('\n') ? source.slice(0, -1) : source;
    const lineCount = trimmedSource.length === 0 ? 0 : trimmedSource.split('\n').length;
    if (lineCount > 350) diagnostic(file, null, 'TS-FILE', `file has ${lineCount} lines; maximum allowed is 350 (files over 100 lines are discouraged)`);
    const ast = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    for (const error of ast.parseDiagnostics) diagnostic(file, null, 'TS-PARSE', ts.flattenDiagnosticMessageText(error.messageText, '\n'));
    const stem = path.basename(file).replace(/\.tsx?$/, '');
    const relSegments = [path.basename(root), ...path.relative(root, file).split(path.sep)];
    const isNextEntryFile = (relSegments.includes('app') && nextAppRouterStems.has(stem)) || nextRootEntryStems.has(stem);
    const types = [], declarations = new Map();
    for (const node of ast.statements) {
      if (ts.isModuleDeclaration(node)) diagnostic(file, node, 'TS-COVERAGE', 'namespace/module surfaces require explicit review');
      if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node) || ts.isEnumDeclaration(node)) types.push(node);
      if (ts.isExportDeclaration(node) || ts.isExportAssignment(node)) {
        diagnostic(file, node, 'TS-COVERAGE', 're-exports/default exports require explicit public-surface review; checker does not infer them');
      }
      if (!exported(node)) continue;
      if (node.modifiers?.some(m => m.kind === ts.SyntaxKind.DefaultKeyword)) {
        if (!(isNextEntryFile && ts.isFunctionDeclaration(node) && nameOf(node))) {
          diagnostic(file, node, 'TS-COVERAGE', 'default export identity requires explicit public-surface review');
        }
      }
      if (ts.isFunctionDeclaration(node)) {
        const name = nameOf(node);
        if (name) declarations.set(signature(node, name), []);
      } else if (ts.isVariableStatement(node)) {
        for (const decl of node.declarationList.declarations) {
          if (!ts.isIdentifier(decl.name)) { diagnostic(file, decl, 'TS-COVERAGE', 'destructured public declarations require explicit review'); continue; }
          let children = [];
          const unwrappedInit = unwrapExpression(decl.initializer);
          if (unwrappedInit && ts.isObjectLiteralExpression(unwrappedInit)) children = publicMembers(unwrappedInit);
          const callable = decl.type && ts.isFunctionTypeNode(decl.type) ? decl.type :
            unwrappedInit && (ts.isArrowFunction(unwrappedInit) || ts.isFunctionExpression(unwrappedInit)) ? unwrappedInit : null;
          declarations.set(callable ? signature(callable, nameOf(decl)) : nameOf(decl), children);
        }
      } else if (nameOf(node)) {
        if (node.heritageClauses?.length) diagnostic(file,node,'TS-COVERAGE','inherited public members require explicit review');
        declarations.set(nameOf(node), publicMembers(ts.isTypeAliasDeclaration(node) ? node.type : node));
      }
    }
    const isTypesFile = (stem === 'Types' || stem === 'types') && file.endsWith('.ts');
    if (isTypesFile) {
      const hasBehaviorNode = ast.statements.some(n => ts.isClassDeclaration(n) || ts.isFunctionDeclaration(n) || ts.isVariableStatement(n));
      const hasCallableMember = [...declarations.entries()].some(([k, members]) => k.includes('(') || members.some(m => m.includes('(')));
      if (!types.length || hasBehaviorNode || hasCallableMember) {
        diagnostic(file, null, 'TS-FILE', 'Types.ts may only group pure data types/DTOs without behavior');
      }
    } else {
      const exportedFnNames = new Set([...declarations.keys()].map(k => k.split('(')[0]));
      const props = types.filter(t => file.endsWith('.tsx') && (
        nameOf(t) === `${stem}Props` ||
        (isNextEntryFile && (exportedFnNames.has(nameOf(t).replace(/Props$/, '')) || /^(?:Page|Layout|Error|GlobalError|Loading|NotFound|Template|Default)Props$/.test(nameOf(t))))
      ));
      const primary = types.filter(t => !props.includes(t));
      if (primary.length > 1 || props.length > 1) diagnostic(file, null, 'TS-FILE', 'one concept/type per file; component props and pure DTOs in Types.ts are the narrow exceptions');
      if (!isNextEntryFile) {
        const matchesStem = key => {
          const base = key.split('(')[0];
          if (base === stem) return true;
          if (/^[A-Z][A-Z0-9_]*$/.test(base)) {
            const camel = base.toLowerCase().replace(/_([a-z0-9])/g, (_, c) => c.toUpperCase());
            const kebab = base.toLowerCase().replaceAll('_', '-');
            return camel === stem || kebab === stem;
          }
          return false;
        };
        if (primary.length === 1 && !/\.test$/.test(stem) && nameOf(primary[0]) !== stem) diagnostic(file, primary[0], 'TS-FILE', `rename file for ${nameOf(primary[0])}`);
        if (primary.length === 0 && declarations.size && !/\.test$/.test(stem) && ![...declarations.keys()].some(matchesStem)) diagnostic(file, null, 'TS-FILE', 'filename must name a primary export');
      }
    }
    const sorted = [...declarations].sort(([a],[b]) => a < b ? -1 : a > b ? 1 : 0), tree = [];
    sorted.forEach(([name, members], index) => {
      const last = index === sorted.length-1;
      tree.push(`${last ? '`-- ' : '+-- '}${name}`);
      const children = [...new Set(members)].sort();
      children.forEach((m,j) => tree.push(`${last ? '    ' : '|   '}${j === children.length-1 ? '`-- ' : '+-- '}${m}`));
    });
    if (!tree.length) tree.push('(none)');
    inventory[file] = tree;
    if (updateHeaders && !unsafeFiles.has(path.resolve(file))) {
      const updated = updatedHeader(original, tree);
      if (updated !== original) {
        fs.writeFileSync(file, updated);
        console.log(`UPDATED: ${file}`);
        source = updated.replaceAll('\r\n', '\n');
      }
    }
    if (!describe) {
      const lines = source.split('\n'), header = [];
      for (const line of lines) { if (!line.trim()) continue; if (!line.startsWith('//')) break; header.push(line.slice(2).replace(/^ /, '')); }
      if (!header[0]?.startsWith('Responsibility: ') || !header[0].slice(16).trim() || header[1] !== 'Public interface:' || header.slice(2).join('\n') !== tree.join('\n')) diagnostic(file, null, 'TS-HEADER', `require Responsibility and exact public tree:\n// ${tree.join('\n// ')}`);
    }
    const role = [path.basename(root), ...path.relative(root, file).split(path.sep)]
      .findLast(p => p === 'domain' || p === 'application');
    function visit(node) {
      if (node.kind === ts.SyntaxKind.AnyKeyword) diagnostic(file, node, 'TS-STRICT', 'any is forbidden; narrow unknown');
      if (role && ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier)) {
        const rawParts = node.moduleSpecifier.text.split('/');
        const parts = rawParts[0]?.startsWith('@') && rawParts.length > 2 ? rawParts.slice(2) : rawParts;
        if (parts.some(p => ['infrastructure','configuration','api','ui'].includes(p) || role === 'domain' && p === 'application')) diagnostic(file, node, 'TS-BOUNDARY', `${role} imports outer layer`);
      }
      ts.forEachChild(node, visit);
    }
    visit(ast);
  }
}
try {
  if (fs.lstatSync(root).isSymbolicLink()) throw new Error('source root must not be a symlink');
  walk(root);
} catch (error) { console.error(error.message); process.exit(2); }
if (!count) { console.error('no TypeScript source files found'); process.exit(2); }
if (describe) console.log(JSON.stringify(inventory));
else if (!failed) console.log(`PASS: ${count} TypeScript files; parsed placement, public trees, no-any, folders and named-layer imports. Full conformance requires review.`);
process.exitCode = failed ? 1 : 0;
