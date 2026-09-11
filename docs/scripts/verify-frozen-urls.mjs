// Fails the build if a URL that has already shipped inside a released NuGet package stops answering.
//
// The four package READMEs are rendered by nuget.org against the version they were packed with, forever.
// The lists below are that history, each entry tagged with the release that published it, and each path ends
// with a slash: Jekyll built `<name>/index.html` and VitePress still does.
//
// This does not freeze the site's structure. A page may move; what has to stay at the old path is
// *something* -- the page, or a redirect stub (a markdown file whose `head` sets `http-equiv: refresh`),
// which builds to the same `index.html` and satisfies this check. What it catches is the accident:
// renaming `<name>/index.md` to `<name>.md` builds `<name>.html`, which GitHub Pages serves at
// `/guide/<name>` but 404s at `/guide/<name>/` -- a dead link inside a package, found by a consumer.
//
// The second half reads the READMEs as they are *now* and requires the same of whatever they advertise, because
// that is what the NEXT release freezes. So the job at release time is to move that set into the lists below under
// the new version number, which is what turns "the READMEs still happen to point there" into a permanent guarantee.
// Never remove an entry.
//
// Only the *package* READMEs earn that promotion. Packaging is per project (Directory.Build.props sets
// PackageReadmeFile and packs each project's own README.md), so the root README ships in no package at all: it is
// the GitHub landing page, and a page it names can move with a one-line edit. Its URLs are still checked -- a dead
// link on the landing page is a real defect -- but promoting them would add permanent entries to a list that may
// never shrink, on the strength of a document that is not immutable. The closing summary counts the two sources
// separately, and the set to promote at a stable release is the package-README one.
//
// A README URL may also carry a `#fragment`, checked against the ids the build actually emitted. A deep link is
// frozen exactly like the page it points into: reword the heading and every copy of that readme nuget.org has
// already rendered lands the reader at the top of the page instead.

import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join, dirname, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const docs = join(dirname(fileURLToPath(import.meta.url)), '..')
const root = join(docs, '..')
const dist = join(docs, '.dist')

const frozen = [
	// 10.0.0 -- the pre-VitePress guide, built by Jekyll's `permalink: pretty`. Four of these are redirect stubs today.
	['index.html', '10.0.0'],
	['guide/index.html', '10.0.0'],
	['guide/getting-started/index.html', '10.0.0'],
	['guide/query-string/index.html', '10.0.0'],
	['guide/configuration/index.html', '10.0.0'],
	['guide/projections/index.html', '10.0.0'],
	['guide/aspnetcore/index.html', '10.0.0'],
	['guide/providers-and-types/index.html', '10.0.0'],
	['guide/recipes/index.html', '10.0.0'],

	// 10.0.1 -- the READMEs stopped leaning on the stubs above and point at the current homes instead.
	['integrations/aspnetcore/index.html', '10.0.1'],
	['integrations/aspnetcore/openapi/index.html', '10.0.1'],
	['integrations/custom-types/index.html', '10.0.1'],
	['integrations/nodatime/index.html', '10.0.1'],
	['integrations/postgresql/index.html', '10.0.1'],
	['recipes/index.html', '10.0.1'],
	['reference/configuration/index.html', '10.0.1'],
	['reference/errors/index.html', '10.0.1'],
	['reference/query-string/index.html', '10.0.1'],
	['reference/response/index.html', '10.0.1'],

	// 10.0.2 -- the query composers got their own reference page, named by the EntityFrameworkCore readme.
	['reference/composers/index.html', '10.0.2']
]

// The deep links a package README names, frozen for the same reason and just as permanently: reword the heading and
// every copy of that readme nuget.org has already rendered drops its reader at the top of the page instead.
const frozenFragments = [
	['integrations/aspnetcore/index.html', 'errors-as-problemdetails', '10.0.1'],
	['integrations/postgresql/index.html', 'like-vs-ilike', '10.0.1'],
	['reference/errors/index.html', 'filter-operators', '10.0.1'],
	['reference/query-string/index.html', 'operator-reference', '10.0.1'],
	['reference/query-string/index.html', 'value-formats', '10.0.1'],
	['reference/response/index.html', 'link-response-header-rfc-8288', '10.0.1'],
	['reference/response/index.html', 'the-request-echo', '10.0.2']
]

// The list above is history: what 10.0.0 already published. This second half is the future: whatever the
// READMEs currently point at is what the NEXT release freezes, so it has to exist before that release, not
// after someone reports a dead link from nuget.org. Reading them turns "remember to add a path here" into a
// check -- the same reason scripts/verify-anchors.mjs exists.
const SITE = 'https://janzen01.github.io/efcore.pagination/'

const readmes = [
	join(root, 'README.md'),
	...readdirSync(join(root, 'src'), { withFileTypes: true })
		.filter((entry) => entry.isDirectory())
		.map((entry) => join(root, 'src', entry.name, 'README.md'))
]

const LANDING = 'README.md'

const advertised = new Map()
const malformed = []

// Keyed so a link that is both frozen and still advertised is checked once, and reported as frozen: that is the
// half that cannot be fixed by editing a README.
const fragments = new Map(frozenFragments.map(([page, anchor, release]) => [
	`${page}#${anchor}`,
	{ page, anchor, source: `released in ${release}`, url: `${page.replace(/index\.html$/, '')}#${anchor}` }
]))

for (const readme of readmes.filter(existsSync)) {
	const source = relative(root, readme).replaceAll('\\', '/')

	// The slash after the project name is optional so the bare site root is read too, and a bare URL in running
	// prose is allowed to end a sentence -- the address stops before the punctuation, not after it.
	for (const [, match] of readFileSync(readme, 'utf8').matchAll(/https:\/\/janzen01\.github\.io\/efcore\.pagination\/?([^)\s"']*)/g)) {
		const url = match.replace(/[.,;:!?]+$/, '')
		const [path, anchor] = url.split('#')

		// Not a page address. Skipping it silently is how a README ships a permanent 404: nuget.org renders that
		// copy for its version forever, and neither VitePress nor scripts/verify-anchors.mjs reads a README.
		if (path !== '' && !path.endsWith('/')) {
			malformed.push({ source, url })
			continue
		}

		const page = `${path}index.html`
		advertised.set(page, source)

		// A README link that names a section is frozen the same way the page is. Nothing else checks these:
		// scripts/verify-anchors.mjs walks the markdown sources, and these are absolute URLs sitting in files
		// VitePress never builds.
		if (anchor && !fragments.has(`${page}#${anchor}`)) fragments.set(`${page}#${anchor}`, { page, anchor, source, url })
	}
}

if (malformed.length > 0) {
	console.error('\nThese README links are not page addresses, so nothing can freeze them:\n')
	for (const { source, url } of malformed) {
		console.error(`  ${SITE}${url}   (${source})`)
	}
	console.error('\nA page is authored as <name>/index.md and published at <name>/, so every site URL a README\n' +
		'advertises has to end with a slash -- add one, or drop the link.\n')
	process.exit(1)
}

const required = new Map(frozen.map(([path, release]) => [path, `released in ${release}`]))
for (const [path, source] of advertised) if (!required.has(path)) required.set(path, source)

const missing = [...required].filter(([path]) => !existsSync(join(dist, path)))

if (missing.length > 0) {
	console.error('\nThese URLs are published in a package README and are missing from the build:\n')
	for (const [path, source] of missing) {
		console.error(`  ${SITE}${path.replace(/index\.html$/, '')}   (${source})`)
	}
	console.error('\nEach page must be authored as <name>/index.md so it builds to <name>/index.html.\n')
	process.exit(1)
}

// Every page above exists, so reading them is safe from here.
const idsOf = (page) => new Set([...readFileSync(join(dist, page), 'utf8').matchAll(/id="([^"]+)"/g)].map((match) => match[1]))

const dangling = [...fragments.values()].filter(({ page, anchor }) => !idsOf(page).has(anchor))

if (dangling.length > 0) {
	console.error('\nThese README links name a heading the build did not emit:\n')
	for (const { url, source } of dangling) {
		console.error(`  ${SITE}${url}   (${source})`)
	}
	console.error('\nRead the id out of .dist -- VitePress slugify is not GitHub slugify.\n')
	process.exit(1)
}

console.log(
	`All ${required.size} advertised URLs are present in the build (${frozen.length} frozen by a release), ` +
	`and all ${fragments.size} deep-linked headings exist (${frozenFragments.length} of them frozen).`
)

// The promotion list, split by where the link lives, because only one of the two sources is immutable.
const toFreeze = (sources) => sources.filter((source) => source !== LANDING && !source.startsWith('released in')).length
const landingOnly = (sources) => sources.filter((source) => source === LANDING).length

const pageSources = [...required.values()]
const fragmentSources = [...fragments.values()].map(({ source }) => source)

console.log(
	`Freeze at the next stable release: ${toFreeze(pageSources)} URLs and ${toFreeze(fragmentSources)} headings, ` +
	`advertised by a package README. Checked but never frozen: ${landingOnly(pageSources)} URLs and ` +
	`${landingOnly(fragmentSources)} headings, advertised only by the root README.`
)
