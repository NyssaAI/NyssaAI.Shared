// Responsibility: Select authored sample tests once, excluding emitted build copies.
// Public interface:
// `-- default (test configuration)
import {defineConfig} from 'vitest/config';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, '../..');

export default defineConfig({
  root: ROOT,
  cacheDir: path.resolve(__dirname, '../../.temp/vite-cache'),
  server: {
    fs: {
      allow: [ROOT]
    }
  },
  resolve: {
    alias: {
      'pg': path.resolve(__dirname, 'node_modules/pg'),
      '@testing-library/react': path.resolve(__dirname, 'node_modules/@testing-library/react'),
      'react': path.resolve(__dirname, 'node_modules/react'),
      'react-dom': path.resolve(__dirname, 'node_modules/react-dom')
    }
  },
  test: {
    include: ['test/use_case/typescript/**/*.test.{ts,tsx}']
  }
});
