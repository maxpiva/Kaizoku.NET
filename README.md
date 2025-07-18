# Kaizoku.NET

**Kaizoku.NET** is a modern fork of the original **Kaizoku** and **Kaizoku Next Gen** by OAE,  built to fill the void and bring a streamlined manga series manager back to life.

This is a **feature-complete** application (not a preview). While it may contain bugs, it *definitely doesn’t contain spiders* — yet.

---

## 🎯 What It Does

Kaizoku.NET is a **series manager** that prioritizes simplicity, speed, and reliability, just like the original Kaizoku, but with powerful new features under the hood.

It uses the power of **Suwayomi Server** and **MIHON extensions** to connect with multiple sources.

---

## ✨ Key Features

- 🧙‍♂️ **Startup Wizard**  
  Automatically imports your existing library.

- 🔁 **Temporary vs Permanent Sources**  
  - Chapters are only downloaded from **temporary** sources.  
  - Auto-deleted if a **permanent** source later provides them.

- 🔎 **Multi-Search & Multi-Linking**  
  Search and link one series to **multiple sources/providers**.

- 📥 **Automatic Downloads, Retries, and Rescheduling**

- 🔄 **Auto-Updates**  
  Extensions and sources are kept up to date.

- 🧹 **Filename Normalization**  
  Rebuild your library easily with consistent naming.

- 🧾 **ComicInfo.xml Injection**  
  Chapters include rich metadata from the original source.

- 🖼️ **Extras**  
  - `cover.jpg` per series  
  - `kaizoku.json` for full metadata mapping  
  - And much more...

---

## 🛠️ Under the Hood

Kaizoku.NET is composed of:

- **Frontend**: A beautiful UI forked from [Kaizoku Next by OAE](https://github.com/oae/kaizoku-next) (Next.js).
- **Backend**: A custom .NET engine managing schedules, downloads, and metadata.
- **Bridge**: Suwayomi Server (to access Mihon Android extensions).

> ❗ **Note:** Kaizoku.NET does **not** use Suwayomi Server’s built-in download or scheduling logic, only its extension bridge.

---

## ⚙️ Configuration Notes

- By default, **Suwayomi Server is embedded** and auto-launched by Kaizoku.NET.
- You **can expose Suwayomi’s port** (via Docker or in the Desktop App).
- You can also **use your own Suwayomi instance** by editing `appSettings.json` (after install).

> ⚠️ **Warning:** Suwayomi assigns internal IDs for series/chapters.  
> If you change servers, **you must reset Kaizoku.NET** by deleting `kaizoku.db`, as ID mappings will no longer match.

---

## 🤔 Why Suwayomi Server?

Only the **MIHON** extensions are actively maintained and they’re Android-based APKs.  
Suwayomi provides a working **Java bridge** for those. Other options (e.g., IKVM) were avoided due to complexity, Kotlin compatibility issues, and Java version mismatches.

---

## 🐳 Docker Support

- Available for both `amd64` and `arm64`.
- **Host networking mode is recommended** for optimal performance (especially during heavy parallel operations like searching multiple sources).

---

## 🖥️ Desktop App

- A **tray application** based on Avalonia is available in the [Releases](https://github.com/yourrepo/releases).
- Currently tested only on **Windows**  testers for Linux/macOS are welcome!

---

## 🧱 Build It Yourself

It should be straightforward to build.  
Documentation coming soon™ (once laziness subsides).

---

## ⚠️ Resource Usage

Be aware: **Kaizoku.NET** and **Suwayomi Server** can be **memory-intensive**, especially when managing large libraries or doing parallel searches.

---

## 🤝 Contributing

### Frontend Devs ! You're Needed 🙏  
Help clean up the mess left behind by our overenthusiastic friend, GitHub Copilot.

### Backend Devs ! PRs Welcome  
This was a **rushed 1-month project**. There are known race conditions and an import system that’s... let’s say *aggressively functional*.  
PRs are welcome to improve stability and architecture.

---

## 🏴‍☠️ Brace Yourself

This app *just works™*  until it doesn't. But it's here, it’s fast, it’s yours.  
Start managing your series with the style it deserves.
