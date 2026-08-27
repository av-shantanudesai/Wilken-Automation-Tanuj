// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // `x != null` is used intentionally to cover both null and undefined;
      // rewriting to `!== null` would change behavior for undefined values.
      '@angular-eslint/template/eqeqeq': ['error', { allowNullOrUndefined: true }],
      // Existing templates use <label> as plain field captions (including
      // display-only detail grids with no form control). Associating them via
      // for/id would add label-click focus behavior and fixing otherwise would
      // require DOM structure changes, both of which are behavior changes.
      '@angular-eslint/template/label-has-associated-control': 'off',
    },
  },
]);
