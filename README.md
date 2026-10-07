<div align="center">

<img width="500px" src="./RensaioFrontend/public/rensaiow.png" alt="Rensaiō">
<br/>

</div>

<div align="center">

[![Discord](https://img.shields.io/discord/1516878111239700510?label&logo=discord&logoColor=white&color=blue)](https://discord.gg/f53BRuyVZf)&nbsp;&nbsp;
[![Website](https://img.shields.io/badge/-rensaio.net-blue?logo=googlechrome&logoColor=white)](https://www.rensaio.net)&nbsp;&nbsp;
[![License](https://img.shields.io/github/license/maxpiva/rensaio?label&color=blue)](./LICENSE)&nbsp;&nbsp;
[![Docker Pulls](https://img.shields.io/docker/pulls/maxpiva/rensaio?label&logo=docker&logoColor=white&color=blue)](https://hub.docker.com/r/maxpiva/rensaio)&nbsp;&nbsp;
[![Release](https://img.shields.io/github/v/release/maxpiva/rensaio?label&logo=github&color=blue)](https://github.com/maxpiva/Rensaio/releases)

</div>

<div align="center">

<strong>Rensaiō</strong> is a modern fork of the original <strong>Kaizoku</strong> and <strong>Kaizoku Next Gen</strong> by OAE, built to fill the void and bring a streamlined series manager back to life.

<strong>What does it do?</strong><br/>

When you subscribe to a series, it will automatically download it. Whenever the series is updated in any of your configured providers, new chapters will be downloaded automatically, in a "drop and forget" fashion.

</div>

> [!IMPORTANT]
> ⬆️ **You can upgrade directly from Kaizoku.Net to Rensaiō.** No changes are required on your side: simply run the new executable or update your Docker image, and everything will be upgraded automatically.
>
> Nevertheless, **before upgrading**, make sure to back up your config directory, including your database. Since the devil never rests.

---

## Table of Contents

- [Quick Start](#-quick-start)
- [What It Does](#-what-it-does)
- [Key Features](#-key-features)
- [Under the Hood](#-under-the-hood)
- [Issues](#-issues)
- [Running Android libraries on .NET, is that possible?](#-running-android-libraries-on-net-is-that-possible)
- [Docker Support](#-docker-support)
  - [Volumes](#-volumes)
  - [Ports](#-ports)
  - [Permissions](#-permissions)
  - [Network Mode](#-network-mode)
  - [One-Liner Run Command](#-example-one-liner-run-command)
  - [Docker Compose Example](#docker-compose-example)
  - [Unraid Template](#-unraid-template)
  - [Helm Chart](#-helm-chart)
- [Single Sign-On (OpenID Connect)](#-single-sign-on-openid-connect)
- [Desktop App](#-desktop-app)
- [Build It Yourself](#-build-it-yourself)
- [Resource Usage](#-resource-usage)
- [Contributing](#-contributing)
- [License](#-license)

---

## 🚀 Quick Start

```bash
docker run -d \
  --name rensaio \
  --network host \
  -e PUID=99 -e PGID=100 -e UMASK=022 \
  -v /path/to/config:/config \
  -v /path/to/series:/series \
  maxpiva/rensaio:latest
```

Open `http://<host-ip>:9833` and follow the setup wizard.

For Docker Compose, Unraid, or Kubernetes (Helm), see [Docker Support](#-docker-support).

---

## 🎯 What It Does

Rensaiō is a **series manager** that prioritizes simplicity, speed, and reliability.

It uses the power of  **MIHON extensions** to connect with multiple sources.

---

## ✨ Key Features

- 🧙‍♂️ **Startup Wizard**  
  Automatically imports your existing library.

- 🔁 **Temporary vs Permanent Sources**  
  - Chapters are only downloaded from **temporary** sources when there is no permanent sources 
  - Auto-deleted if a **permanent** source later provides them.

- 🔎 **Multi-Search & Multi-Linking**  
  Search and link one series to **multiple sources/providers**.

- 📥 **Automatic Downloads**

  Everything is automatic, Retries, Reschedules. With a dedicated download Page.

- 🔄 **Auto-Updates**  
  Extensions are kept up to date.

- 👥 **Multi-User System**
  Create separate accounts with different permission levels. Invite people, and control who has access to what. Optionally enable authentication to restrict access to authorized users only, with password login or [Single Sign-On](#-single-sign-on-openid-connect) through any OpenID Connect provider.

- 🩺 **Status & Health Dashboard**
  A dedicated page that shows which series and providers need attention. Color-coded alerts (green/yellow/red) help you spot issues at a glance like broken providers, stale series with no new chapters, and more.

- 📡 **OPDS Server**
  Read your library from any OPDS-compatible reader app. Browse by all series, what's new, what you're currently reading, categories, and tags. Reading progress syncs back automatically. Each user gets their own unique random OPDS path (e.g. `door-pebble`) for private access. Example: ```https://rensaio.example.com/door-pebble```

- 🤖 **MCP Server (AI Integration)**
  Expose your library to AI tools through the Model Context Protocol. Let LLMs search your series, check status, and more, all respecting user permissions. Just add /mcp to your OPDS path. Example: ```https://rensaio.example.com/door-pebble/mcp```

- 📖 **Read State Tracking**
  Remembers where you left off reading each chapter, across all your devices, backed in your series, not the database.

- 🔗 **External Scrobbler Sync**
  Sync your reading progress with external trackers. Supported providers: **AniList**, **MyAnimeList**, **Kitsu**, **MangaDex**. Each user controls their own connections and sync settings.

- 🧹 **Filename Normalization**  
  Rebuild your library easily with consistent naming, that will help you reimport it back when needed.
  In this fork, **Settings → Storage → Chapter filename template** optionally changes
  future archive names. Leave it empty for the original canonical format. Supported
  tokens: `{series}`, `{chapter}`, `{chapter:0000}` (1–8 zeros), `{title}`, `{source}`,
  `{language}`. Example: `Capítulo {chapter:0000}`. Fractional chapters are preserved.
  Paths, dots, filename extensions, and unknown tokens are rejected; `.cbz` and a
  source/language plus stable identity suffix are automatic. Existing unrelated
  files are never overwritten by custom names. **Keep `rensaio.json` beside custom
  archives for recovery**: filename-only recovery is not guaranteed. ComicInfo is
  unchanged; the existing series Rename action skips custom archive names (it
  recognizes canonical names only). This setting does not rename existing archives
  or customize their internal image names.

- 🧾 **ComicInfo.xml Injection**  
  Chapters include rich metadata from the original source.

- 🖼️ **Extras**
  - Stores `cover.jpg` per series
  - This fork optionally injects that existing JPEG into **new** CBZ files via
    **Settings → Storage → Include series cover in new CBZ files** (off by default).
    Choose the cover provider using the existing series source settings. The cover
    is `0000-cover.jpg`, first in ZIP and natural filename order, with ComicInfo
    `Pages/Page Image="0" Type="FrontCover"` and an updated total PageCount.
    Only the fixed `cover.jpg` under that series in configured storage is read;
    symlinks/path escapes are refused, JPEG bytes/decoding are validated, and
    limits are 5 MiB, 8192 pixels per side, and 32 megapixels. Missing/invalid
    covers log a message and leave the chapter without an injected cover.
    There is no new remote fetch, RSS/scraping service, upload endpoint, manual
    chapter-cover editor, or retroactive archive modification. Existing archives
    remain untouched. Each CBZ repeats the cover bytes, so enable only if you
    accept the extra disk usage. Archive-reader page counts include the cover.
  - Stores `rensaio.json` for full metadata mapping, and read-state stored with you series.
  - External Domain Support for reverse proxy scenarios.
  - Support for jxl, jp2, avif image formats, with real-time transcoding for clients not supporting them.
  - And much more...

---

## 🛠️ Under the Hood

Rensaiō is composed of:

- **Frontend**: A beautiful UI forked from [Kaizoku Next by OAE](https://github.com/oae/kaizoku/tree/next) (Next.js).
- **Backend**: A custom .NET engine that manages schedules, downloads, metadata, OPDS/MCP servers, and scrobbler sync, with a Mihon Bridge that enables the use of Mihon Android extensions.

---

## ⚙️ Issues

- If you encounter any issues, check the `logs` folder. You can review the logs there or upload them to share feedback.

---

## 🤔 Running Android libraries on .NET, is that possible?

Only the **MIHON** extensions are actively maintained, and they are distributed as Android APKs. So we need to hack around that!

By leveraging the Java/Android bridge originally created by the [Suwayomi](https://github.com/Suwayomi/Suwayomi-Server) team, and adapting parts of it to fit our use case, including replacing KCEF with JCEF Maven we can generate a Java 8 Android compatibility layer with all required Java dependencies included.

Then use [IKVM](https://github.com/ikvmnet/ikvm) to run this on .NET.

---

## 🐳 Docker Support

- Available for both `amd64` and `arm64`.

### 📁 Volumes

| Container Path | Description                      |
|----------------|----------------------------------|
| `/config`      | Stores application configuration |
| `/series`      | Stores series                    |

### 🌐 Ports

| Port  | Service         | Required | Notes                        |
|-------|--------------|----------|------------------------------|
| 9833  | Rensaiō UI   | ✅       | Web interface                |

### 👤 Permissions

| Variable | Value | Description                    |
|----------|-------|--------------------------------|
| `PUID`    | 99    | Host user ID                   |
| `PGID`   | 100   | Host group ID                  |
| `UMASK`  | 022   | File permission mask (default) |

> Ensure the specified PUID and PGID have write access to your mounted `/config` and `/series` directories.

### 🌐 Network Mode

It is recommended to use **host networking** for optimal performance when downloading a lot and querying multiple providers in parallel. This applies to the Docker one-liner and Unraid template below; the Compose and Helm examples use bridged/`ClusterIP` networking instead since host networking isn't practical (or needed) in those environments.

### 🚀 Example: One-Liner Run Command

```bash
docker run -d \
  --name Rensaio \
  --network host \
  -p 9833:9833 \
  -e PUID=99 \
  -e PGID=100 \
  -e UMASK=022 \
  -v /path/to/your/config:/config \
  -v /path/to/your/series:/series \
  maxpiva/rensaio:latest
```
Replace /path/to/your/config and /path/to/your/series with real paths on your host.

### Docker Compose Example

Runnable file: [`examples/docker-compose.yml`](./examples/docker-compose.yml)

```yaml
services:
  rensaio:
    container_name: rensaio
    image: 'maxpiva/rensaio:latest'
    volumes:
        - '/path/to/your/series:/series'
        - '/path/to/your/config:/config'
    environment:
        - UMASK=022
        - PGID=100
        - PUID=99
    ports:
        - '9833:9833'
```

### 🧩 Unraid Template

Template file: [`examples/unraid.xml`](./examples/unraid.xml)

```xml
<Container>
  <Name>Rensaiō</Name>
  <Repository>maxpiva/rensaio:latest</Repository>
  <Registry>https://hub.docker.com/r/maxpiva/rensaio</Registry>
  <Network>host</Network>
  <MyID>rensaio</MyID>
  <Shell>sh</Shell>
  <Privileged>false</Privileged>
  <Support>https://github.com/maxpiva/rensaio/issues</Support>
  <Project>https://github.com/maxpiva/rensaio</Project>
  <Overview>Rensaiō – a feature-complete series manager powered by Mihon extensions. </Overview>
  <Category>MediaManager:Comics</Category>

  <Config Name="Config Folder" Target="/config" Default="/mnt/user/appdata/rensaio" Mode="rw" Description="Path to store configuration, database, and settings." Type="Path" />
  <Config Name="Series Folder" Target="/series" Default="/mnt/user/media/series" Mode="rw" Description="Path where series and chapters will be downloaded." Type="Path" />

  <Config Name="PUID" Target="PUID" Default="99" Mode="rw" Description="User ID to run the container as." Type="Variable" />
  <Config Name="PGID" Target="PGID" Default="100" Mode="rw" Description="Group ID to run the container as." Type="Variable" />
  <Config Name="UMASK" Target="UMASK" Default="022" Mode="rw" Description="UMASK for file permissions." Type="Variable" />

  <WebUI>http://[IP]:9833</WebUI>

  <TemplateURL>https://raw.githubusercontent.com/maxpiva/rensaio/main/examples/unraid.xml</TemplateURL>
  <Icon>https://raw.githubusercontent.com/maxpiva/rensaio/refs/heads/main/RensaioFrontend/public/rensaio.png</Icon>
</Container>
```

### ⎈ Helm Chart

A Helm chart is available at [`charts/rensaio`](./charts/rensaio) for running Rensaiō on Kubernetes. It deploys a single container, a `Service` on port `9833`, and PVCs for `/config` and `/series`.

```bash
helm dependency update ./charts/rensaio
helm install rensaio ./charts/rensaio -n rensaio --create-namespace
```

Configure `PUID`/`PGID`/`UMASK`, image tag, PVC sizes/storage classes, and ingress via `values.yaml`. A sample override — existing PVC claims, a dedicated storage class, and ingress with TLS — is at [`examples/helm-values.yaml`](./examples/helm-values.yaml):

```bash
helm install rensaio ./charts/rensaio -n rensaio --create-namespace \
  -f examples/helm-values.yaml
```

---

## 🔐 Single Sign-On (OpenID Connect)

Rensaiō can log users in through any OpenID Connect provider (Authentik, Keycloak, Pocket ID, Authelia, Zitadel, ...). Password login keeps working alongside it, and OPDS / MCP access is unchanged because those use the per-user path, not the login.

### Setup

1. **Enable Authentication** in *Settings → Security* and set the **External Domain** (e.g. `https://rensaio.example.com`).
2. At your identity provider, create an OIDC client:
   - Type: *confidential* (with a client secret) or *public* (PKCE only, no secret). PKCE is always used.
   - Redirect / callback URL: `https://rensaio.example.com/api/auth/oidc/callback`
   - Scopes: `openid profile email` (add `groups` if you want group mapping)
3. Back in *Settings → Security*, turn on **Single Sign-On**, fill in the **Issuer URL** (the provider's base URL, without `/.well-known/openid-configuration`), the **Client ID** and, for a confidential client, the **Client Secret**. Save.
4. The login page now shows a **Single Sign-On** button.

On first sign-in a user is matched to an existing Rensaiō account with the same username (exact match first, then case-insensitive if unambiguous) and linked to it. Later sign-ins use the link, so renaming the account is safe. If no account matches, sign-in is refused unless **auto-register** is on (see below). The **Owner** account is never linked and always logs in with its password.

The client secret is write-only: the Settings page never displays it, and saving with the field empty keeps the stored value.

### Advanced options (`appsettings.json` or environment variables)

Everything beyond the four basics lives in the `Oidc` section of `appsettings.json`. Each key can also be set as an environment variable using the `Oidc__Key` form, which is convenient for Docker; environment variables override the file, and both override what is stored from the Settings page.

| Key | Default | Description |
| --- | --- | --- |
| `Enabled`, `Issuer`, `ClientId`, `ClientSecret`, `ButtonLabel` | *(from Settings)* | Set here to manage them outside the UI; the Settings fields become read-only |
| `Scopes` | `openid profile email groups` | Scopes requested from the provider |
| `UsernameClaim` | `preferred_username` | Claim used as the Rensaiō username (falls back to `name`, then `email`) |
| `GroupsClaim` | `groups` | Claim holding the user's groups |
| `AdminGroup` | *(empty)* | Members get the **Admin** level on every login |
| `ManagerGroup` | *(empty)* | Members get the **Manager** level on every login |
| `DefaultLevel` | `User` | Level for auto-registered users, and for mapped users in neither group |
| `AutoRegister` | `false` | Create a Rensaiō account on first sign-in when none matches |
| `HidePasswordLogin` | `false` | Hide the username/password form and show only the SSO button |
| `AutoRedirect` | `false` | Skip the login page and go straight to the provider |
| `SyncAvatar` | `true` | Copy the provider's profile picture (`picture` claim) to the user's avatar on each login |
| `RedirectUri` | *(derived)* | Override the callback URL when the External Domain is not what the provider sees |

When `AdminGroup` or `ManagerGroup` is set, the user's level is re-evaluated on every login that carries the groups claim: in the admin group → Admin, in the manager group → Manager, otherwise `DefaultLevel`. A login without the groups claim leaves the level untouched. The **Owner** level is never granted by SSO.

Docker example:

```yaml
environment:
  - Oidc__Enabled=true
  - Oidc__Issuer=https://id.example.com
  - Oidc__ClientId=rensaio
  - Oidc__ClientSecret=xxxxxxxx
  - Oidc__AdminGroup=rensaio-admins
  - Oidc__AutoRegister=true
  - Oidc__HidePasswordLogin=true
```

---

## 🖥️ Desktop App

- A **tray application** based on Avalonia is available in the [Releases](https://github.com/maxpiva/Rensaio/releases).
- Currently tested only on **Windows**. Testers for Linux and macOS are welcome, as I'm unable to verify it myself.

---

## 🧱 Build It Yourself

Build scripts are provided for convenience:

### Frontend
```powershell
.\build_frontend.ps1
```
Builds the Next.js frontend and packages it as `wwwroot.zip` for the backend to serve.

### Docker Image
```powershell
.\build_docker.ps1
```
Restores and publishes the backend for `linux-x64` and `linux-arm64`, then builds and pushes a multi-arch Docker image.

### Desktop Apps + Backend
```powershell
.\build_apps.ps1
```
Publishes the backend and tray app for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, and `osx-arm64`, then zips the tray binaries.

---

## ⚠️ Resource Usage

Be aware: **Rensaiō** can be **memory-intensive**, especially when managing large libraries or doing parallel searches and downloads.

### WebView/CEF renderer processes (`jcef_helper`)

Extensions that need a browser context (anti-bot / Cloudflare bypass via the `WebViewFetchInterceptor`) spawn Chromium renderer processes (`jcef_helper --type=renderer`), each consuming roughly 80–200 MB. To keep long-running instances from accumulating orphaned helpers and exhausting memory, Rensaiō bounds and reaps them automatically:

| Knob | Default | Meaning |
|------|---------|---------|
| `cefMaxRenderers` | `4` | Hard cap on concurrently alive renderer processes **across all extensions**. Requests beyond the cap degrade to the direct network chain instead of spawning another helper. Also passed to CEF as `--max-render-processes`. |
| `cefIdleTimeoutMs` | `300000` | Idle time (ms) before an unused pooled WebView's browser is destroyed by the watchdog sweep (5 minutes). |
| `cefWebViewPoolEnabled` | `true` | Enables host-keyed WebView reuse (same host reuses the same browser/cookie context) instead of a fresh WebView per request. |

These are configurable from Settings → Server (`cefMaxRenderers`, `cefIdleTimeoutMs`, `cefWebViewPoolEnabled`).

**How it stays bounded:**
1. The authoritative renderer budget (`RendererGate`) is enforced **inside the CEF WebView provider itself**, at browser creation — so the cap covers every WebView in the process, including extension-owned ones (e.g. Keiyoushi-style `runWebView`/`WebViewSession` helpers), not just the interceptor path. When the cap is reached the provider refuses to spawn another renderer; the interceptor pre-checks the budget and falls back to the direct network/FlareSolverr chain.
2. WebViews are pooled per host (`WebViewPool`) instead of created per request, and are deterministically destroyed in a `finally` block on success/timeout/error — the old racy `postDelayed(destroy)` is gone.
3. A time-gated watchdog sweep runs on the existing safe CEF pump path (the IKVM-attached daemon on Docker, the Avalonia UI thread on desktop) and evicts idle browsers, posting destruction to the main looper. It never calls JCEF from a raw thread.

**Validation checklist (long-running instances):**
- Run with several WebView-requiring sources active for 24–48 h.
- Monitor with your platform's process tooling (`ps aux --sort=-%mem` on Linux/macOS, Task Manager on Windows); the number of `jcef_helper --type=renderer` processes should stay at or below `cefMaxRenderers` (plus a short transition window), not accumulate.
- Watch the log for `Renderer budget exhausted` / `WebView pool saturated` — these indicate you may want to raise `cefMaxRenderers` on a memory-rich host, or lower it on a constrained one.
- No manual process killing is needed on any platform: renderer processes are bounded by the budget and reaped automatically by the watchdog. If the process list ever looks wrong, the fix is to restart Rensaiō (a clean restart tears down CEF entirely), not to kill individual helper processes.

---

## 🤝 Contributing

### Frontend Devs ! You're Needed 🙏  
Help clean up the mess left behind by our overenthusiastic friends, Copilot, Claude and ~~Fable~~ (banned).

### Backend Devs ! PRs Welcome  

PRs are welcome to improve stability and architecture.

---

## 📄 License

Licensed under the [GNU General Public License v3.0](./LICENSE).

---

## 🏴‍☠️ Brace Yourself

This app *just works™*  until it doesn't. But it's here.
Start managing your series with the style it deserves.
