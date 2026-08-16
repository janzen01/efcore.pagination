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

import { existsSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', '.dist')

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

const missing = frozen.filter((path) => !existsSync(join(dist, path)))

if (missing.length > 0) {
	console.error('\nThese URLs ship inside released packages and are missing from the build:\n')
	for (const path of missing) {
		console.error(`  https://janzen01.github.io/efcore.pagination/${path.replace(/index\.html$/, '')}`)
	}
	console.error('\nEach page must be authored as <name>/index.md so it builds to <name>/index.html.\n')
	process.exit(1)
}

console.log(`All ${frozen.length} released URLs are present in the build.`)
