import { defineConfig } from 'vite';
import preact from '@preact/preset-vite';
import basicSsl from '@vitejs/plugin-basic-ssl';

// https://vitejs.dev/config/
export default defineConfig({
	plugins: [
		preact(),
		basicSsl(),
	],
	// basicSsl() turns on HTTPS with its self-signed certificate
	server: {
		port: 5173,
		open: true,
	}
});
