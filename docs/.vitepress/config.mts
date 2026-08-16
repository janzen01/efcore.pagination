import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

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

const config = withMermaid(defineConfig({
    title: 'Janzen.Pagination',
    description: 'Dynamic, configuration-driven pagination, filtering and sorting for EF Core and ASP.NET Core',

    // Project page, not a user page: everything is served under the repository name.
    base: '/efcore.pagination/',
    srcDir: './src',
    outDir: './.dist',

    // The Czech translation is a single landing page, so the `cs` locale is not registered (see below)
    // and its draft must not build either -- it would otherwise be published as an English-locale page
    // with English chrome and lang="en-US". Kept in the repository rather than deleted: phase 3 removes
    // this line and restores the locale block.
    srcExclude: ['cs/**'],

    // GitHub Pages serves /foo from foo.html without a redirect, so extension-less links are safe here.
    cleanUrls: true,

    // VitePress only emits sitemap.xml when a hostname is set. The base belongs in it: these URLs are
    // advertised in PackageProjectUrl and in all four package READMEs on nuget.org.
    sitemap: {
        hostname: 'https://janzen01.github.io/efcore.pagination/',
        transformItems: (items) => items.filter((item) => !redirectStubs.includes(item.url))
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
    vite: {
        optimizeDeps: {
            exclude: ['@nolebase/vitepress-plugin-enhanced-readabilities/client']
        },
        ssr: {
            noExternal: [/@nolebase\//]
        }
    },

    locales: {
        root: {
            label: 'English',
            lang: 'en-US',
            themeConfig: {
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
                    }
                ],
                sidebar: {
                    '/guide/': guideSidebar,
                    '/integrations/': integrationsSidebar,
                    '/reference/': referenceSidebar,
                    '/recipes/': cookbookSidebar
                },
                outline: { level: [2, 3], label: 'On this page' }
            }
        }

        // The `cs` locale is deliberately absent until there is Czech content to switch to. VitePress
        // rewrites the current path into the other locale unconditionally, so with one Czech page and
        // 25 English ones the language switcher pointed at /cs/<path>/ on every page but the home --
        // 48 links in the built output, every one of them a 404. Registering a locale advertises a
        // translation; one landing page is not one. The draft stays at docs/src/cs/index.md, excluded
        // from the build by `srcExclude` above, and phase 3 restores this block alongside it.
    },

    themeConfig: {
        logo: '/icon.svg',
        search: { provider: 'local' },
        socialLinks: [{ icon: 'github', link: 'https://github.com/janzen01/efcore.pagination' }],
        editLink: {
            pattern: 'https://github.com/janzen01/efcore.pagination/edit/master/docs/src/:path',
            text: 'Edit this page on GitHub'
        },
        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © Lubos Jansky'
        }
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

export default config
