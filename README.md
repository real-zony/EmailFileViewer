# EmailFileViewer

EmailFileViewer is a cross-platform desktop application for opening and reading `.eml` and `.msg` email files. Messages are parsed locally on your computer; nothing is uploaded unless you choose to.

The project is in early development. The Tauri shell and the UI are currently placeholders; the .NET parsing core is being built first.

## Architecture

- **`ui/`**: the front end, written in React, TypeScript and Vite.
- **`src-tauri/`**: the desktop shell, built with [Tauri 2](https://tauri.app) (Rust). It owns the window, native dialogs and file associations.
- **`core/`**: the email parsing library and the sidecar executable, written in .NET 10. The sidecar is published with NativeAOT and bundled with the app as a Tauri `externalBin`. The Rust side talks to it over stdio, so a parser crash cannot take the UI down.

## Prerequisites

| Tool | Version |
| --- | --- |
| [Rust](https://rustup.rs) (stable) | 1.90 or newer |
| [Node.js](https://nodejs.org) | 24.15+ (the UI scripts run `.ts` files directly); npm is the package manager |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 (see `global.json`) |
| Tauri CLI | 2.x |

Install the Tauri CLI once:

```sh
cargo install tauri-cli --version "^2" --locked
```

Make sure `~/.cargo/bin` is on your `PATH`, so that `cargo tauri` works.

### Platform dependencies

**macOS**

- Xcode or the Xcode Command Line Tools (`xcode-select --install`). They provide the linker and clang that NativeAOT needs.

**Windows**

- Visual Studio 2022 Build Tools with the "Desktop development with C++" workload (MSVC and the Windows SDK). Rust's MSVC toolchain and NativeAOT both need it.
- WebView2 runtime (already installed on current Windows 10 and 11).

**Linux** (Debian and Ubuntu package names; CI builds on Ubuntu 22.04)

```sh
sudo apt-get install -y \
  build-essential curl wget file pkg-config \
  libwebkit2gtk-4.1-dev libxdo-dev libssl-dev \
  libayatana-appindicator3-dev librsvg2-dev \
  clang zlib1g-dev
```

The first group is what Tauri needs (see the [Tauri prerequisites](https://v2.tauri.app/start/prerequisites/) for other distributions). `clang` and `zlib1g-dev` are needed for NativeAOT.

## Getting started

```sh
git clone https://github.com/real-zony/EmailFileViewer.git
cd EmailFileViewer

# Install front-end dependencies
npm --prefix ui ci

# Start the app in development mode (run from the repository root)
cargo tauri dev
```

`cargo tauri dev` does the following:

1. Runs `scripts/build-sidecar.mjs`, which publishes the .NET sidecar with NativeAOT and copies it to `src-tauri/binaries/`.
2. Starts the Vite dev server on `http://localhost:1420`.
3. Compiles the Rust shell and opens the window.

The first run is slow because it compiles all Rust dependencies and runs the NativeAOT compiler. Later runs are much faster.

The sidecar is not rebuilt while `cargo tauri dev` is running. After changing code under `core/`, restart the command.

## Building

```sh
# Release build and installers for the current platform
cargo tauri build

# macOS only: just the .app bundle
cargo tauri build --bundles app
```

`cargo tauri build` also runs `scripts/build-sidecar.mjs` first. Output goes to `src-tauri/target/release/bundle/`.

## Working on individual parts

**Parsing core**

```sh
dotnet test core -c Release
```

**Sidecar**

```sh
# Publish for the current platform and copy it to src-tauri/binaries/efv-sidecar-<target-triple>[.exe]
node scripts/build-sidecar.mjs

# Run the smoke test against the sample emails
./src-tauri/binaries/efv-sidecar-<target-triple> --smoke samples
```

The script picks the target triple from its first argument (or `--target <triple>`), then from the `TAURI_ENV_TARGET_TRIPLE` environment variable (set by the Tauri CLI), then from `rustc -vV`. Supported triples:

| Target triple | .NET runtime identifier |
| --- | --- |
| `aarch64-apple-darwin` | `osx-arm64` |
| `x86_64-apple-darwin` | `osx-x64` |
| `x86_64-pc-windows-msvc` | `win-x64` |
| `x86_64-unknown-linux-gnu` | `linux-x64` |
| `universal-apple-darwin` | publishes both macOS architectures and merges them with `lipo` (macOS only) |

NativeAOT cannot cross-compile between operating systems, so build on the platform you target.

`src-tauri/binaries/` is generated and git-ignored. Tauri's build script requires the sidecar file to exist, so run `node scripts/build-sidecar.mjs` once before running plain `cargo check` or `cargo test` inside `src-tauri/`. The `cargo tauri` commands do this for you.

**Front end**

```sh
npm --prefix ui run build   # type-check and build to ui/dist
npm --prefix ui run lint
```

## Repository layout

```
.
├── ui/                  React + TypeScript + Vite front end
├── src-tauri/           Tauri 2 shell (Rust)
│   ├── binaries/        sidecar executables (generated, not committed)
│   ├── capabilities/    Tauri permission sets
│   └── tauri.conf.json  app, window and bundle configuration
├── core/                .NET solution
│   ├── src/EmailFileViewer.Core/      parsing library
│   ├── src/EmailFileViewer.Sidecar/   sidecar executable (NativeAOT)
│   └── tests/EmailFileViewer.Core.Tests/
├── scripts/
│   └── build-sidecar.mjs  publishes the sidecar into src-tauri/binaries/
├── samples/             synthetic and redistributable test emails, and their generator
├── docs/                design documents and the UI draft
├── openspec/            specifications and change proposals
└── .github/workflows/   CI
```

## License

[MIT](LICENSE)
