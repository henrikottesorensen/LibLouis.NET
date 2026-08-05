# Packaging and build

How this repository turns an upstream liblouis release into NuGet packages, and how the right
native binary reaches a consumer's application at run time.

## Packages

| Package | Contents |
| --- | --- |
| `LibLouis.NET` | The managed wrapper. Depends on `runtime.liblouis`. |
| `LibLouis.NET.Tables` | The braille tables from the upstream release, copied to the consumer's output directory. |
| `runtime.liblouis` | Metapackage. No payload; depends on all eight per-RID packages. |
| `runtime.<rid>.liblouis` | One native liblouis binary for a single runtime identifier. |

Supported runtime identifiers: `linux-x86`, `linux-x64`, `linux-arm64`, `win-x86`, `win-x64`,
`win-arm64`, `osx-x64`, `osx-arm64`.

The runtime packages are versioned after the upstream liblouis release; `LibLouis.NET` has its own
version. Both come from `Directory.Build.props`.

## How the native binary gets selected

This is the part that is poorly documented upstream, so it is worth stating plainly. There are two
different mechanisms and they are easy to confuse.

### What this repository uses: the `runtimes/<rid>/native/` convention

Each per-RID package contains exactly one payload file:

```
runtimes/win-x64/native/liblouis.dll
```

When a project references such a package, the .NET SDK records every one of these assets in the
application's `.deps.json` under `runtimeTargets`, tagged with its RID:

```json
"runtime.win-x64.liblouis/3.33.0": {
  "runtimeTargets": {
    "runtimes/win-x64/native/liblouis.dll": { "rid": "win-x64", "assetType": "native" }
  }
}
```

At startup the host reads `.deps.json`, picks the entries whose RID is compatible with the machine
it is running on, and adds those directories to `NATIVE_DLL_SEARCH_DIRECTORIES`. That is the list
`[LibraryImport("liblouis")]` probes, so the correct binary is found without any code in the
wrapper. `dotnet publish -r <rid>` resolves the same graph at publish time and copies only the one
matching binary.

Because the metapackage's dependencies are unconditional, a consumer restores all eight per-RID
packages regardless of platform, and a RID-less `dotnet build` copies all eight into
`bin/.../runtimes/`. Only the matching one is ever loaded, and publishing with a RID narrows it to
one. This is the same approach SkiaSharp and SQLitePCLRaw take.

### What this repository does not use: `runtime.json`

A package can also ship a root `runtime.json` declaring RID-conditional dependencies — "when
restoring for `win-x64`, also depend on `runtime.win-x64.liblouis`". That would let a consumer
download only the native package they actually need.

It was dropped because NuGet only evaluates `runtime.json` when a RID is in scope for the restore,
meaning `RuntimeIdentifier`/`RuntimeIdentifiers` is set or `dotnet publish -r` is used. In .NET 5
and later, portable RID-less builds are the default and `UseRidGraph` is off, so an ordinary
`dotnet build` / `dotnet run` would resolve no native package at all and fail at the first P/Invoke.
The mechanism dates from .NET Core 1.x/2.x, when every application had a RID.

To switch back, delete the dependency group from `runtime.liblouis.nuspec` and add a `runtime.json`
to the metapackage — nothing else in the repository depends on the choice.

## Building

`Directory.Build.props` is the single source of truth for the upstream version and its checksum;
the shell scripts read the values back out of it.

| Command | Produces |
| --- | --- |
| `./build.sh` | Linux + Windows runtime packages and the metapackage, via the container. |
| `sh build/build_runtime_macos_packages.sh` | macOS runtime packages. Requires a Mac. |
| `sh build/build_metapackage.sh` | `runtime.liblouis`. Needs no native binaries. |
| `sh build/build_managed_packages.sh` | `LibLouis.NET` and `LibLouis.NET.Tables`. |
| `sh build/stage_tables.sh` | Copies upstream tables into `LibLouis.NET.Tables`. |

Everything lands in `./packages`, or in `$PACKAGE_OUTPUT_DIR` if set.

Building the managed packages resolves `runtime.liblouis`, so the runtime packages have to be
available first. Point `NUGET_LOCAL_FEED` at a directory of freshly built packages to build against
those instead of what is already published:

```sh
NUGET_LOCAL_FEED=$PWD/packages sh build/build_managed_packages.sh
```

### Cross-compilation

Linux and Windows binaries are cross-compiled in the container defined by `Dockerfile`. The
toolchain package names there only exist for amd64, so the image is pinned to `linux/amd64` and
runs under emulation on Apple Silicon.

`win-arm64` is the exception: Ubuntu has no aarch64 mingw-w64 gcc, so the image installs the
[llvm-mingw](https://github.com/mstorsjo/llvm-mingw) toolchain, pinned by SHA-256. It provides
`aarch64-w64-mingw32-gcc` driver wrappers, so the build script treats it like the other Windows
targets. Bump `LLVM_MINGW_VERSION` and `LLVM_MINGW_SHA256` together.

llvm-mingw lives in its own container stage, and that separation is load bearing. It also ships
`i686-w64-mingw32-gcc` and `x86_64-w64-mingw32-gcc`, so merely having it on `PATH` alongside the
Ubuntu cross compilers silently takes over the `win-x86` and `win-x64` builds. That is not
hypothetical: it happened, and it broke `win-x86`, because clang treats the
`-Wincompatible-pointer-types` that gnulib trips on mingw as an error where gcc only warns.

Two upstream assumptions bite the ARM64 Windows build, both because liblouis takes `*mingw*` to
mean x86:

- `configure.ac` adds `-Wl,--add-stdcall-alias` for every mingw host. It decorates i386 stdcall
  symbols, means nothing on ARM64, and lld rejects unknown arguments where GNU ld ignored them.
  `build_runtime_packages.sh` strips the line from `configure` for this target only.
- gnulib's `metadata.c` assigns a `DIR *` to a `struct gl_directory *`. clang rejects it, so that
  one diagnostic is turned back into a warning through `CFLAGS`.

Both were still present in liblouis 3.38.0, so expect them to survive an upstream bump.

### Self-contained Windows binaries

The Windows targets are built with `-static-libgcc`. Without it, 32-bit mingw links against
`libgcc_s_dw2-1.dll`, because it uses DWARF-2 exception handling whose unwinder lives in that DLL.
That is a load time dependency, so `liblouis.dll` would fail to load on any machine without a mingw
installation, while working fine on a developer box that has one. 64-bit mingw uses SEH and never
had the problem, which is why it survived unnoticed: `win-x64` is the target anyone would test.

Worth re-checking after a toolchain or upstream bump. The binaries should import nothing beyond
`KERNEL32`, `msvcrt` and the `api-ms-win-crt-*` system libraries:

```bash
llvm-readobj --coff-imports liblouis.dll | grep Name:
```

### Post-build verification

Every native binary is inspected before it is packed, by `build/verify_native_binary.sh`, called
from `pack_runtime_package`. A binary that fails never becomes a package.

| Check | Why |
| --- | --- |
| Architecture matches the RID | A mis-targeted binary restores fine and never loads. |
| Every P/Invoke symbol is exported | Otherwise `EntryPointNotFoundException` at first use. |
| No dependency outside an allowlist | Catches the `libgcc_s_dw2-1.dll` class of bug. |
| Max `GLIBC_` version within the floor | The floor decides which distributions can consume the packages, and it is a property of the build image. |

The expected symbols are read out of the `EntryPoint` attributes in `LibLouis.NET/NativeMethod.cs`
rather than listed in the script, so the check cannot drift away from what the wrapper actually
imports.

The glibc floor is `MAX_GLIBC` in the script, currently 2.34, which covers RHEL 9, Debian 12 and
Ubuntu 22.04 and later. `linux-x86` sits exactly on it. Raising it drops support for older
distributions, so it should be a deliberate decision rather than a side effect of bumping the base
image.

Run it by hand against an extracted package to audit a published one:

```bash
sh build/verify_native_binary.sh win-x64 runtimes/win-x64/native/liblouis.dll
```

It needs tools that can read the format being checked. The container has them. On macOS,
`brew install llvm` covers all three formats, since llvm-readelf, llvm-nm and llvm-readobj read ELF,
PE and Mach-O alike; the script finds them under Homebrew's keg-only prefix. Without that, only the
macOS RIDs can be checked locally, because BSD nm has no -D and cannot read ELF.

`SKIP_NATIVE_VERIFICATION=1` bypasses it, which is only reasonable when deliberately building
something the checks were not written for.

### Parallel make

The Linux and macOS targets build with `make -j`. The Windows targets deliberately do not.

`liblouis/Makefile` makes `all-am` depend on `liblouis-<soversion>.def`, a file with no rule of its
own: it exists only as a side effect of linking `liblouis.la` with `-Wl,--output-def`. Under `-j`,
make is free to schedule the `.def` before that link and fails with `No rule to make target`. The
`.def` is only produced for Windows hosts, so ELF and Mach-O targets are unaffected. The failure is
a scheduling race, so it is intermittent rather than reliable, which is reason enough not to gamble
on it in CI.

Both macOS slices are built on whichever architecture the Mac happens to be, using `clang -arch`
plus an explicit `--host`. Passing `--host` also puts autoconf into cross-compilation mode so
`configure` does not try to execute test binaries for the foreign slice. The build verifies the
resulting architecture with `file` before packing.

## CI

`.github/workflows/build.yml`:

| Job | Runner | Does |
| --- | --- | --- |
| `native-linux` | `ubuntu-latest` | Container build: 6 runtime packages + metapackage. |
| `native-macos` | `macos-latest` | The 2 macOS runtime packages. |
| `test` | ubuntu, windows, macOS | Restores from the built packages and runs the tests. |
| `managed` | `ubuntu-latest` | `LibLouis.NET` and `LibLouis.NET.Tables`. |
| `publish` | `ubuntu-latest` | Pushes to GitHub Packages. Tags matching `v*` only. |

`managed` is a separate job from `native-linux` because building `LibLouis.NET` resolves
`runtime.liblouis`, which depends on the macOS packages the container cannot produce. Jobs exchange
packages as workflow artifacts, and the consuming jobs assemble them into a local feed.

The `test` job is the end-to-end check on native selection. Restoring pulls all eight per-RID
packages onto each runner, and each runner then has to resolve the one matching binary before any
P/Invoke succeeds — so a mistake in the package layout or the metapackage dependencies fails here
rather than in a consumer's application.

Publishing is gated on a `v*` tag, so pushes and pull requests build and test without reaching the
feed. A tag does not map to a single package version, since the runtime and managed packages are
versioned independently; `--skip-duplicate` makes re-pushing an unchanged runtime package a no-op.
