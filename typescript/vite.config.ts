import { defineConfig } from 'vite';
import { resolve } from 'path';
import dts from 'vite-plugin-dts';

export default defineConfig({
  build: {
    lib: {
      entry: {
        index: resolve(import.meta.dirname, 'src/index.ts'),
      },
      name: 'SubscrioPayments',
      formats: ['es', 'cjs'],
    },
    rollupOptions: {
      external: [
        'subscrio',
        'pg',
        'zod',
      ],
    },
  },
  plugins: [
    dts({
      include: ['src'],
      outDir: 'dist',
    }),
  ],
});
