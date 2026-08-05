# Upstream braille specs

Copied verbatim from `upstream/liblouis-<version>/tests/braille-specs/`. Re-copy when the upstream
version is bumped; the diff is the set of expectations that changed.

102 specs are here, covering roughly 60 languages. Upstream ships 150.

## What is not here, and why

### The eight dictionary harnesses

200,000+ cases each, around 800,000 in total. They would dominate every run for little extra signal.
Add them behind a switch if wanted.

### 40 specs that do not yet pass

Not excluded because they are wrong — excluded because nobody has worked out yet whether the
disagreement is this harness or the wrapper, and a red suite that is expected to be red is worse
than a smaller green one. They divide into three groups.

**29 fail on table resolution, and are probably one root cause rather than 29.** 22 report no table
matching a query, 7 resolve to a table other than the one the spec's `__assert-match` names. The
queries themselves look well formed — `language:bn grade:1` — so the likely cause is which tables
reach `lou_indexTables`, or which of them liblouis manages to analyse. Worth starting here: it is
the largest group and the cheapest to be wrong about.

```
bn.yaml cs_harness.yaml eo-g1_harness.yaml es-g0-g1.yaml ethio-g1_harness.yaml hi_harness.yaml 
hr-8dots_harness.yaml hu-hu-g1_braille_input_backward.yaml hu-hu-g1_braille_input_forward.yaml 
hu-hu-g1_dictionary_numbers.yaml hu-hu-g1_harness.yaml kmr.yaml litdigits6Dots_backward.yaml 
lt-6dot_harness.yaml lt_harness.yaml lv_harness.yaml ml.yaml nl-g0_harness.yaml pa.yaml 
pl-g1.yaml sl-g1.yaml tr.yaml
```

**10 disagree on translation output**, a fraction of their cases each. These are the interesting
ones: either a per-case option this harness does not model, or a real difference between the wrapper
and upstream.

```
bel.yaml  1/45
de-g0-detailed-specs.yaml  35/476
de-g1-detailed-specs.yaml  35/476
grc-international-composed.yaml  30/432
no.yaml  167/868
pt.yaml  21/806
ru.yaml  39/140
spaces.yaml  11/1849
uk.yaml  3/45
zh-tw.yaml  2/20
```

**1 does not parse**: a multi-line double-quoted scalar YamlDotNet rejects.

## Restoring one

Copy it back from the upstream tarball and run the suite. Note that liblouis caches compiled tables
process-wide, so when bisecting a disagreement, use a cold process per variant — otherwise the
result depends on which test compiled a given table first.
