---
# robots: noindex governs crawlers, not the local search index -- without this a reader
# searching the site gets "This page has moved" alongside the page it moved to.
search: false
head:
  - - meta
    - http-equiv: refresh
      content: '0; url=/efcore.pagination/integrations/'
  - - meta
    - name: robots
      content: noindex
---

# This page has moved

It was split into one page per integration, under **Integrations**:
[PostgreSQL](/integrations/postgresql/), [NodaTime](/integrations/nodatime/) and
[Custom types](/integrations/custom-types/). The [section overview](/integrations/) links all three.

You should have been redirected already. This address stays published because it ships inside the `10.0.0`
package READMEs on nuget.org, which are rendered against that version forever — see
`docs/scripts/verify-frozen-urls.mjs`.
