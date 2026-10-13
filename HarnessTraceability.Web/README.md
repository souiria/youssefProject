# Harness Traceability Blazor migration

This project is the Blazor Server + MudBlazor replacement for the actively used WinForms path: authentication, main shell, traceability search, station production history, defected harness search, harness details data access, and About.

## Configuration

Set `ConnectionStrings:Wtr` in `appsettings.json` or user secrets. The `{server}` token is replaced with the existing site-to-server mapping. Set `Authentication:Url` if the authentication endpoint differs in your environment. Do not commit production credentials.

## Run

```bash
dotnet restore HarnessTraceability.Web/HarnessTraceability.Web.csproj
dotnet run --project HarnessTraceability.Web
```

The original WinForms project remains unchanged and is still the legacy desktop application. The new web project is added to the solution and uses the existing SQL views/relationships and authentication endpoint rather than mock data.

## SQL Server connectivity

The SQL databases are on internal SEWS-E hosts (`MOKEMLS001`, `MOASMLS001`, etc.). The Blazor server—not the browser—must run on a machine that can resolve those names and reach TCP port 1433 through the corporate network or VPN. Configure the real `wtrviewuser` password outside source control using user secrets, environment variables, or deployment settings. The default `CHANGE_ME` value is intentionally not usable. The app now forces TCP and reports a clear corporate-network/firewall message when a host cannot be reached.
