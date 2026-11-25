import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { parseRuntimeConfig } from './app/infrastructure/config/runtime-config';
import { Shell } from './app/presentation/shell/shell';

async function start(): Promise<void> {
  const response = await fetch('/config.json', { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`config.json could not be loaded (HTTP ${response.status}).`);
  }

  const runtime = parseRuntimeConfig(await response.json());
  await bootstrapApplication(Shell, appConfig(runtime, window.location.origin));
}

start().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  document.body.textContent = `VirtualBank could not start: ${message}`;
  console.error(error);
});
