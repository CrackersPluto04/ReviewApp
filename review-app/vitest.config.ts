import { defineConfig } from 'vitest/config';
import preact from '@preact/preset-vite';

// Separate from vite.config.ts: the tests need neither the dev certificate nor the dev server options.
export default defineConfig({
	plugins: [
		preact({ prefreshEnabled: false, devToolsEnabled: false, reactAliasesEnabled: false }),
	],
	resolve: {
		/*
		 * The app renders with Preact, so every library written for React (MUI, Emotion, react-router, dnd-kit)
		 * must get preact/compat. Real react/react-dom are installed as their peer dependencies, and any CJS build
		 * that require()s them would bring in a second renderer, which no alias reaches. So, like the browser build,
		 * resolve the ESM builds: the 'module' entry (react-transition-group and @dnd-kit have no exports map),
		 * the 'module' export condition (Emotion's 'import' one is a CJS wrapper) and 'module-sync'
		 * (react-router's ESM entry for Node).
		 */
		mainFields: ['module', 'jsnext:main', 'jsnext', 'main'],
		conditions: ['module', 'module-sync', 'development|production'],
		alias: [
			{ find: /^react$/, replacement: 'preact/compat' },
			{ find: /^react-dom$/, replacement: 'preact/compat' },
			{ find: /^react-dom\/client$/, replacement: 'preact/compat/client' },
			{ find: /^react-dom\/test-utils$/, replacement: 'preact/test-utils' },
			{ find: /^react\/jsx-runtime$/, replacement: 'preact/jsx-runtime' },
			{ find: /^react\/jsx-dev-runtime$/, replacement: 'preact/jsx-dev-runtime' },
		],
	},
	test: {
		environment: 'jsdom',
		include: ['tests/**/*.test.{ts,tsx}'],
		setupFiles: ['tests/setup.ts'],
		restoreMocks: true,
		unstubGlobals: true,
		server: {
			deps: {
				// Externalized packages are imported by Node directly and would skip the resolution above
				inline: [/@mui\//, /@emotion\//, /react-router/, /@dnd-kit\//, /react-transition-group/, /@testing-library\/preact/],
			},
		},
		reporters: ['default', 'junit'],
		outputFile: { junit: 'test-results/junit.xml' },
		coverage: {
			provider: 'v8',
			include: ['src/**/*.{ts,tsx}'],
			exclude: ['src/index.tsx', 'src/types/**'],
			reporter: ['text-summary', 'html', 'cobertura'],
			reportsDirectory: 'coverage',
		},
	},
});
