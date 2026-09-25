import { execFile } from 'node:child_process'
import { existsSync, readdirSync } from 'node:fs'
import { resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Plugin } from 'vite'
import { defineVersionedConfig } from '@viteplus/versions'
import type { DefaultTheme } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import rootNavigation from '../src/navigation.json' with { type: 'json' }
import { navigationIn, versionedNavigation } from '../scripts/navigation.mjs'

// Dev only. scripts/sync-archive.mjs runs once before `vitepress dev`, which left the newest line's copy
// serving a start-up snapshot: the manifest defines that line as the working tree, but an edit to docs/src
// only ever reached the site root. `/v<line>.x/` is exactly the page an author opens to check that the version
// rewriting behaved, so a stale render reads as "my change did not take effect". The script writes only files
// whose bytes changed, so a save costs one HMR update rather than a rebuilt archive.
const syncArchiveOnEdit: Plugin = {
    name: 'janzen-sync-archive',
    apply: 'serve',
    configureServer(server) {
        const script = fileURLToPath(new URL('../scripts/sync-archive.mjs', import.meta.url))

        // `resolve` drops the trailing slash, so the separator has to be put back. Without it a sibling
        // directory whose name merely begins with `src` matches too -- and `docs/*` is deny-by-default
        // precisely so local planning directories can live beside the site.
        const sources = resolve(fileURLToPath(new URL('../src/', import.meta.url))) + sep

        let pending: NodeJS.Timeout | undefined
        let running = false
        let missed = false
        let closed = false

        // One run at a time, with a single catch-up if edits arrived while it was working. Two overlapping
        // runs each prune whatever their own run did not produce, so the one that started earlier can delete
        // a page the later one has just written -- and nothing would then schedule the sync that puts it back.
        const sync = () => {
            if (closed || running) {
                missed = !closed
                return
            }

            running = true
            execFile(process.execPath, [script], (error, _stdout, stderr) => {
                running = false
                if (error && !closed) server.config.logger.error(stderr || error.message)
                if (missed) {
                    missed = false
                    sync()
                }
            })
        }

        // Only docs/src is watched. The script writes into docs/archive, which VitePress also watches now that
        // docs/ is the source root -- reacting to that would feed the script its own output.
        server.watcher.on('all', (_event, file) => {
            if (!resolve(file).startsWith(sources)) return
            clearTimeout(pending)
            pending = setTimeout(sync, 150)
        })

        // A save usually precedes Ctrl+C, so a sync is often still in flight here. Clearing the timer alone
        // left its callback free to schedule one more run against a server that no longer exists.
        server.httpServer?.once('close', () => {
            closed = true
            missed = false
            clearTimeout(pending)
        })
    }
}

// Every released line is served from its own copy under docs/archive/<segment>/, generated before the build by
// scripts/sync-archive.mjs. The plugin discovers them by reading that directory, and the directory name is the
// URL segment verbatim -- so this list is also what distinguishes an archived locale from a real one below.
const archiveDir = new URL('../archive/', import.meta.url)
const archived = existsSync(archiveDir)
    ? readdirSync(archiveDir, { withFileTypes: true }).filter((entry) => entry.isDirectory()).map((entry) => entry.name)
    : []

// The published origin, in one place. It is the canonical href, the sitemap hostname, and -- as its path --
// the `base` every built URL carries and the prefix on the favicon. Those were four literals before, so a
// custom domain or a repository rename had to find all of them; miss the canonical and every page names an
// authoritative copy at an address that no longer exists, while the sitemap correctly advertises the new one.
// Nothing checks the canonical, so that combination stays green and actively misdirects.
const site = 'https://janzen01.github.io/efcore.pagination/'
const base = new URL(site).pathname

// A page that asks not to be indexed must not also name itself authoritative: the two are contradictory
// signals, and the redirect stubs already say it three ways (robots, site search, sitemap).
const noindexed = (head: unknown[][] = []) =>
    head.some(([, attrs]) => (attrs as { name?: string, content?: string })?.name === 'robots'
        && String((attrs as { content?: string })?.content ?? '').includes('noindex'))

// The site's URLs ship inside the package READMEs on nuget.org, which nuget.org renders per version
// forever. The obligation is that each of them keeps answering -- not that this file keeps owning them.
// A page may move; what has to stay behind is something published at the old path, either the page itself
// or a redirect stub (a markdown file whose `head` sets `http-equiv: refresh`). What breaks the promise is
// publishing nothing there, which is what happens by accident: renaming `<name>/index.md` to `<name>.md`
// builds `<name>.html`, and GitHub Pages serves that at `/guide/<name>` but 404s at `/guide/<name>/`.
// `docs/scripts/verify-frozen-urls.mjs` fails the build when one of these paths has nothing behind it.
// Four of them are now redirect stubs rather than pages: query-string, recipes, aspnetcore and
// providers-and-types moved into the Reference, Cookbook and Integrations sections.
//
// The frozen paths that are now redirect stubs rather than pages. They must keep being published, but
// they carry `robots: noindex` and must not be advertised in the sitemap -- submitting a URL and then
// telling the crawler to ignore it is a contradiction search consoles report back as one.
const redirectStubs = [
    'guide/query-string/',
    'guide/recipes/',
    'guide/aspnetcore/',
    'guide/providers-and-types/'
]

// Registered in .vitepress/theme/index.ts. Unlike the built-in dropdown this keeps the reader on the page they were
// reading, which is the whole point when the link that brought them here came out of a package README and names a
// specific page. It goes into every version's nav -- see scripts/navigation.mjs.
const versionSwitcher = { component: 'VersionSwitcher' }

const navigation = versionedNavigation(
    rootNavigation,
    archived
        .map((segment) => [segment, navigationIn(fileURLToPath(new URL(segment, archiveDir)))] as const)
        .filter(([, own]) => own !== undefined),
    versionSwitcher
)

const config = withMermaid(defineVersionedConfig({
    title: 'Janzen.Pagination',
    description: 'Dynamic, configuration-driven pagination, filtering and sorting for EF Core and ASP.NET Core',

    // Project page, not a user page: everything is served under the repository name.
    base,
    lang: 'en-US',
    outDir: './.dist',

    // `srcDir` is deliberately absent. @viteplus/versions reads it as the root *containing* `sources` and
    // `archive`, so `./src` would send it looking for docs/src/src and throw at startup. The cost is that
    // VitePress now treats docs/ itself as the source root, which is why `srcExclude` below is rooted there
    // and has to keep out the stray planning notes that live at docs/*.md and are gitignored.

    // The current line is served from `sources` at the root, so the URLs frozen in the 10.0.0 READMEs keep
    // answering; each subfolder of `archive` is served at /<name>/. `versionSwitcher` is false because the
    // built-in dropdown always lands on a version's home page -- the VersionSwitcher component in the nav
    // keeps the reader on the page they were reading.
    versionsConfig: {
        current: 'latest',
        sources: 'src',
        archive: 'archive',
        versionSwitcher: false
    },

    // Rooted at docs/, not docs/src/, because of the missing `srcDir` above.
    // `*.md` keeps out the local planning notes dropped at the top of docs/: they are gitignored, so they
    // exist on a maintainer's machine and not in CI, and without this the two builds would differ.
    // `.dist` and `.vitepress/cache` are inside the source root for the same reason, and nothing else covers
    // them -- VitePress's own default ignores `**/node_modules/**` and `**/dist/**`, and this output directory
    // is `.dist`. They hold no markdown today; one `.md` left in `src/public/` would be copied into `.dist`
    // by a build and become a page at the site root on the next one, archived under every version with it.
    srcExclude: ['*.md', '.dist', '.vitepress/cache'],

    // GitHub Pages serves /foo from foo.html without a redirect, so extension-less links are safe here.
    cleanUrls: true,

    // VitePress only emits sitemap.xml when a hostname is set. The base belongs in it: these URLs are
    // advertised in PackageProjectUrl and in all four package READMEs on nuget.org.
    // GitHub Pages answers a page at three addresses -- `/x/`, `/x/index` and `/x/index.html` -- and the
    // version switcher links the middle one, because it builds its targets from `relativePath`. That form is
    // not configurable and it does resolve (measured against the live site: all three return 200), so the
    // duplication is what is left to deal with: without this every page is crawlable twice, and with three
    // versions that is 78 pages at two addresses each. One tag per built file covers all of its addresses.
    // Archived pages point at themselves, not at the root: they are a different version's content, and saying
    // otherwise would be a lie the moment the line they belong to stops being the newest.
    transformPageData(pageData) {
        const head = (pageData.frontmatter.head ?? []).filter(([, attrs]) => attrs?.rel !== 'canonical')

        if (noindexed(head)) return

        const route = pageData.relativePath.replace(/(^|\/)index\.md$/, '$1').replace(/\.md$/, '')

        pageData.frontmatter.head = [...head, ['link', { rel: 'canonical', href: `${site}${route}` }]]
    },

    // Archived versions are dropped as a whole rather than stub by stub. While a line is the current one its
    // archived copy is byte-identical to the root, so advertising both is asking a crawler to pick a canonical
    // between two copies of the same page. They stay reachable and linkable -- just not submitted.
    sitemap: {
        hostname: site,
        transformItems: (items) => items.filter((item) =>
            !redirectStubs.includes(item.url) && !archived.some((version) => item.url.startsWith(`${version}/`)))
    },

    // A dead link fails the build. With cross-page links written by hand, that is the only thing standing
    // between a renamed heading and a guide that quietly points at nothing.
    ignoreDeadLinks: false,

    lastUpdated: true,

    head: [
        ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}icon.svg` }],
        ['meta', { name: 'theme-color', content: '#512BD4' }]
    ],

    // Mermaid must not sweep the DOM for `.mermaid` elements on load. The plugin renders each diagram
    // explicitly from its own component, so the sweep finds nothing it is responsible for -- except the
    // empty `<div class="mermaid">` that SSR leaves behind. `<Suspense>` renders the component with no SVG
    // yet, hydration then re-creates it rather than adopting it, and the server's copy is orphaned in the
    // DOM. Left to `startOnLoad`, mermaid parsed that empty div and replaced it with its own error graphic:
    // every page carrying a diagram showed a rendered diagram *and* "Syntax error in text" underneath it.
    // The diagram sources were never the problem -- all six parse cleanly.
    mermaid: { startOnLoad: false },

    // `mermaidPlugin` is the markdown rule's options; `mermaid` above is the runtime config handed to
    // `mermaid.initialize`. The class matters because mermaid's own sweep selects `.mermaid`, and the
    // diagrams do not need to be found that way -- the plugin's component renders each one explicitly by id.
    mermaidPlugin: { class: 'mermaid-diagram' },

    // Nolebase enhanced-readabilities ships raw .vue in its dist, so Vite has to bundle it for SSR rather
    // than let Node require it -- Node cannot load a .vue file. Both halves are needed: without `exclude`
    // the dev server pre-bundles it and the menu never mounts, without `noExternal` the production build
    // fails while rendering.
    // @viteplus/versions ships the VersionSwitcher component the same way, so it needs the same pairing.
    vite: {
        // VitePress resolves the public directory as `resolve(srcDir, vite.publicDir || 'public')`, and dropping
        // `srcDir` moved srcDir from docs/src to docs -- which silently stopped publishing docs/src/public and
        // took the favicon and the logo with it. Named here rather than moving the directory, so that one copy
        // keeps serving every version from the site root.
        publicDir: 'src/public',

        plugins: [syncArchiveOnEdit],

        optimizeDeps: {
            exclude: ['@nolebase/vitepress-plugin-enhanced-readabilities/client', '@viteplus/versions']
        },
        ssr: {
            noExternal: [/@nolebase\//, /@viteplus\//]
        }
    },


    themeConfig: {
        logo: '/icon.svg',

        // Every version indexes itself, and nothing more is needed: the plugin gives each archived version its
        // own locale, and VitePress builds one index per locale -- so a search on /v10.0.x/ only ever sees
        // /v10.0.x/, and the root index carries no archived page. The duplicate-hits-across-versions problem
        // this was once guarded against cannot occur. Blanking `archive/` instead left every archived page with
        // a search box over an empty index (documentCount: 0), including the copy every package README points
        // a reader at.
        search: { provider: 'local' },
        socialLinks: [{ icon: 'github', link: 'https://github.com/janzen01/efcore.pagination' }],
        // `:path` is substituted with VitePress's `filePath`, which is relative to `srcDir` -- and dropping
        // `srcDir` above moved that from `guide/index.md` to `src/guide/index.md`. So the pattern stops at
        // `docs/`: leaving the old `docs/src/` here spells every link `docs/src/src/...`, which is a GitHub
        // 404 on every page, and nothing in `docs:build` reads an edit link to notice.
        editLink: {
            pattern: 'https://github.com/janzen01/efcore.pagination/edit/master/docs/:path',
            text: 'Edit this page on GitHub'
        },
        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © Lubos Jansky'
        },

        // docs/src/navigation.json, keyed per version. The plugin's types describe only the flat form, so the
        // keyed one is cast; the shape it accepts is documented in scripts/navigation.mjs.
        nav: navigation.nav as unknown as DefaultTheme.NavItem[],
        sidebar: navigation.sidebar,
        outline: { level: [2, 3], label: 'On this page' }
    }
}))

// vitepress-plugin-mermaid (2.0.17, 2024) pre-bundles mermaid's dependencies by name, and `debug` is one
// mermaid 11 no longer has. Vite then logs "Failed to resolve dependency: debug" on every dev start, for a
// package that is neither installed nor needed. Dropping it from the list keeps the dev output honest --
// a warning nobody can act on is a warning everybody learns to skip past.
// `mermaid` is added because the plugin names mermaid's *dependencies* but not mermaid itself, and Vite does
// not crawl inside node_modules for imports -- so mermaid was served raw, and the browser then fetched its
// CommonJS dependencies raw too, where `import fastdom from 'fastdom'` throws and the whole page renders blank.
// Pre-bundling mermaid pulls that entire subtree into one ES module, which fixes the class rather than the
// instance: the plugin's hardcoded list dates from 2024 and drifts from mermaid's real dependencies with every
// release. The production build never had the problem, which is what let it sit unnoticed.
const include = config.vite?.optimizeDeps?.include
if (Array.isArray(include)) {
    config.vite!.optimizeDeps!.include = [...include.filter((dep) => dep !== 'debug'), 'mermaid']
}

// An archived page is docs/src as it stood at a tag, so "Edit this page on GitHub" would open the file living
// at that path on master today -- different content, from a line the reader deliberately is not reading, with
// no way to tell from the page that it happened. The plugin gives every archived version its own locale, which
// is where the inherited link has to be switched off.
for (const version of archived) {
    const locale = config.locales?.[version]
    if (locale) (locale.themeConfig ??= {}).editLink = false
}

export default config
