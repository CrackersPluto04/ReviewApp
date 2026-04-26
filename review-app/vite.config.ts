import { defineConfig } from 'vite';
import preact from '@preact/preset-vite';
import basicSsl from '@vitejs/plugin-basic-ssl';

// https://vitejs.dev/config/
export default defineConfig({
	plugins: [
		preact(),
		basicSsl(),
	],
	server: {
		port: 5173,
		https: true,
		open: true,
	}
});
