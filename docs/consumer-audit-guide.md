# LibLouis.NET consumer audit

**For:** an agent with access to the codebases that *consume* the `LibLouis.NET` package.
**Not for:** the LibLouis.NET wrapper repository itself — that side is already done.

You are auditing **calling code**, not the wrapper. The wrapper has just had eleven
memory-safety and API fixes applied, some of which change behaviour silently. Your job is to
find out what that means for the callers, and to answer two design questions that cannot be
decided without seeing how the API is actually used.

## Ground rules for reporting

The person requesting this audit **cannot share source code** outside their environment.
Report **answers, counts, and classifications** — not code.

- ✅ "12 call sites; 3 read `InputPosition`; all slice by `Output.Length`"
- ✅ "`FindTable` is called once at startup, cached in a static field"
- ✅ File paths and line numbers, *if* local policy permits
- ❌ Pasting method bodies, business logic, table contents, or user data
- ❌ Quoting more than a single expression when a snippet is unavoidable

If a question genuinely cannot be answered without showing code, say so and describe the shape
of the code in words instead.

---

## 0. Context you need

**The wrapper source is readable** at
`https://github.com/henrikottesorensen/LibLouis.NET`, branch **`fix/pinvoke-audit`**
(based on PR #5's head, `e368487`). Read it when a question below turns on what the wrapper
actually does — `LibLouis.NET/LibLouis.cs` is the whole surface, and the eleven commit messages
on that branch explain each fix and the evidence behind it. The tests under
`LibLouis.NET.Test/` document the native contracts in prose.


`LibLouis.NET` wraps the native `liblouis` braille translation library via P/Invoke.

Three facts drive almost everything below:

1. **The singleton is process-global.** `LibLouis.Instance` wraps native state shared by the
   entire process. There is no per-caller instance.
2. **liblouis counts in "widechars", not .NET chars.** Every shipped native binary is built
   `--enable-ucs4`, so one widechar = one 4-byte Unicode *code point*. A .NET `string` counts
   UTF-16 code units. These agree for all BMP text and disagree for anything non-BMP —
   emoji, musical symbols, rare CJK extensions, some historic scripts. A non-BMP character is
   **1 widechar but 2 chars**.
3. **liblouis is not thread-safe.** The wrapper serialises every native call on one process-wide
   lock.

Point 2 is the source of most of the silent changes. Whenever you see a length, index, or array
size in caller code, the question is always: *is this counted in UTF-16 code units or code
points, and does the caller's data ever contain non-BMP characters?*

### Current public API

```csharp
public class LibLouis                          // NOTE: no longer IDisposable
{
    public static LibLouis Instance { get; }
    public static void Shutdown();             // NEW - replaces Dispose()

    public ILogger Logger { get; set; }
    public string Version { get; }
    public string? DataPath { get; set; }

    public string? FindTable(string query);
    public void IndexTables(IEnumerable<string> tables);

    public string DotsToCharacters(IEnumerable<string> tableList, string input);
    public string CharactersToDots(IEnumerable<string> tableList, string input);

    public string Translate(IEnumerable<string> tableList, string input, int outputLength,
                            TypeForm[]? formtype, string? spacing, TranslationMode mode);

    public TranslatedString Translate(IEnumerable<string> tableList, string input, int outputLength,
                                      TypeForm[]? formtype, string? spacing,
                                      int[] outputPosition, int[] inputPosition,
                                      int cursorPosition, TranslationMode mode);

    public string BackTranslate(...);          // same two shapes as Translate
    public TranslatedString BackTranslate(...);

    public string Hyphenate(IEnumerable<string> tableList, string input, TranslationMode mode);
}

public class TranslatedString
{
    public required string Output { get; set; }
    public required int[] OutputPosition { get; set; }
    public required int[] InputPosition { get; set; }
    public required int CursorPosition { get; set; }
    public bool[]? OutputDots78 { get; set; }  // NEW - see §2.1
}

public static class Logging
{
    public static void SetCallback(NativeMethods.LoggingCallback value);
    public static LogLevel LogLevel { get; set; }
    public static void DebugLogCallback(LogLevel level, string message);
}
```

---

## 1. Compile-breaking — must be fixed to build

### 1.1 `IDisposable` removed

`LibLouis` no longer implements `IDisposable`. `Dispose()` is replaced by the static
`LibLouis.Shutdown()`.

**Search:**
```
rg -n "using\s*\(\s*LibLouis|using\s+var\s+\w+\s*=\s*LibLouis|LibLouis\.Instance\s*\.\s*Dispose|\.Dispose\(\)" --type cs
rg -n "AddSingleton.*LibLouis|AddScoped.*LibLouis|AddTransient.*LibLouis" --type cs
```

Also check DI registrations, `IHostedService`/`IHostApplicationLifetime` shutdown hooks, and any
wrapper/facade class of their own that implements `IDisposable` and forwards to it.

**Expected error:** `CS1674: type used in a using statement must implement 'System.IDisposable'`

**Guidance for each hit:**
- A `using` statement or block over the singleton was **always a bug** — it tore liblouis down
  for the whole process on first use. Delete it; do not translate it into a `Shutdown()` call.
- An explicit `Dispose()` at genuine application shutdown → `LibLouis.Shutdown()`.
- Anything else → almost certainly just delete it. Normal applications should never call
  `Shutdown()`: liblouis caches compiled tables per *table list*, not per call, so nothing
  accumulates, and process exit reclaims everything anyway.

**Report:** count of each category, and whether any was inside a request/loop scope rather than
application shutdown (that would mean production was silently discarding the whole table cache,
repeatedly).

### 1.2 Nullability of `FindTable`

`FindTable` is now annotated `string?` (it always could return null; the annotation was missing).
In a project with `<Nullable>enable</Nullable>` this produces new warnings — errors if
`TreatWarningsAsErrors` is set.

**Search:** `rg -n "FindTable" --type cs`

**Report:** number of call sites, and whether any dereferences the result without a null check.

---

## 2. Silent behaviour changes — compile fine, behave differently

These are the dangerous ones. Nothing here fails to build.

### 2.1 `formtype` array is no longer overwritten  🔴 highest risk

**Before:** liblouis wrote back into the caller's `TypeForm[]`, one ASCII `'0'`/`'8'` per *output*
cell, indicating dots 7/8. Because the array is sized to the *input*, this also wrote past the end
of the managed array whenever the translation grew the text — corrupting the GC heap.

**Now:** the wrapper copies the caller's values into an internal buffer. The caller's array comes
back **untouched**. The write-back information is instead surfaced as
**`TranslatedString.OutputDots78`** — a `bool[]?` with one entry per *output cell*, `true` where
the cell contains dot 7 or dot 8. It is `null` when no `formtype` was passed (liblouis only
computes it when one is supplied), and it is populated by the **forward** `TranslatedString`
overload only — back-translation reports nothing, and the string-returning overloads have
nowhere to put it.

**Search:**
```
rg -n -B4 -A10 "Translate\(|BackTranslate\(" --type cs
```
For each call passing a non-null `formtype`, determine whether the array is **read after the
call**.

**Migration for callers that read the array afterwards:**
- Values previously read from `formtype[k]` were the ASCII characters `'0'`/`'8'` (cast into
  `TypeForm`), indexed by output cell but bounded by the input length — i.e. truncated or
  overrunning. Replace with `result.OutputDots78[k]`, which is correctly sized and typed.
- Callers using the *string-returning* overload who need this information must switch to the
  `TranslatedString` overload.

**Report:**
- How many call sites pass a non-null `formtype`.
- Whether **any** of them read the array afterwards. If yes, describe in words what the values
  were used for, and confirm `OutputDots78` covers that use.
- Whether any `TypeForm[]` is **reused** across calls (previously it would have been silently
  overwritten between iterations).

### 2.2 `Hyphenate` — previously non-functional

`Hyphenate` could never have worked: the output buffer was marshalled as `ref string`, which
passed a pointer-to-pointer, so liblouis wrote its results over the marshalling stub's stack.
Calling it corrupted memory and wedged the process.

Now it works, and:
- Returns **one flag per code point** (was: one per UTF-16 code unit, plus one extra).
- Throws `ArgumentException` for input ≥ 100 characters (liblouis's `HYPHSTRING` limit). Was:
  a generic `LibLouisException` about hyphenation failure.
- Throws `ArgumentException` for null/empty input. The old guard validated a string *literal*
  and could never fire.

**Search:** `rg -n "Hyphenate" --type cs`

**Report:** whether it is called at all (expected: no — it could not have worked). If it is
called, that code path was corrupting memory; flag it loudly and describe what it did with the
result.

### 2.3 Output length of `CharactersToDots` / `DotsToCharacters`

These passed `input.Length` (UTF-16 units) as a widechar count. For non-BMP input that both
overstated the buffer (reading past its end) and returned too many cells.

**Now:** one cell per code point.

**Concrete change:** `CharactersToDots(tables, "𝄞𝄞")` returned a **4**-character string; it now
returns **2**.

**Search:** `rg -n "CharactersToDots|DotsToCharacters" --type cs`

**Report:** call-site count, and whether any caller assumes `result.Length == input.Length`.
Combine with §3.3 (does non-BMP text reach this API at all?).

### 2.4 Position arrays are filled to the code-point count

`outputPosition` is now populated for **code-point count** entries, not `input.Length` entries.
For BMP-only text these are identical. For non-BMP input, entries beyond the code-point count are
left at whatever the caller initialised them to.

This is closely tied to the open question in §3.1 — answer that one and this follows.

### 2.5 Exception type after shutdown

Calling any translation method after `Shutdown()` throws `InvalidOperationException` with a
message containing "shut down". Previously the object silently kept working (lazily recompiling
the tables that had just been freed).

`ObjectDisposedException` derives from `InvalidOperationException`, so existing
`catch (InvalidOperationException)` still works; `catch (ObjectDisposedException)` will not.

**Search:** `rg -n "ObjectDisposedException" --type cs`

### 2.6 Log messages are now a structured template

Native messages were previously passed as the ILogger *message template*. They are now passed as
an argument under the property name `LiblouisMessage`, i.e. `_logger.Log(level,
"{LiblouisMessage}", message)`.

Why: a table path or rule containing `{` was being parsed as a template placeholder.

**Impact:** structured logging sinks (Seq, Splunk, App Insights, Elastic) will see the message
text under a named property, and the template itself is now the constant `"{LiblouisMessage}"`.
Any log-based alert, saved search, or dashboard that matches on the rendered message template
needs updating.

**Report:** whether any alerting or log query keys off liblouis log text, and where.

### 2.7 `DataPath` no longer aborts the process

Getting or setting `DataPath` used to abort the process outright — the marshaller freed a pointer
into liblouis's static storage, and the allocator killed the process
(`POINTER_BEING_FREED_WAS_NOT_ALLOCATED`). It now works.

**Report:** whether `DataPath` is used. If it is, that code path was fatal, so it presumably
never ran — check whether it sits behind a config flag or an unused branch, and whether enabling
it now changes table resolution.

---

## 3. Open design questions — the wrapper needs these answers

These are the reason the audit exists. Both are decisions that cannot be made without seeing real
usage.

### 3.1 Position array index units  🔴 primary question

**The problem.** `InputPosition` and `OutputPosition` are indexed by liblouis in **widechars**
(code points on our UCS-4 builds). .NET callers naturally treat them as indices into a `string`,
which is UTF-16. These agree for BMP text and diverge the moment a non-BMP character appears.

A known pattern in existing code is:

```csharp
result.InputPosition[..result.Output.Length]
```

`Output.Length` is a UTF-16 count; `InputPosition` is filled to the code-point count. Correct for
BMP text, silently wrong otherwise.

**Determine, for every consumer that touches either array:**

1. How many call sites use the `TranslatedString`-returning overloads at all?
2. For each, what is done with `InputPosition` / `OutputPosition`? Classify as:
   - **(a) Sliced by length** — e.g. `[..Output.Length]`, `Take(n)`, `Array.Resize`
   - **(b) Indexed to map a position** — e.g. `Output[result.InputPosition[i]]`, or mapping a
     cursor/selection between text and braille
   - **(c) Passed onward** — stored, serialised, sent to a display driver or UI layer
   - **(d) Ignored** — allocated only because the overload requires it
3. Are the arrays **allocated fresh per call, or reused**? If reused, are stale entries beyond the
   valid range ever read?
4. How is the array size chosen? (`input.Length`? `outputLength`? a constant?)
5. Does anything convert between a braille cell index and a .NET string index — and if so, does it
   assume they are the same number?

**Then answer the design question.** Which of these should the wrapper do?

- **A. Expose the cell count.** Add `OutputLength` (widechar count) to `TranslatedString`, document
  that positions are code-point indices, callers slice by that. Non-breaking; callers mapping a
  position to a string index must still convert.
- **B. Convert to UTF-16 indices.** The wrapper remaps both arrays so every value is a valid index
  into the returned string. Existing caller code becomes correct as written; costs a pass over the
  arrays per call and rewrites caller-supplied buffers.
- **C. Reject non-BMP input** from the position-reporting overloads. No silent wrongness, no
  remapping; refuses input the simpler overloads accept.
- **D. Document only.** No API change.

**Recommend one, with the usage evidence behind it.** If classification (d) dominates — the arrays
are allocated but ignored — say so plainly; that makes this trivial.

### 3.2 `FindTable` allocation

`lou_findTable` returns memory that liblouis documents as the caller's to free. The wrapper
deliberately **does not** free it: the Windows binaries are built with mingw-w64 and allocate from
`msvcrt.dll`, while .NET frees through `ucrtbase.dll`. Freeing across those heaps corrupts them.

So every `FindTable` call leaks one small string, permanently. `Shutdown()` does not reclaim it —
those strings are caller-owned and sit outside the chains it frees.

**This is fine if `FindTable` is called a bounded number of times. It is a genuine unbounded leak
if it is called per request or per document.**

**Search:** `rg -n "FindTable|IndexTables" --type cs`

**Determine:**
1. Call frequency: once at startup? per request? per translated document? in a loop?
2. Rough upper bound on calls over a long-lived process's lifetime.
3. How many *distinct* query strings are used? (A small fixed set means a managed cache fixes it
   completely.)
4. Is the result already cached by the caller?

**Report:** frequency classification and distinct-query count. If it is hot, the fix is a managed
cache in the wrapper keyed by query string — that bounds the leak *and* removes the repeated
native call.

### 3.3 Non-BMP exposure  🔴 answer this first

Several items above collapse to "no impact" if non-BMP characters never reach the API.

**Determine:** can the text passed to `Translate` / `BackTranslate` / `CharactersToDots` /
`DotsToCharacters` / `Hyphenate` ever contain non-BMP characters? Consider:

- Emoji (extremely common in user-generated text and increasingly in published material)
- Musical symbols (U+1D100–U+1D1FF) — plausible in braille music contexts
- Rare CJK (Extension B and beyond), historic scripts, some mathematical alphanumerics
- Text originating from user input, web content, EPUB/DAISY sources, or OCR

**A useful empirical check**, if there is a corpus or logs of real inputs:
```csharp
// count inputs where these disagree
value.Length != value.EnumerateRunes().Count()
```

**Report:** *never* / *rare but possible* / *routine* — plus what evidence supports it. If the
answer is a confident *never*, say what enforces that (input validation? a sanitising step?).

---

## 4. Latent risks worth checking while you are in there

### 4.1 Throughput under the global lock

Every native call now serialises on one process-wide lock — including `Version` and the `Logging`
members, which previously did not take it.

**Check:** is `Version` read on a hot path (a health endpoint, a per-request log line)? It now
contends with translations. Cheap to fix caller-side by caching it once — it cannot change at
runtime.

**Report:** whether translation is called concurrently, at what rough rate, and whether `Version`
or `Logging.LogLevel` is touched per request.

### 4.2 Two competing logging paths

`LibLouis.Instance.Logger` and `Logging.SetCallback` both register a native log callback. **Last
writer wins** — whichever ran most recently silently disables the other.

**Search:** `rg -n "Logging\.SetCallback|Logging\.LogLevel|\.Logger\s*=" --type cs`

**Report:** whether both are used anywhere in the same process, and in what order they run.

### 4.3 Table paths containing commas

The wrapper joins table lists with `string.Join(',', tableList)`, because that is liblouis's list
separator. A table path containing a comma silently splits into two bogus paths.

**Check:** are table paths ever derived from user input, configuration, a content directory, or an
install path that could contain a comma?

**Report:** how table paths are constructed, and whether a comma is possible.

### 4.4 Buffer sizing for `outputLength`

`outputLength` is the maximum number of braille cells. Too small a value makes the translation
fail (`LibLouisException`), not truncate.

**Check:** how is `outputLength` derived? A common pattern is `input.Length * 2`. Contracted
braille is usually shorter than the input, but marker/emphasis tables can expand it
substantially — the wrapper's own tests produce 20 cells from 15 characters.

**Report:** the formula(s) used, and whether any translation failures have been observed in
production that might be under-sized buffers rather than bad tables.

### 4.5 Relative table paths

Table resolution depends on the process working directory and `DataPath`. Relative paths are
fragile under IIS, systemd, containers, and test runners.

**Report:** relative or absolute, and whether `DataPath` is set anywhere.

---

## 5. Report template

Please answer in roughly this shape.

```markdown
## Scope
Repositories/projects audited:
LibLouis.NET version referenced:
Total call sites into LibLouis.NET:

## 1. Compile-breaking
- `using`/`Dispose` on the singleton: N hits
  - at application shutdown: N
  - in request/loop scope (was destroying the table cache repeatedly): N
- DI registrations: N, form used:
- `FindTable` nullability warnings: N

## 2. Silent behaviour changes
- Non-null `formtype` passed: N call sites; array read after the call: YES/NO
  - if YES, what the values were used for:
- `formtype` arrays reused across calls: YES/NO
- `Hyphenate` called: YES/NO
- `CharactersToDots` / `DotsToCharacters` call sites: N
- Any caller assuming result.Length == input.Length: YES/NO
- `catch (ObjectDisposedException)` around LibLouis calls: N
- Log alerting keyed on liblouis message text: YES/NO
- `DataPath` used: YES/NO

## 3. Design questions
### Position arrays
- Call sites using TranslatedString overloads: N
- Usage classification: (a) sliced N / (b) indexed N / (c) passed on N / (d) ignored N
- Arrays reused across calls: YES/NO
- Cell index ↔ string index conversion present: YES/NO
- **Recommendation: A / B / C / D**, because:

### FindTable
- Frequency: startup once / per request / per document / loop
- Estimated calls per process lifetime:
- Distinct query strings: N
- Already cached caller-side: YES/NO

### Non-BMP exposure
- Verdict: never / rare but possible / routine
- Evidence:

## 4. Latent risks
- Concurrent translation: YES/NO, approx rate:
- `Version` on a hot path: YES/NO
- Both logging paths used: YES/NO
- Table paths could contain a comma: YES/NO
- `outputLength` formula(s):
- Table paths relative or absolute:
- `DataPath` set: YES/NO

## 5. Anything else
Surprises, suspicious patterns, or places where the wrapper's contract seems misunderstood.
```

---

## Appendix A: widechar vs. char, in one table

Given `string s`:

| Text | `s.Length` (UTF-16) | widechars (UCS-4) | Agree? |
|---|---|---|---|
| `"abc"` | 3 | 3 | yes |
| `"Første"` | 6 | 6 | yes (Latin-1 is BMP) |
| `"日本語"` | 3 | 3 | yes (BMP) |
| `"😀"` | 2 | 1 | **no** |
| `"𝄞𝄞"` | 4 | 2 | **no** |

Rule of thumb for reviewing caller code: **any `.Length` used as a count of braille cells, table
positions, or liblouis buffer entries is suspect** unless the text is known BMP-only.

## Appendix B: search patterns

```bash
# every entry point
rg -n "LibLouis\.Instance|LibLouis\.Shutdown|TranslatedString|TypeForm|TranslationMode" --type cs

# compile-breaking
rg -n "using\s*\(\s*LibLouis|using\s+var\s+\w+\s*=\s*LibLouis\.Instance|\.Dispose\(\)" --type cs
rg -n "Add(Singleton|Scoped|Transient).*LibLouis" --type cs

# position arrays
rg -n "InputPosition|OutputPosition|CursorPosition" --type cs

# length assumptions near calls
rg -n -C3 "\.Length" --type cs | rg -n "Translate|Dots|Hyphenate|Position"

# logging
rg -n "Logging\.SetCallback|Logging\.LogLevel|LibLouis\.Instance\.Logger" --type cs

# table list construction
rg -n "IndexTables|FindTable|\.ctb|\.cti|\.dis|\.utb|tables" --type cs
```

## Appendix C: what changed in the wrapper, for reference

Eleven commits, all with reasoning in the messages:

| Fix | Symptom before |
|---|---|
| typeform scratch buffer | wrote past a managed array on every text-expanding translation |
| `lou_indexTables` NULL terminator | walked off the array into garbage pointers; process hung |
| `lou_hyphenate` byte[] buffer | wrote over the marshalling stub's stack; process wedged |
| returned strings not freed | `free()` on liblouis static storage; process aborted |
| `UTF8StringNoFreeMarshaller` rewrite | returned uninitialised memory; threw on empty strings |
| log callback rooted | "callback was made on a garbage collected delegate"; process died |
| widechar length counting | over-read input buffers on non-BMP text |
| `lou_free` under the lock | use-after-free against concurrent translation |
| shutdown guards | "disposed" object silently kept working |
| all native calls under one lock | `Version` and `Logging` mutated global state unsynchronised |
| `IDisposable` → `Shutdown()` | `using` on the singleton killed liblouis process-wide |
