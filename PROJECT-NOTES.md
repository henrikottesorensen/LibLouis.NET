# LibLouis.NET — state of play

Written 2026-08-06. Two sessions have been working this repository in parallel: a packaging and CI
strand, and a P/Invoke audit strand. This is the state of both.

---

## 1. Pull requests

Nine open, all green. The "skipping" check in each is the publish job, which is gated on a `v*` tag.

| PR | What | Depends on | CI |
| --- | --- | --- | --- |
| [#6](https://github.com/Notalib/LibLouis.NET/pull/6) | Verify native binaries before packing | — | 6 pass |
| [#7](https://github.com/Notalib/LibLouis.NET/pull/7) | Multi-target net8.0 and net10.0 | #6 | 6 pass |
| [#9](https://github.com/Notalib/LibLouis.NET/pull/9) | Memory, thread and lifecycle safety in the P/Invoke layer | — | 6 pass |
| [#10](https://github.com/Notalib/LibLouis.NET/pull/10) | Separate Nota's tables from the upstream set | #9 | 6 pass |
| [#11](https://github.com/Notalib/LibLouis.NET/pull/11) | Update liblouis to 3.38.0 | — | 6 pass |
| [#12](https://github.com/Notalib/LibLouis.NET/pull/12) | Build only the library, not the whole tree | — | 6 pass |
| [#13](https://github.com/Notalib/LibLouis.NET/pull/13) | Compile and pack on images chosen for the job | — | 6 pass |
| [#14](https://github.com/Notalib/LibLouis.NET/pull/14) | Convert the solution to slnx | #7 | 6 pass |
| [#15](https://github.com/Notalib/LibLouis.NET/pull/15) | Run the upstream braille specs against the wrapper | #10 | 6 pass |

Merged already: #5 (runtime package selection, win-arm64, GitHub Actions) and #8 (actions on Node 24).

### Merge order

Independent, land in any order: **#6, #9, #11, #12, #13**.

Stacked, land after their base: **#7** after #6, **#14** after #7, **#10** after #9, **#15** after #10.

### Conflicts to expect

- **#6 and #13 collide semantically, not textually.** #6 runs `verify_native_binary.sh` from inside
  `pack_runtime_package`; #13 moves packing to a stage that deliberately installs no tools, so it
  has no `readelf`, `nm` or `llvm-readobj`. Whichever merges second must resolve it. The better
  resolution is moving verification into the toolchain stages, where those tools already exist, so a
  binary is checked where it was made rather than after crossing a stage boundary.
- **#12 and #13** both touch `build/build_runtime_packages.sh`, in different regions. Should merge,
  worth a look.
- **Stacked branches need re-stacking whenever their base moves**, not only at merge time. This bit
  once already: an edit to #7 silently pulled in a commit from an older #6 tip.

---

## 2. Native API coverage

Counted from `LIBLOUIS_API` declarations in `liblouis.h.in` against `EntryPoint` values in
`NativeMethod.cs`.

| | 3.33.0 | 3.38.0 |
| --- | --- | --- |
| Public functions declared | 31 | 35 |
| Wrapped | 18 | 18 |
| Unwrapped | 13 | 17 |

Binary symbol counts differ (30 exported `lou_*` in 3.33, 34 in 3.38) because the library also
exports internal `_lou_*` helpers. Use the header, not `nm`, for coverage questions.

### What 3.38 adds — all deallocators

```
lou_freeEmphClasses  lou_freeTableFile  lou_freeTableFiles  lou_freeTableInfo
```

plus `lou_freeTableIndex` in the binary. **This is the main reason to take #11 beyond the security
fixes.** #9 deliberately leaks the string returned by `lou_findTable`: liblouis documents it as
caller-frees, but the Windows binaries are mingw-w64 allocating from `msvcrt.dll` while .NET frees
through `ucrtbase.dll`, and freeing across those heaps corrupts them. The new deallocators free
through liblouis's own allocator, so the mismatch disappears and the leak becomes properly fixable.

They also unblock four metadata functions that exist in 3.33 but would leak identically if wrapped
today: `lou_findTables`, `lou_listTables`, `lou_getTableInfo`, `lou_getEmphClasses`. Wrap them as a
set with their deallocators, not piecemeal.

### Worth wrapping regardless of version

- **`lou_translatePrehyphenated`** — `lou_translate` plus hyphen arrays. The consumer currently
  computes hyphenation separately with its own `HyphenationTrie` and line-breaks against the
  position arrays by hand. Probably the largest real win available.
- **`lou_registerTableResolver`** — supply tables from memory instead of the filesystem. Removes the
  relative-path fragility and the comma-in-path hazard, and is how tables would be embedded in the
  package rather than shipped as a directory. Not cheap: a callback returning allocated memory, so
  it needs the same delegate-rooting care that was one of the crashes in #9.
- **`lou_getTypeformForEmphClass`** — the `TypeForm` enum currently hard-codes that mapping.

### Not worth wrapping

`lou_logFile`, `lou_logPrint`, `lou_logEnd` write to a `FILE*` and the registered callback is
strictly better; `lou_free` calls `lou_logEnd` itself. `lou_getTable`, `lou_readCharFromFile` and
`lou_getProgramPath` are internal helpers.

### Caveat for the upgrade

The header marks a "sort-of private API" boundary, and `lou_findTable`, `lou_indexTables`,
`lou_free` and all the deallocators sit **below** it. Three are already depended on. Those contracts
are less stable across releases than the translate functions, and `lou_indexTables`' NULL-termination
requirement is exactly the sort of thing that could change quietly. Diff that header section between
3.33 and 3.38 as part of #11.

---

## 3. Test expansion

### Where it is

`feature/upstream-yaml-tests` → **#15**, stacked on #10. Runs liblouis's own braille specs through
the wrapper: three Danish specs, 10,524 cases, both directions, all passing.

`yaml-specs-all` → **#16, not yet opened.** Widens to the full corpus.

| | |
| --- | --- |
| Specs running | 142 of 142 |
| Passing entirely | 102 |
| With mismatches | 40 |
| Cases | ~212,000 |
| Crashes | none |

Excluded deliberately: the eight dictionary harnesses at 200,000+ cases each. They would dominate
every run for little extra signal; add them behind a switch if wanted.

### Why the reader is hand-written

The spec files are not YAML mappings and cannot be deserialised. One document repeats `table:`,
`flags:` and `tests:` at the same level. `lou_checkyaml` treats the file as an event stream where
each key mutates parser state, so the reader does the same over YamlDotNet's `IParser`.
Anything it does not model throws rather than being skipped.

### Format details that were got wrong, and how

Every one of these was found by the specs failing, not by reading:

1. **The backward leg of `bothDirections` swaps input and expected**; an explicit
   `testmode: backward` does not. Treating them alike failed ~2,800 cases.
2. **A display table can be an inline block scalar**, not a file name. It arrives as an ordinary
   scalar event, so it was read as a path. All 140 remaining Danish mismatches, from two spec lines.
3. **A translation table can be inline too** — same bug, one key over. `nemeth.yaml` 133/133 and
   `en-nabcc.yaml` 8/8.
4. **A test entry can lead with a description**: `[label, input, expected]`. Ten specs.
5. **A table can be named by file rather than by query**, so it must not go through `lou_findTable`.

The `NotSupportedException`-on-unknown-construct design caught unknown *keys* but not unknown
*shapes*, which is where all four of the above hid.

### Constructs recognised but not driven

Counted in `BrailleSpec.SkippedConstructs` rather than silently dropped, and each maps to wrapper
surface with no coverage:

| Construct | Occurrences | Maps to |
| --- | --- | --- |
| `inputPos` | 5,071 | position arrays |
| `outputPos` | 5,070 | position arrays |
| `typeform` | 522 | `TypeForm` |
| `cursorPos` | 19 | cursor handling |
| `mode` | 19 | `TranslationMode` |
| `testmode: display` | 3 | `DotsToCharacters` / `CharactersToDots` |
| `testmode: hyphenate` | 1 | `Hyphenate` |

Driving these would widen coverage *and* cover the untested API — the same job twice over.

### Open

- **40 specs have mismatches, uncharacterised.** `no.yaml` 167/868, `ru.yaml` 39/140,
  `de-g*-detailed-specs.yaml` 35/476 each. Expect a mix of further harness gaps and genuine
  differences. **Bisect with a cold process per variant** — see the compile cache note below.
- One spec fails to parse: a multi-line double-quoted scalar YamlDotNet rejects.

---

## 4. Outstanding issues

### Deep table compilation can overflow an ordinary thread stack — affects production

Compiling `ancient-languages-borger.utb` needs 640–768 KB of stack. More than a test host gives a
test, and **more than an ASP.NET request thread's ~1 MB leaves spare**. A stack overflow kills the
process and cannot be caught.

Established jointly with the audit session, after both of us drew wrong conclusions first:

- It is **compilation**, not translation. Once a table list is compiled, translating through it runs
  in 128 KB.
- **Nothing about the input matters.** ASCII overflows the same as non-BMP.
- **The display table is irrelevant.**
- liblouis caches compiled tables **process-wide**, keyed by table-list string, never evicted until
  `lou_free`. So whichever variant runs first in a process pays the compile cost and every later one
  reuses the cache. Both of our "isolating" experiments were measuring test order.

**Action for the consumer:** compile every table list at startup, on the main thread or a
large-stack thread, by translating a dummy string through each. AutoBraille's Danish tables are
presumably compiled early enough that this has never bitten, but it is one table-list change away
from mattering.

**In the harness:** each spec runs on a 64 MB thread. The audit session suggests warming all table
lists once in a fixture instead, which is closer to what a consumer should do and removes test-order
sensitivity. Either works.

**Upstream:** worth reporting. The case is simple — compiling that table needs ~700 KB of stack, no
unusual input. `lou_checkyaml` never sees it because it runs on the process main thread. Present on
both 3.33 and 3.38.

### `spacing` is marshalled wrong

An output buffer passed as an input string. liblouis writes to it; the result is computed into a
temporary and discarded, and because `inlen` includes the terminator it can write one byte past that
temporary. Harmless while callers pass `null`, which the specs and AutoBraille do. Not fixed in #9.

### Nota's tables are a fork, and drifting

22 of the 30 committed test tables shadow an upstream table and differ from it; 8 exist nowhere
upstream. They are a fork of an *older* upstream: they add what is needed (`emphclass foreign`,
behind `TypeForm.ForeignLanguage`) and lack what upstream has since fixed — the rules removing the
space between `§` and a following number, for one. `da-dk-g2.dic` diverges by 14,810 lines.

23 of the 30 are reachable from no test. They are a stale copy of what the application ships,
drifting independently of both upstream and production. #10 separates them into `nota-tables/` and
documents them; it deliberately does not decide what is canonical.

### Publishing has never run

The `publish` job is gated on a `v*` tag and no tag has been pushed. Everything else in the workflow
has now run for real, but the push to GitHub Packages has not.

### Untested by construction

`win-x86` and `win-arm64` have no runtime coverage anywhere — GitHub has no 32-bit or ARM64 Windows
runners. Their binaries are verified statically (architecture, exports, dependencies) but nothing has
loaded them and translated a string.

---

## 5. Backlog

Roughly in order of value.

1. **Characterise the 40 failing specs** and open #16. 102 specs of coverage are ready now.
2. **Fix the `lou_findTable` leak** once #11 lands, using `lou_freeTableFile`.
3. **Wrap `lou_translatePrehyphenated`** — replaces work the consumer does by hand.
4. **Drive the skipped spec constructs**, which covers `Hyphenate`, `DotsToCharacters`,
   `CharactersToDots`, the position arrays and `TranslationMode` at the same time.
5. **Report the compilation stack depth upstream.**
6. **Fix `spacing`**, or hard-code `null` and document why.
7. **Decide what is canonical** for the Danish tables, and whether the repository copy should exist.
8. **Wrap the metadata set** — `lou_findTables`, `lou_listTables`, `lou_getTableInfo`,
   `lou_getEmphClasses` — with their deallocators.
9. **`lou_registerTableResolver`**, if embedding tables in the package is wanted.
10. **Add the dictionary harnesses** behind a switch, if the extra 800,000 cases are wanted.

---

## 6. Things worth not relearning

- **liblouis caches compiled tables process-wide.** Any experiment that varies one factor and reruns
  in the same process is measuring cache state, not the factor. Use a cold process per variant.
- **`find -size -1M` matches only empty files.** Sizes round up to whole units. Use `-size -1000000c`.
- **Scraping xunit console output attributes messages to the wrong test.** Output interleaves. Use a
  structured logger, or a single test that collects and reports.
- **`exit` inside a function called through command substitution only kills the subshell.** This made
  a verification check pass against an empty symbol list while printing an error.
- **The Dockerfile is part of the build context**, so editing it invalidates every `COPY . /source`.
  It is excluded in #13.
- **`global.json` binds every `dotnet` invocation in the repository, including inside containers.**
  Adding one to satisfy `.slnx` broke the container build, which runs an older SDK and never loads
  the solution.
- **Cross-compiler apt packages only *Recommend* their target libc.** With `--no-install-recommends`
  you get a compiler that cannot link.
