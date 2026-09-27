# TF Studio

A desktop UI for this boilerplate's Terraform workflow. It runs the same `task` commands you run
by hand (so `.env`, `environments/<workspace>/` and the Taskfile stay the single source of truth),
streams their output live, and turns `plan.tfgraph` into a readable plan review before you apply it.

```
┌──────────── Electron (app/electron) ────────────┐
│  BrowserWindow ── http://127.0.0.1:<port>/ ──┐  │
│  main.js: spawns + supervises the server     │  │
└──────────────────────────────────────────────┼──┘
                                               ▼
┌──────── TF Studio server (app/server, .NET 10 Native AOT) ────────┐
│  wwwroot/      web UI (plain ES modules, no build step)           │
│  /api/*        project, tasks, tools, runs (+SSE), plan report    │
│  RunManager ─► task <name> [VAR=value] ─► terraform / tflint / …  │
│  PlanService ◄─ plan.tfgraph  (terraform show -json plan.tfplan)  │
└───────────────────────────────────────────────────────────────────┘
```

## What it does

| View | |
| --- | --- |
| **Overview** | Active workspace, backend state (detects when `.terraform/` was initialized for another environment → `tf:reconfigure`), the `init → validate → lint → plan` workflow run as sequential steps, security/cost/docs tasks, installed tools. |
| **Plan** | Summary tiles, resource list filterable by action, attribute-level diffs (`(known after apply)`, *forces replacement*, sensitive values masked server-side), outputs, drift. **Apply** runs `tf:apply:approve` on the saved plan after a confirmation (type the workspace name when resources are destroyed or the workspace is prod-like). |
| **Runs** | Live console with ANSI colors, replay after reconnect, stdin for Terraform prompts (`Enter a value:`), cancel. History is in memory for the session. |
| **Tasks** | Every task in `Taskfile.yml` / `.tasks/*.yml` (`app:*` excluded), with prompts for required vars (`tf:unlock ID=…`, `tf:workspace:* NAME=…`). |

Safety rails, all enforced by the server:

- Tasks must exist in the Taskfile, and variables are checked against a strict allow-list before they reach `task KEY=value`.
- Only one run at a time.
- `tf:apply:approve` is refused when `plan.tfplan` was made (in this session) for another workspace, or was already applied.
- An inherited `TF_WORKSPACE` is stripped from child processes, so `.env` always decides the target environment.

## Run it

Prerequisites: .NET 10 SDK, Node.js 22+, plus the boilerplate's own tools (`task`, `terraform`, …).

```ps1
task app:install     # npm install in app/electron (once)
task app:start       # Electron + `dotnet run` on this repository
task app:server      # server only; open the printed http://127.0.0.1:5080/#token=… in a browser
task app:smoke       # hidden run over every view, screenshots saved, exit code 0/1
```

In the desktop app, **File → Open Terraform Project…** switches to any fork of the boilerplate
(any folder with `Taskfile.yml` and `environments/`). The last project is remembered.

## Package it

```ps1
task app:publish     # dotnet publish -r win-x64 → app/server/bin/publish (Native AOT, single exe)
task app:dist        # publish + electron-builder → app/electron/dist (NSIS installer + portable exe)
```

Native AOT needs the MSVC linker: install Visual Studio (or Build Tools) with the
**Desktop development with C++** workload. Without it `dotnet publish` fails with
*Platform linker not found*. `dotnet build`/`run` don't need it.

## Security model

- The server listens on `127.0.0.1` only, on a dynamic port chosen at startup.
- Electron generates a random token for each launch and passes it through the `TFSTUDIO_TOKEN` environment variable, never argv. The UI receives it once in the URL fragment and sends it back as `X-TfStudio-Token` on every `/api` call. Other local web pages can't forge that header cross-origin, so they can't trigger a plan or apply.
- The token is removed from the environment of `task`/`terraform` child processes.
- Strict CSP, no `innerHTML` anywhere in the UI, and a sandboxed renderer (context isolation on, no Node). The preload script exposes exactly two actions: open project, reveal project.
- `local.tfvars` is never read or served. Sensitive plan values are replaced by `(sensitive value)` before the plan leaves the server. Input typed at a prompt is echoed in the log only for `yes`/`no`.

## Develop

- **UI**: edit `server/wwwroot/**` and reload the window (Ctrl+R). No rebuild is needed: in dev the server serves the project's `wwwroot` directly.
- **Server**: `dotnet build` runs the AOT/trim analyzers, with warnings treated as errors. JSON is source-generated only (`JsonSerializerIsReflectionEnabledByDefault=false`), so add every new API type to `Api/AppJsonContext.cs`, and every new external JSON shape (task/terraform output) to `Terraform/ExternalJsonContext.cs`.
- **Conventions the server relies on**: `server/Project/ProjectLayout.cs` (task names, `plan.tfplan` / `plan.tfgraph`, `.env`, `environments/`). Keep it in sync with `.tasks/TerraformTasks.yml`.
- `ELECTRON_RUN_AS_NODE` (set by VS Code for extension-host children) makes Electron behave like plain Node. `npm start` clears it through `scripts/launch.cjs`. Clear it yourself if you call `electron .` directly.
- Server options: `--root <project>`, `--port <n>` (0 = dynamic), `--parent-pid <pid>` (exit when that process dies), `--task-bin <path>`.
