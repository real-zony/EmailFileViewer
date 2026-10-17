# Releasing EmailFileViewer

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](../.github/workflows/release.yml)) when a tag that starts with `v` is pushed. The workflow builds the installers on four runners, uploads them to a **draft** GitHub Release, and adds a `SHA256SUMS.txt` file. Nothing is published until you publish the draft yourself.

## What a release contains

| Platform | Runner | Target | Files |
| --- | --- | --- | --- |
| Windows 10 1809+ / 11, x64 | `windows-latest` | `x86_64-pc-windows-msvc` | `EmailFileViewer_<version>_x64-setup.exe` (NSIS) |
| macOS 15+, Apple Silicon | `macos-latest` | `aarch64-apple-darwin` | `EmailFileViewer_<version>_aarch64.dmg` |
| macOS 15+, Intel | `macos-15-intel` | `x86_64-apple-darwin` | `EmailFileViewer_<version>_x64.dmg` |
| Linux x64 | `ubuntu-22.04` | `x86_64-unknown-linux-gnu` | `EmailFileViewer_<version>_amd64.AppImage`, `EmailFileViewer_<version>_amd64.deb` |
| All | | | `SHA256SUMS.txt` |

These are exactly the files the workflow uploads (checked against the `v0.0.1-test` run): five installers plus `SHA256SUMS.txt`. There is no `.app.tar.gz`, `.sig` or `latest.json`, because no updater is configured (`uploadUpdaterJson` and `uploadUpdaterSignatures` are off and `createUpdaterArtifacts` is not set). If you ever turn the updater on, the macOS jobs also upload `EmailFileViewer.app.tar.gz` and its `.sig`; add them to this table and to the `checksums` job's check list in the workflow.

Notes:

- Linux is built on Ubuntu 22.04 on purpose. A program built against an older glibc runs on more distributions.
- The Windows installer installs the WebView2 runtime when it is missing. The Tauri WebView2 bootstrapper is embedded in the installer (`embedBootstrapper`), so the installer does not have to download the bootstrapper itself; the bootstrapper then needs an internet connection to fetch the runtime. On a Windows 10 machine that has neither WebView2 nor network access the installation fails. Embedding the full offline installer instead (`offlineInstaller`) would add about 127 MB.
- The `.deb` declares `libwebkit2gtk-4.1-0` and `libgtk-3-0` as dependencies, so `apt` installs them. The Tauri CLI adds these two automatically when it builds on Linux. The bundled .NET parser (a NativeAOT executable) links only against the C library.
- On Linux, the `.desktop` file (for both the `.deb` and the AppImage) comes from `src-tauri/linux/main.desktop`. It differs from Tauri's default template only by `Exec=... %F`: without it, the desktop environment starts the app without the file you double-clicked, so the `.eml` / `.msg` file association would not open anything.
- The installers contain no debug symbols (no `.dSYM`, `.pdb` or `.dbg` files; the release profile strips the Rust binary).
- Each platform job runs the parser tests (`dotnet test core`) and the sidecar smoke test before it builds the installer. A failure stops that platform's upload.
- The smoke test runs a second time after the installer is built, on the sidecar that went into the installer. Tauri's `beforeBuildCommand` publishes the sidecar again, so the file the first smoke test ran on is not the one that gets packaged. On Windows (NSIS) and Linux (deb, AppImage) the packaged copy is the file at `src-tauri/binaries/efv-sidecar-<target>`, which does not change after the build. On macOS the step runs the copy inside `EmailFileViewer.app/Contents/MacOS` (the signed one) and also runs `codesign --verify --deep --strict` on the app. This step runs after the upload, so when it fails the `checksums` job does not run: do not publish that draft.
- The license is MIT. It is declared in `Cargo.toml` and, explicitly, as `bundle.license` in `tauri.conf.json`. The Tauri bundler currently reads that field only for RPM packages, which this project does not build, so none of the five installers shows it; the NSIS installer would show a license page only if `bundle.licenseFile` were set.
- The macOS app declares `.eml` through its UTI (`com.apple.mail.email`, `LSItemContentTypes`) as well as by extension, so Finder's *Open With* lists the app for both `.eml` and `.msg` files. `.msg` has no system UTI; it is matched by extension.

## Release steps

1. **Bump the version** and commit it to `main`:
   - `version` in `src-tauri/tauri.conf.json`. The installers, the app's About dialog and the file names take their version from here.
   - `version` in `ui/package.json`.
   - Optional but recommended: `version` in `src-tauri/Cargo.toml` (run `cargo check` in `src-tauri/` afterwards so `Cargo.lock` follows), so the three files do not drift apart.

2. **Tag the commit and push the tag.** The tag is `v` plus the version:

   ```sh
   git tag v0.1.0
   git push origin v0.1.0
   ```

   A tag with a hyphen (`v0.1.0-rc.1`, `v0.0.1-test`) makes the draft a *pre-release*, so it never becomes the "latest" release. If the tag does not match the version in `tauri.conf.json`, the workflow prints a warning and carries on; the installers are still named after `tauri.conf.json`.

3. **Wait for the workflow** (Actions tab, workflow `release`). It runs these jobs:
   1. `create-release` creates the draft Release (or reuses an existing draft for the same tag).
   2. `build (...)`, four jobs in parallel, one per target. Each uploads its installers to the draft. One failing job does not stop the others; use *Re-run failed jobs* to fill in the missing files.
   3. `checksums` runs after all four succeed. It downloads every installer from the draft, checks that all five installers are there, and uploads `SHA256SUMS.txt`. The checksums are also shown in the job summary.

4. **Check the draft** on the Releases page:
   - The five installers and `SHA256SUMS.txt` are attached.
   - Edit the release notes (they are pre-filled with GitHub's generated notes).
   - Download an installer for the platform you have at hand and make sure it opens an `.eml` and a `.msg` file. [Verify its SHA-256](#verifying-sha-256) while you are at it.

5. **Publish** the draft. The tag already exists, so publishing only changes the release from draft to published.

If a release went wrong before publishing, delete the draft on the Releases page, delete the tag (`git push origin --delete v0.1.0` and `git tag -d v0.1.0`), fix the problem, and tag again.

### Trying the pipeline

Push a throwaway tag such as `v0.0.1-test`. You get a pre-release draft with installers named after the current `tauri.conf.json` version. Delete the draft and the tag afterwards.

## Secrets

Secrets are configured under *Settings → Secrets and variables → Actions → New repository secret*.

**None of them are needed until the signing tasks (9.4 for macOS, 9.5 for Windows).** Without them the workflow still succeeds:

- **macOS** builds are **ad-hoc signed** (the workflow sets `APPLE_SIGNING_IDENTITY=-` when `APPLE_CERTIFICATE` and `APPLE_SIGNING_IDENTITY` are both empty). Tauri signs the sidecar, the main executable and the whole `.app`, and the workflow verifies the result with `codesign --verify --deep --strict`. An ad-hoc signature identifies no developer, so Gatekeeper still blocks the first launch of a downloaded copy; see [Opening an unsigned macOS build](#opening-an-unsigned-macos-build). A build whose `.app` has no valid signature at all is worse: a downloaded copy is commonly reported on Apple Silicon as "damaged and can't be opened", and *Privacy & Security* offers no *Open Anyway* for it.
- **Windows** builds are **unsigned**: SmartScreen shows "Unknown publisher".

Such builds are fine for testing the pipeline and for hand-picked testers, but must not be published as a real release.

Only secrets that are set are passed to the build. (The Tauri CLI treats a variable that is set but empty as a real value and fails on it, so a step in the `build` job exports only the non-empty ones.)

| Secret | Used for | Needed from |
| --- | --- | --- |
| `APPLE_CERTIFICATE` | The *Developer ID Application* certificate with its private key, exported as a `.p12` file and base64-encoded (`base64 -i certificate.p12 \| pbcopy`). Tauri imports it into a temporary keychain on the runner. | 9.4 |
| `APPLE_CERTIFICATE_PASSWORD` | The password you set when exporting the `.p12` file. | 9.4 |
| `APPLE_SIGNING_IDENTITY` | The certificate's identity, for example `Developer ID Application: Your Name (TEAMID)`. `security find-identity -v -p codesigning` lists it. | 9.4 |
| `APPLE_ID` | The Apple ID (email) used for notarization. | 9.4 |
| `APPLE_PASSWORD` | An app-specific password for that Apple ID (created at appleid.apple.com), not the account password. | 9.4 |
| `APPLE_TEAM_ID` | The 10-character team ID from the Apple Developer account. | 9.4 |
| `WINDOWS_CERTIFICATE` | Reserved for Windows code signing: the code signing certificate as a base64-encoded `.pfx` file. The final set of Windows secrets depends on the signing service chosen in 9.3 (Certum or SignPath), so this name may change. | 9.5 |
| `WINDOWS_CERTIFICATE_PASSWORD` | Reserved: the password of the `.pfx` file. | 9.5 |

The workflow only forwards these values to the build. Actually signing also needs changes that belong to 9.4 and 9.5: the signing and notarization checks (`codesign -dv`, `spctl -a -vv`) on macOS, and the signing configuration (`certificateThumbprint` or `signCommand` under `bundle.windows` in `tauri.conf.json`) on Windows. `GITHUB_TOKEN` is provided by GitHub and needs no setup; the workflow asks for `contents: write` only.

## Opening an unsigned macOS build

This applies to builds made without the Apple secrets (ad-hoc signed, not notarized). Once the Developer ID signing and notarization of task 9.4 are in place, none of this is needed.

1. Open the `.dmg` and drag `EmailFileViewer` into `Applications`.
2. Double-click the app. macOS says it cannot verify that the app is free of malware. Click **Done** (do not move it to the Bin).
3. Open **System Settings → Privacy & Security**, scroll down to the *Security* section, and click **Open Anyway** next to "EmailFileViewer was blocked to protect your Mac". Confirm with your password or Touch ID, then click **Open**. This is needed only once.

On macOS 15 and later, Control-click → *Open* no longer gets past this prompt; use *Privacy & Security* as above. Without the settings page, this Terminal command removes the quarantine flag that the browser put on the download:

```sh
xattr -dr com.apple.quarantine /Applications/EmailFileViewer.app
```

If macOS says the app is **damaged**, the build was not signed at all (for example it was built outside the workflow); the steps above do not help in that case.

## Verifying SHA-256

Download the installer and `SHA256SUMS.txt` from the release page into the same folder. Each line of `SHA256SUMS.txt` is `<hash>  <file name>`.

Linux:

```sh
sha256sum --check --ignore-missing SHA256SUMS.txt
```

macOS:

```sh
shasum -a 256 --check --ignore-missing SHA256SUMS.txt
```

On an older macOS whose `shasum` has no `--ignore-missing`, compare the one file you downloaded: `shasum -a 256 EmailFileViewer_0.1.0_aarch64.dmg`, and look for the same hash in `SHA256SUMS.txt`.

Windows (PowerShell). The hash is printed in upper case; the file uses lower case, so compare without regard to case:

```powershell
Get-FileHash .\EmailFileViewer_0.1.0_x64-setup.exe -Algorithm SHA256
Select-String -Path .\SHA256SUMS.txt -Pattern 'x64-setup.exe'
```

An `OK` from `sha256sum` or `shasum`, or two identical hashes in PowerShell, means the file is exactly what the workflow built from the tagged commit.

## Maintenance notes

- GitHub plans to retire the `macos-15-intel` runner in autumn 2027. Then switch the Intel build to `macos-26-intel`, or cross-compile it on an Apple Silicon runner (the sidecar script already supports `x86_64-apple-darwin` and `universal-apple-darwin` on macOS).
- The `macos-latest` and `macos-15-intel` jobs are the only ones that sign. When the Developer ID certificate is configured (9.4), the same step still verifies the signature; add the notarization checks (`spctl -a -vv`) there.
- The workflow installs the Tauri CLI as a prebuilt binary (`cargo binstall tauri-cli`, any 2.x release) and runs it through `tauri-apps/tauri-action@v1` with `tauriScript: cargo tauri`, the same CLI as local development.
