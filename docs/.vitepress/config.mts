import { existsSync, readdirSync } from 'node:fs'
import { defineVersionedConfig } from '@viteplus/versions'
import { withMermaid } from 'vitepress-plugin-mermaid'

// Every released line is served from its own copy under docs/archive/<segment>/, generated before the build by
// scripts/sync-archive.mjs. The plugin discovers them by reading that directory, and the directory name is the
// URL segment verbatim -- so this list is also what distinguishes an archived locale from a real one below.
const archiveDir = new URL('../archive/', import.meta.url)
const archived = existsSync(archiveDir)
    ? readdirSync(archiveDir, { withFileTypes: true }).filter((entry) => entry.isDirectory()).map((entry) => entry.name)
    : []

// The URLs below ship inside the 10.0.0 package READMEs on nuget.org, which nuget.org renders per version
// forever. The obligation is that each of them keeps answering -- not that this file keeps owning them.
// A page may move; what has to stay behind is something published at the old path, either the page itself
// or a redirect stub (a markdown file whose `head` sets `http-equiv: refresh`). What breaks the promise is
// publishing nothing there, which is what happens by accident: renaming `<name>/index.md` to `<name>.md`
// builds `<name>.html`, and GitHub Pages serves that at `/guide/<name>` but 404s at `/guide/<name>/`.
// `docs/scripts/verify-frozen-urls.mjs` fails the build when one of these paths has nothing behind it.
// Four of them are now redirect stubs rather than pages: query-string, recipes, aspnetcore and
// providers-and-types moved into the Reference, Cookbook and Integrations sections.
const guide = {
    overview: '/guide/',
    gettingStarted: '/guide/getting-started/',
    configuration: '/guide/configuration/',
    projections: '/guide/projections/'
}

// The frozen paths that are now redirect stubs rather than pages. They must keep being published, but
// they carry `robots: noindex` and must not be advertised in the sitemap -- submitting a URL and then
// telling the crawler to ignore it is a contradiction search consoles report back as one.
const redirectStubs = [
    'guide/query-string/',
    'guide/recipes/',
    'guide/aspnetcore/',
    'guide/providers-and-types/'
]

const integrations = {
    overview: '/integrations/',
    aspnetcore: '/integrations/aspnetcore/',
    openapi: '/integrations/aspnetcore/openapi/',
    postgresql: '/integrations/postgresql/',
    nodatime: '/integrations/nodatime/',
    customTypes: '/integrations/custom-types/'
}

const reference = {
    queryString: '/reference/query-string/',
    response: '/reference/response/',
    configuration: '/reference/configuration/',
    composers: '/reference/composers/',
    errors: '/reference/errors/'
}

const cookbook = {
    recipes: '/recipes/',
    withoutAspNetCore: '/recipes/without-aspnetcore/',
    performance: '/recipes/performance/',
    testing: '/recipes/testing/',
    troubleshooting: '/recipes/troubleshooting/',
    migration: '/recipes/migration/'
}

const feed = 'https://www.nuget.org/packages/Janzen.Pagination.'

const packages = {
    core: feed + 'EntityFrameworkCore',
    aspnetcore: feed + 'AspNetCore',
    postgresql: feed + 'PostgreSql',
    nodatime: feed + 'NodaTime'
}

// Four sections, four sidebars. The split is by how a page is read: the guide is prose you follow once,
// integrations are per-package and only some readers need them, the reference is looked up mid-task, and
// the cookbook is task-shaped answers.
const guideSidebar = [
    {
        text: 'Guide',
        items: [
            { text: 'Overview', link: guide.overview },
            { text: 'Getting started', link: guide.gettingStarted },
            { text: 'Configuration', link: guide.configuration },
            { text: 'Projections', link: guide.projections }
        ]
    }
]

const integrationsSidebar = [
    {
        text: 'Integrations',
        items: [
            { text: 'Overview', link: integrations.overview },
            { text: 'ASP.NET Core', link: integrations.aspnetcore },
            { text: 'ASP.NET Core — OpenAPI', link: integrations.openapi },
            { text: 'PostgreSQL', link: integrations.postgresql },
            { text: 'NodaTime', link: integrations.nodatime },
            { text: 'Custom types', link: integrations.customTypes }
        ]
    }
]

const referenceSidebar = [
    {
        text: 'Reference',
        items: [
            { text: 'Query-string contract', link: reference.queryString },
            { text: 'Response contract', link: reference.response },
            { text: 'Configuration API', link: reference.configuration },
            { text: 'Query composers', link: reference.composers },
            { text: 'Errors', link: reference.errors }
        ]
    }
]

const cookbookSidebar = [
    {
        text: 'Cookbook',
        items: [
            { text: 'Recipes', link: cookbook.recipes },
            { text: 'Without ASP.NET Core', link: cookbook.withoutAspNetCore },
            { text: 'Performance and indexing', link: cookbook.performance },
            { text: 'Testing', link: cookbook.testing },
            { text: 'Troubleshooting', link: cookbook.troubleshooting },
            { text: 'From nestjs-paginate', link: cookbook.migration }
        ]
    }
]

const config = withMermaid(defineVersionedConfig({
    title: 'Janzen.Pagination',
    description: 'Dynamic, configuration-driven pagination, filtering and sorting for EF Core and ASP.NET Core',

    // Project page, not a user page: everything is served under the repository name.
    base: '/efcore.pagination/',
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

    // Rooted at docs/, not docs/src/, because of the missing `srcDir` above. It keeps out the local planning
    // notes dropped at the top of docs/: they are gitignored, so they exist on a maintainer's machine and not
    // in CI, and without this the two builds would differ.
    srcExclude: ['*.md'],

    // GitHub Pages serves /foo from foo.html without a redirect, so extension-less links are safe here.
    cleanUrls: true,

    // VitePress only emits sitemap.xml when a hostname is set. The base belongs in it: these URLs are
    // advertised in PackageProjectUrl and in all four package READMEs on nuget.org.
    // Archived versions are dropped as a whole rather than stub by stub. While a line is the current one its
    // archived copy is byte-identical to the root, so advertising both is asking a crawler to pick a canonical
    // between two copies of the same page. They stay reachable and linkable -- just not submitted.
    sitemap: {
        hostname: 'https://janzen01.github.io/efcore.pagination/',
        transformItems: (items) => items.filter((item) =>
            !redirectStubs.includes(item.url) && !archived.some((version) => item.url.startsWith(`${version}/`)))
    },

    // A dead link fails the build. With cross-page links written by hand, that is the only thing standing
    // between a renamed heading and a guide that quietly points at nothing.
    ignoreDeadLinks: false,

    lastUpdated: true,

    head: [
        ['link', { rel: 'icon', type: 'image/svg+xml', href: '/efcore.pagination/icon.svg' }],
        ['meta', { name: 'theme-color', content: '#512BD4' }]
    ],

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

        optimizeDeps: {
            exclude: ['@nolebase/vitepress-plugin-enhanced-readabilities/client', '@viteplus/versions']
        },
        ssr: {
            noExternal: [/@nolebase\//, /@viteplus\//]
        }
    },


    themeConfig: {
        logo: '/icon.svg',

        // Only the current version is indexed. Indexing the archive too would return the same page once per
        // line for every query, and VitePress's local search has no facet to group or filter them by -- the
        // reader would get three identical-looking hits and no way to tell which is which. A reader who wants
        // an older page gets there from a README link or the version switcher, both of which are exact.
        search: {
            provider: 'local',
            options: {
                _render: (src, env, md) => env.relativePath.startsWith('archive/') ? '' : md.render(src, env)
            }
        },
        socialLinks: [{ icon: 'github', link: 'https://github.com/janzen01/efcore.pagination' }],
        editLink: {
            pattern: 'https://github.com/janzen01/efcore.pagination/edit/master/docs/src/:path',
            text: 'Edit this page on GitHub'
        },
        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © Lubos Jansky'
        },

        nav: [
            { text: 'Home', link: '/' },
            {
                text: 'Guide',
                items: [
                    { text: 'Overview', link: guide.overview },
                    { text: 'Getting started', link: guide.gettingStarted },
                    { text: 'Configuration', link: guide.configuration },
                    { text: 'Projections', link: guide.projections }
                ]
            },
            {
                text: 'Integrations',
                items: [
                    { text: 'Overview', link: integrations.overview },
                    { text: 'ASP.NET Core', link: integrations.aspnetcore },
                    { text: 'ASP.NET Core — OpenAPI', link: integrations.openapi },
                    { text: 'PostgreSQL', link: integrations.postgresql },
                    { text: 'NodaTime', link: integrations.nodatime },
                    { text: 'Custom types', link: integrations.customTypes }
                ]
            },
            {
                text: 'Reference',
                items: [
                    { text: 'Query-string contract', link: reference.queryString },
                    { text: 'Response contract', link: reference.response },
                    { text: 'Configuration API', link: reference.configuration },
                    { text: 'Query composers', link: reference.composers },
                    { text: 'Errors', link: reference.errors }
                ]
            },
            {
                text: 'Cookbook',
                items: [
                    { text: 'Recipes', link: cookbook.recipes },
                    { text: 'Without ASP.NET Core', link: cookbook.withoutAspNetCore }
                ]
            },
            {
                text: 'NuGet',
                items: [
                    { text: 'EntityFrameworkCore', link: packages.core },
                    { text: 'AspNetCore', link: packages.aspnetcore },
                    { text: 'PostgreSql', link: packages.postgresql },
                    { text: 'NodaTime', link: packages.nodatime }
                ]
            },

            // Registered in .vitepress/theme/index.ts. Unlike the built-in dropdown this keeps the
            // reader on the page they were reading, which is the whole point when the link that
            // brought them here came out of a package README and names a specific page.
            { component: 'VersionSwitcher' }
        ],
        sidebar: {
            '/guide/': guideSidebar,
            '/integrations/': integrationsSidebar,
            '/reference/': referenceSidebar,
            '/recipes/': cookbookSidebar
        },
        outline: { level: [2, 3], label: 'On this page' }
    }
}))

// vitepress-plugin-mermaid (2.0.17, 2024) pre-bundles mermaid's dependencies by name, and `debug` is one
// mermaid 11 no longer has. Vite then logs "Failed to resolve dependency: debug" on every dev start, for a
// package that is neither installed nor needed. Dropping it from the list keeps the dev output honest --
// a warning nobody can act on is a warning everybody learns to skip past.
const include = config.vite?.optimizeDeps?.include
if (Array.isArray(include)) {
    config.vite!.optimizeDeps!.include = include.filter((dep) => dep !== 'debug')
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
