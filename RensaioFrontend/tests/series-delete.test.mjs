import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import ts from 'typescript';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

// Exercise the actual page handlers and dialog JSX without adding a test framework.
// These focused regressions complement (not replace) real API/browser verification.
const source = readFileSync(new URL('../src/app/library/series/page.tsx', import.meta.url), 'utf8');
const file = ts.createSourceFile('page.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const nodes = [];
function visit(node) { nodes.push(node); ts.forEachChild(node, visit); }
visit(file);
function compile(text) {
  return ts.transpileModule(text, { compilerOptions: { target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React } }).outputText;
}
const handlerNames = ['handleDeleteSeriesClick', 'handleDeleteSeriesConfirm', 'handleDeleteSeriesCancel'];
const handlers = handlerNames.map(name => {
  const node = nodes.find(n => ts.isVariableDeclaration(n) && n.name.getText(file) === name);
  assert.ok(node?.initializer, `Page handler ${name} exists`);
  return `${name} = ${node.initializer.getText(file)};`;
}).join('\n');
const dialog = nodes.find(n => ts.isJsxElement(n)
  && n.openingElement.tagName.getText(file) === 'Dialog'
  && n.openingElement.attributes.getText(file).includes('open={showDeleteDialog}'));
assert.ok(dialog, 'Delete dialog exists');

function fixture({ physical = true, mutation = async () => {}, cancellation = async () => {} } = {}) {
  const calls = { deletes: [], cancellations: [], navigation: [] };
  const context = vm.createContext({
    Error, React, seriesId: 'owned-series', showDeleteDialog: true,
    deletePhysicalFiles: physical, deleteError: null, isDeleting: false,
    deleteInFlightRef: { current: false }, displayTitle: 'Owned series',
    queryClient: { cancelQueries: async args => { calls.cancellations.push(args.queryKey); await cancellation(); } },
    deleteSeries: { isPending: false, mutateAsync: async args => { calls.deletes.push(args); return mutation(); } },
    router: { push: url => calls.navigation.push(url) },
    Trash2: () => null,
    Dialog: ({ open, children }) => open ? React.createElement('div', { role: 'dialog' }, children) : null,
    DialogContent: ({ children }) => React.createElement('div', {}, children),
    DialogHeader: ({ children }) => React.createElement('header', {}, children),
    DialogTitle: ({ children }) => React.createElement('h2', {}, children),
    DialogDescription: ({ children }) => React.createElement('p', {}, children),
    DialogFooter: ({ children }) => React.createElement('footer', {}, children),
    Switch: ({ id, checked, disabled }) => React.createElement('input', { id, type: 'checkbox', checked, disabled, readOnly: true }),
    Label: ({ htmlFor, children }) => React.createElement('label', { htmlFor }, children),
    Button: ({ disabled, children }) => React.createElement('button', { disabled }, children),
  });
  for (const [setter, key] of Object.entries({ setShowDeleteDialog: 'showDeleteDialog', setDeletePhysicalFiles: 'deletePhysicalFiles', setDeleteError: 'deleteError', setIsDeleting: 'isDeleting' })) {
    context[setter] = value => { context[key] = value; };
  }
  vm.runInContext(compile(handlers), context);
  const markup = () => renderToStaticMarkup(vm.runInContext(compile(`result = (${dialog.getText(file)});`), context));
  return { context, calls, markup };
}

for (const physical of [false, true]) {
  test(`failure preserves dialog and physical choice ${physical}, displays API error, allows retry`, async () => {
    let failed = true;
    const f = fixture({ physical, mutation: async () => { if (failed) throw new Error('Refusing to delete a series folder containing symbolic links.'); } });
    await f.context.handleDeleteSeriesConfirm();
    assert.equal(f.context.showDeleteDialog, true);
    assert.equal(f.context.deletePhysicalFiles, physical);
    assert.equal(f.context.isDeleting, false);
    assert.equal(f.context.deleteInFlightRef.current, false);
    assert.equal(f.calls.navigation.length, 0);
    assert.match(f.markup(), /role="alert"/);
    assert.match(f.markup(), /Refusing to delete a series folder containing symbolic links/);
    assert.doesNotMatch(f.markup(), /disabled=""/);
    assert.equal(f.markup().includes('checked=""'), physical);
    failed = false;
    await f.context.handleDeleteSeriesConfirm();
    assert.equal(f.calls.deletes.length, 2);
    assert.equal(f.calls.deletes[1].alsoPhysical, physical);
    assert.equal(f.context.deleteError, null);
    assert.equal(f.context.showDeleteDialog, false);
    assert.equal(f.context.deletePhysicalFiles, false);
    assert.deepEqual(f.calls.navigation, ['/library']);
  });

  test(`success passes physical choice ${physical}, closes/reset and navigates`, async () => {
    const f = fixture({ physical });
    await f.context.handleDeleteSeriesConfirm();
    assert.equal(f.calls.deletes[0].id, 'owned-series');
    assert.equal(f.calls.deletes[0].alsoPhysical, physical);
    assert.equal(f.calls.cancellations.length, 2);
    assert.equal(f.context.showDeleteDialog, false);
    assert.equal(f.context.deletePhysicalFiles, false);
    assert.equal(f.context.isDeleting, true); // queries stay stopped until navigation
    assert.deepEqual(f.calls.navigation, ['/library']);
  });
}

test('query cancellation is busy too: duplicate confirmation and cancel are blocked', async () => {
  let release;
  const waiting = new Promise(resolve => { release = resolve; });
  const f = fixture({ cancellation: () => waiting });
  const pending = f.context.handleDeleteSeriesConfirm();
  assert.equal(f.context.isDeleting, true);
  assert.equal((f.markup().match(/disabled=""/g) || []).length, 3);
  assert.match(f.markup(), /Deleting/);
  await f.context.handleDeleteSeriesConfirm();
  f.context.handleDeleteSeriesCancel();
  assert.equal(f.context.showDeleteDialog, true);
  assert.equal(f.context.deletePhysicalFiles, true);
  release();
  await pending;
  assert.equal(f.calls.deletes.length, 1);
  // Guard against the former early return unmounting the dialog while pending.
  const deletingReturn = nodes.find(n => ts.isIfStatement(n) && n.expression.getText(file) === 'isDeleting && !showDeleteDialog');
  assert.ok(deletingReturn);
});

test('cancel after failure clears error/choice; reopening has no stale error', async () => {
  const f = fixture({ mutation: async () => { throw new Error('Storage denied'); } });
  await f.context.handleDeleteSeriesConfirm();
  f.context.handleDeleteSeriesCancel();
  assert.equal(f.context.showDeleteDialog, false);
  assert.equal(f.context.deletePhysicalFiles, false);
  assert.equal(f.context.deleteError, null);
  f.context.handleDeleteSeriesClick();
  assert.equal(f.context.showDeleteDialog, true);
  assert.doesNotMatch(f.markup(), /role="alert"/);
});

test('unknown error receives useful fallback and releases busy state', async () => {
  const f = fixture({ mutation: async () => { throw null; } });
  await f.context.handleDeleteSeriesConfirm();
  assert.match(f.context.deleteError, /Failed to delete series.*try again/);
  assert.equal(f.context.isDeleting, false);
  assert.equal(f.context.deleteInFlightRef.current, false);
});
