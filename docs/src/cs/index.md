---
layout: home

hero:
  name: Janzen.Pagination
  text: Stránkování jako smlouva
  tagline: Jednou za entitu určíš, podle čeho smí klient řadit, v čem hledat a co filtrovat. Engine z opinionated query stringu udělá přeložený EF Core dotaz a všechno, co nedokáže splnit, odmítne.
  image:
    src: /icon.svg
    alt: Janzen.Pagination
  actions:
    - theme: brand
      text: Anglický průvodce
      link: /guide/getting-started/
    - theme: alt
      text: Kontrakt query stringu
      link: /reference/query-string/

features:
  - title: Whitelist, ne dotazovací jazyk
    icon: 🔒
    details: Pole, které není deklarované, není adresovatelné, a operátor, který pro pole není povolený, je pro to pole odmítnutý. Žádné nechtěné ORDER BY nad neindexovaným sloupcem.
  - title: Jedenáct filtrovacích operátorů
    icon: 🎛️
    details: $eq $in $null $sw $ilike $contains $lt $lte $gt $gte $btw, k tomu negace $not a $and / $or mezi kritérii nad jedním polem.
  - title: Deterministické stránkování
    icon: 🧭
    details: Ke každému řazení se připojí tie-breaker, takže řádky nemůžou přeskakovat mezi stránkami. Bez něj engine požadavek raději odmítne.
---

::: warning Česká verze se teprve překládá
Kompletní je zatím jen [anglický průvodce](/guide/). Tahle stránka je začátek překladu, ne jeho výsledek, a
odkazy níž vedou do anglické verze.
:::

## Instalace

```bash
dotnet add package Janzen.Pagination.EntityFrameworkCore
dotnet add package Janzen.Pagination.AspNetCore
```

```http
GET /products?page=2&limit=25&sortBy=price:DESC&search=widget&filter.status=$in:Active,Draft
```

## Kam dál

- [Getting started](/guide/getting-started/) — od instalace k funkčnímu endpointu
- [Query-string contract](/reference/query-string/) — přesná specifikace všech parametrů a operátorů,
  včetně SQL, které z každého operátoru vyleze
- [Configuration API](/reference/configuration/) — každá metoda builderu, co povoluje a co odmítá
- [Errors](/reference/errors/) — všechny chyby `400`, co je způsobí a jak je opravit
