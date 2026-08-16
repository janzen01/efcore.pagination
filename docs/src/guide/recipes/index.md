---
# robots: noindex governs crawlers, not the local search index -- without this a reader
# searching the site gets "This page has moved" alongside the page it moved to.
search: false
head:
  - - meta
    - http-equiv: refresh
      content: '0; url=/efcore.pagination/recipes/'
  - - meta
    - name: robots
      content: noindex
---

# This page has moved

The recipes now live in the **Cookbook**: [Recipes](/recipes/).

You should have been redirected already. This address stays published because it ships inside the `10.0.0`
package READMEs on nuget.org, which are rendered against that version forever — see
`docs/scripts/verify-frozen-urls.mjs`.
