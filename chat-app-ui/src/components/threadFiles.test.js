import test from 'node:test';
import assert from 'node:assert/strict';
import { getSelectedFile, getVisibleFiles } from './threadFiles.js';

test('keeps only valid files and prefers the active selection', () => {
  const files = [
    { path: 'src/App.jsx', content: 'alpha' },
    null,
    { path: 'src/main.jsx', content: 'beta' },
    { path: '', content: 'empty' },
  ];

  assert.deepEqual(getVisibleFiles(files).map((file) => file.path), ['src/App.jsx', 'src/main.jsx']);
  assert.equal(getSelectedFile(files, 'src/main.jsx')?.path, 'src/main.jsx');
  assert.equal(getSelectedFile(files, 'missing.js')?.path, 'src/App.jsx');
});

test('falls back to the first file when nothing is selected', () => {
  const files = [
    { path: 'README.md', content: 'hello' },
    { path: 'docs/notes.md', content: 'world' },
  ];

  assert.equal(getSelectedFile(files)?.path, 'README.md');
});
