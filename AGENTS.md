# AGENTS.md

Source for the Peter Tranchell website (https://peter-tranchell.uk/), built on the **cloudscribe** multi-tenant CMS.

## Project layout

- Single web app in `cloudscribe_PeterTranchell_NET6/`. The folder/solution name is historical — the csproj targets **net10.0** (`cloudscribe_PeterTranchell_NET6.csproj`). There is exactly one project; no test projects and no CI workflows.
- The app is almost entirely cloudscribe NuGet packages (`Version 10.0.*`). All custom wiring lives in `Config/` extension methods (`CloudscribeFeatures.cs`, `RoutingAndMvc.cs`, `Authorization.cs`, `DataProtection.cs`, `Localization.cs`) and in `Program.cs` / `Startup.cs`. Custom app code is minimal: `Services/`, `ViewModels/`, `HttpContextActionContextAccessor.cs`.
- `SharedThemes/tranchell1|2|3/` are full themes; `sitefiles/s1/` holds per-site content (FolderName multi-tenant mode). Custom view overrides live in `Views/` and `SharedThemes/tranchell3/Shared/`.

## Build & run

- Build: `dotnet build cloudscribe_PeterTranchell_NET6.sln` (single csproj; requires .NET 10 SDK). No tests, no linters, no CI to run.
- DB is SQL Server via EF Core (all storage packages are MSSQL). Schema is **auto-created/migrated on startup** in `Program.cs` (`EnsureDataStorageIsReady`) — no manual `dotnet ef` migrations, and running the app needs a reachable SQL Server.
- `appsettings.json` points at LocalDB. `appsettings.Development.json` overrides it with **real remote DB + SMTP credentials committed to the repo**; it is excluded from publish. Don't add new secrets to these files — use UserSecrets (`UserSecretsId` is set) or ask.
- `Startup.cs` deliberately uses legacy `UseMvc` routing under `#pragma warning disable MVC1005` — this is a documented workaround for cloudscribe.SimpleContent issue #466. Do not "modernize" it to `UseEndpoints`.

## Frontend

- The `tranchell3` theme's CSS is generated from SCSS via gulp (`gulpfile.js`): `gulp buildtranchell3ThemeCss` compiles `app-scss/style.scss` → `SharedThemes/tranchell3/wwwroot/css/{style,style.min}.css`. Run `gulp` (default task) to watch. Deps are in `cloudscribe_PeterTranchell_NET6/package.json`; run `npm install` there before building. `tranchell1`/`tranchell2` ship precompiled CSS only.
- Don't edit the generated CSS/JS bundles directly for tranchell3 styles — edit `app-scss/` and rebuild.

## Search (Lunr)

- Site search is client-side Lunr. `LunrSearch/` is a standalone Node indexer (axios + cheerio + lunr) that scrapes the live site and writes `cloudscribe_PeterTranchell_NET6/wwwroot/lunr-index/` (the ~9 MB `search-index.json` is committed). On the server it runs from a scheduled task — not part of the build. Do not regenerate/commit the index as part of other work.
- The csproj explicitly excludes `SharedThemes/tranchell3/wwwroot/lunr-index/**` and orphaned `tranchell2 - Copy` assets; leave those exclusions alone.

## Git

- Local default branch is `dev`; origin HEAD is `master`. Other long-lived branches exist (`net6`, `cs8.6`, `Qtool`, etc.). Check which branch you're on before committing.
- `Publish/` is an empty deploy staging dir; `.github/` has no workflows (only Azure-centric `copilot-instructions.md` — ignore for this repo).