# Nota's Danish tables, for the tests

Kept in their own directory, and copied to `nota-tables/` in the output rather than `tables/`.

`LibLouis.NET.Tables` copies the upstream table set into `tables/`. Five of the files here share a
name with an upstream table and differ from it, so a single shared directory would make which copy
a test gets depend on MSBuild item ordering. Separate directories mean every test states which set
it means, and the upstream braille specs can be checked against upstream tables without this set
shadowing them.

## What these are

A fork of an older upstream, not a patch on the current one. They add things upstream does not
have, such as the `foreign` emphasis class behind `TypeForm.ForeignLanguage`, and they are missing
things upstream has since fixed, such as the rules that remove the space between `§` and a following
number. Do not assume a file here matches the upstream file of the same name in either direction.

Two of them, `da-dk-g16-markers.ctb` and `da-dk-braillo.dis`, exist nowhere upstream, which is why
the tests cannot simply use the upstream set.

## These are not canonical

The canonical Nota tables ship with the application. This is a copy, and a stale one: only seven of
the thirty files are reachable from any test.

Reachable: `da-dk-g16-markers.ctb`, `da-dk-braillo.dis`, `da-dk-g08.ctb`, `da-dk-g26.ctb`, and the
three they include (`da-dk-6miscChars.cti`, `da-dk-octobraille.dis`, `da-dk-g2.dic`).

The other twenty three are referenced by nothing. They are kept for now, but nothing keeps them in
step with the application's copy, so treat any of them as evidence of nothing. Prefer adding a test
that needs a file over adding a file that no test needs.
