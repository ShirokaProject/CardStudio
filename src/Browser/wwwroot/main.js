import { dotnet } from './_framework/dotnet.js';

const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
await runtime.runMain(runtime.getConfig().mainAssemblyName, [globalThis.location.href]);
