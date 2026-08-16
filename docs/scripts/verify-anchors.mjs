// Fails the build when a cross-page link points at a heading that does not exist.
//
// `ignoreDeadLinks: false` catches a link to a missing *page*, but not a link to a missing *anchor* on a page
// that does exist -- so a renamed heading leaves working-looking links that land at the top of the right page
// and quietly drop the reader somewhere else. This walks the built HTML instead of the markdown, so it checks
// the ids VitePress actually emitted rather than the ids we assume it emits from a heading.
//
// That distinction is the point. VitePress's slugify is not GitHub's: `Keep a big table's page count cheap`
// becomes `keep-a-big-table-s-page-count-cheap` (the apostrophe becomes a dash, it is not dropped), and an
// em dash survives into the id verbatim -- `paginateselectmapasync-—-sql-then-finish-in-memory`. Both of
// those were already wrong in the site when this check was written.

import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { join, dirname, relative, posix } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const src = join(root, 'src')
const dist = join(root, '.dist')

const walk = (dir) => readdirSync(dir, { withFileTypes: true })
	.flatMap((entry) => entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)])

const idCache = new Map()

const idsOf = (page) => {
	if (!idCache.has(page)) {
		const file = join(dist, page, 'index.html')
		idCache.set(page, existsSync(file)
			? new Set([...readFileSync(file, 'utf8').matchAll(/id="([^"]+)"/g)].map((m) => m[1]))
			: null)
	}
	return idCache.get(page)
}

const broken = []
let checked = 0

for (const file of walk(src).filter((f) => f.endsWith('.md'))) {

	const dir = relative(src, dirname(file)).replaceAll('\\', '/')
	const base = dir === '' ? '/' : `/${dir}/`

	// Markdown links carrying a fragment. Bare `#anchor` means this page.
	for (const [, href] of readFileSync(file, 'utf8').matchAll(/\]\(([^)\s]*#[^)\s]+)\)/g)) {

		const [target, anchor] = href.split('#')
		const resolved = target === '' ? base : (target.startsWith('/') ? target : posix.normalize(base + target))
		const page = (resolved.replace(/\/+$/, '') || '/').slice(1)

		const ids = idsOf(page)
		checked++

		if (ids === null) broken.push(`${relative(root, file)} -> ${href}   (no such page in the build)`)
		else if (!ids.has(anchor)) broken.push(`${relative(root, file)} -> ${href}   (page exists, heading does not)`)

	}

}

if (broken.length > 0) {
	console.error('\nThese links point at headings that were not emitted:\n')
	for (const line of broken) console.error(`  ${line}`)
	console.error('\nCheck the id in .dist -- VitePress slugify is not GitHub slugify.\n')
	process.exit(1)
}

console.log(`All ${checked} anchor links resolve to a heading.`)
