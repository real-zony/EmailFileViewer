#!/usr/bin/env node
// Builds the .NET sidecar (NativeAOT) and places it where Tauri's `externalBin`
// expects it: src-tauri/binaries/efv-sidecar-<target-triple>[.exe]
//
// Usage:
//   node scripts/build-sidecar.mjs [<target-triple>]
//   node scripts/build-sidecar.mjs --target <target-triple>
//
// The target triple is taken from (in order): the command line, the
// TAURI_ENV_TARGET_TRIPLE environment variable (set by the Tauri CLI), or the
// host triple reported by `rustc -vV`.
//
// `universal-apple-darwin` publishes osx-arm64 and osx-x64 separately and merges
// them with `lipo`. The two per-architecture binaries are kept next to the
// universal one, because Tauri compiles each architecture separately and checks
// that the external binary exists for each of them.
//
// Cross-OS builds are not supported: NativeAOT can only target the OS it runs on.

import { spawnSync } from 'node:child_process'
import { chmodSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const projectPath = join('core', 'src', 'EmailFileViewer.Sidecar')
const assemblyName = 'EmailFileViewer.Sidecar'
const outputBaseName = 'efv-sidecar'
const binariesDir = join(repoRoot, 'src-tauri', 'binaries')

const TRIPLE_TO_RID = {
  'aarch64-apple-darwin': 'osx-arm64',
  'x86_64-apple-darwin': 'osx-x64',
  'x86_64-pc-windows-msvc': 'win-x64',
  'x86_64-unknown-linux-gnu': 'linux-x64',
}
const UNIVERSAL_TRIPLE = 'universal-apple-darwin'
const UNIVERSAL_PARTS = ['aarch64-apple-darwin', 'x86_64-apple-darwin']

function fail(message) {
  console.error(`build-sidecar: ${message}`)
  process.exit(1)
}

function run(command, args, options = {}) {
  console.log(`> ${command} ${args.join(' ')}`)
  const result = spawnSync(command, args, { cwd: repoRoot, stdio: 'inherit', ...options })
  if (result.error) {
    fail(`failed to start '${command}': ${result.error.message}`)
  }
  if (result.status !== 0) {
    fail(`'${command}' exited with code ${result.status ?? result.signal}`)
  }
}

function parseTargetArgument(argv) {
  const args = argv.slice(2)
  const flagIndex = args.indexOf('--target')
  if (flagIndex !== -1) {
    const value = args[flagIndex + 1]
    if (!value) fail('--target requires a value')
    return value
  }
  const positional = args.find((arg) => !arg.startsWith('-'))
  if (args.some((arg) => arg.startsWith('-')) && !positional) {
    fail(`unknown option: ${args.find((arg) => arg.startsWith('-'))}`)
  }
  return positional
}

function hostTriple() {
  const result = spawnSync('rustc', ['-vV'], { encoding: 'utf8' })
  if (result.error || result.status !== 0) {
    fail('could not determine the target triple: pass one explicitly, set TAURI_ENV_TARGET_TRIPLE, or install Rust (rustc)')
  }
  const match = /^host:\s*(\S+)\s*$/m.exec(result.stdout)
  if (!match) fail('could not find the host triple in `rustc -vV` output')
  return match[1]
}

function executableName(triple) {
  return triple.includes('-windows-') ? `${outputBaseName}-${triple}.exe` : `${outputBaseName}-${triple}`
}

// Publishes the sidecar for one target triple and copies only the executable
// (no .dSYM / .pdb / .dbg debug symbols) into src-tauri/binaries.
function publish(triple) {
  const rid = TRIPLE_TO_RID[triple]
  if (!rid) {
    fail(`unsupported target triple '${triple}'. Supported: ${[...Object.keys(TRIPLE_TO_RID), UNIVERSAL_TRIPLE].join(', ')}`)
  }

  const publishDir = mkdtempSync(join(tmpdir(), `efv-sidecar-${rid}-`))
  try {
    run('dotnet', ['publish', projectPath, '-c', 'Release', '-r', rid, '-o', publishDir])

    const sourceName = rid.startsWith('win-') ? `${assemblyName}.exe` : assemblyName
    const source = join(publishDir, sourceName)
    if (!existsSync(source)) fail(`publish did not produce '${sourceName}' in ${publishDir}`)

    mkdirSync(binariesDir, { recursive: true })
    const destination = join(binariesDir, executableName(triple))
    copyFileSync(source, destination)
    chmodSync(destination, 0o755)
    console.log(`build-sidecar: wrote ${destination}`)
    return destination
  } finally {
    rmSync(publishDir, { recursive: true, force: true })
  }
}

function buildUniversal() {
  if (process.platform !== 'darwin') fail(`${UNIVERSAL_TRIPLE} can only be built on macOS (needs lipo)`)
  const inputs = UNIVERSAL_PARTS.map(publish)
  const destination = join(binariesDir, executableName(UNIVERSAL_TRIPLE))
  rmSync(destination, { force: true })
  run('lipo', ['-create', ...inputs, '-output', destination])
  chmodSync(destination, 0o755)
  console.log(`build-sidecar: wrote ${destination}`)
}

const triple = parseTargetArgument(process.argv) || process.env.TAURI_ENV_TARGET_TRIPLE || hostTriple()
console.log(`build-sidecar: target triple ${triple}`)

if (triple === UNIVERSAL_TRIPLE) {
  buildUniversal()
} else {
  publish(triple)
}
