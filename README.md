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
  Create separate accounts with different permission levels. Invite people, and control who has access to what. Optionally enable authentication to restrict access to authorized users only.

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

- 🧾 **ComicInfo.xml Injection**  
  Chapters include rich metadata from the original source.

- 🖼️ **Extras**
  - Stores `cover.jpg` per series
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
