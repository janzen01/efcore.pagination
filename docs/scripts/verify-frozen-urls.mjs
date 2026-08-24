// Fails the build if a URL that has already shipped inside a released NuGet package stops answering.
//
// The four package READMEs are rendered by nuget.org against the version they were packed with, forever.
// The paths below are the ones 10.0.0 shipped, and each ends with a slash, because Jekyll's
// `permalink: pretty` built every page as `<name>/index.html`.
//
// This does not freeze the site's structure. A page may move; what has to stay at the old path is
// *something* -- the page, or a redirect stub (a markdown file whose `head` sets `http-equiv: refresh`),
// which builds to the same `index.html` and satisfies this check. What it catches is the accident:
// renaming `<name>/index.md` to `<name>.md` builds `<name>.html`, which GitHub Pages serves at
// `/guide/<name>` but 404s at `/guide/<name>/` -- a dead link inside a package, found by a consumer.
//
// Add a path here when a new one is published in a package README. Never remove one.
//
// A README URL may also carry a `#fragment`, and the second half of this script checks it against the ids the build
// actually emitted. A deep link is frozen exactly like the page it points into: reword the heading and every copy of
// that readme nuget.org has already rendered lands the reader at the top of the page instead.

import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join, dirname, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const docs = join(dirname(fileURLToPath(import.meta.url)), '..')
const root = join(docs, '..')
const dist = join(docs, '.dist')

const frozen = [
	'index.html',
	'guide/index.html',
	'guide/getting-started/index.html',
	'guide/query-string/index.html',
	'guide/configuration/index.html',
	'guide/projections/index.html',
	'guide/aspnetcore/index.html',
	'guide/providers-and-types/index.html',
	'guide/recipes/index.html'
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

const advertised = new Map()
const fragments = []

for (const readme of readmes.filter(existsSync)) {
	const source = relative(root, readme).replaceAll('\\', '/')

	for (const [, url] of readFileSync(readme, 'utf8').matchAll(/https:\/\/janzen01\.github\.io\/efcore\.pagination\/([^)\s"']*)/g)) {
		// Only site pages: the READMEs also link into github.com paths under the same project name.
		if (url.startsWith('blob/') || url.startsWith('releases')) continue

		const [path, anchor] = url.split('#')
		if (path !== '' && !path.endsWith('/')) continue

		advertised.set(`${path}index.html`, source)

		// A README link that names a section is frozen the same way the page is, and a fragment cannot be recalled
		// from a readme nuget.org has already rendered. Nothing else checks these: scripts/verify-anchors.mjs walks
		// the markdown sources, and these are absolute URLs sitting in files VitePress never builds.
		if (anchor) fragments.push({ page: `${path}index.html`, anchor, source, url })
	}
}

const required = new Map(frozen.map((path) => [path, 'released in 10.0.0']))
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

const dangling = fragments.filter(({ page, anchor }) => !idsOf(page).has(anchor))

if (dangling.length > 0) {
	console.error('\nThese README links name a heading the build did not emit:\n')
	for (const { url, source } of dangling) {
		console.error(`  ${SITE}${url}   (${source})`)
	}
	console.error('\nRead the id out of .dist -- VitePress slugify is not GitHub slugify.\n')
	process.exit(1)
}

console.log(
	`All ${required.size} advertised URLs are present in the build (${frozen.length} of them frozen by 10.0.0), ` +
	`and all ${fragments.length} deep-linked headings exist.`
)
