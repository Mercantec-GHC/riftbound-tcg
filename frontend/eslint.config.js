import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [{
          group: [
            '../features/game/rules/*',
            '../game/rules/*',
            '../../features/game/rules/*',
            '**/features/game/rules/*',
            '**/archive/local-hotseat/*',
            '**/archive/local-hotseat/**',
          ],
          message: 'Production frontend code must render server state and submit server-approved actions; archived local-hotseat rule helpers are reference-only.',
        }],
      }],
    },
  },
])
