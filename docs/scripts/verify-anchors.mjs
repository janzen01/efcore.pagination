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
//
// It also fails the build when a page links at a redirect stub. A stub keeps an address that already shipped
// inside a package README answering (see scripts/verify-frozen-urls.mjs); it exists for those external copies,
// never for navigation inside the site. Nothing else catches one: `ignoreDeadLinks` is satisfied because the stub
// is a real page, and the frozen-URL check reads only the READMEs. The reader gets "This page has moved" and a
// meta refresh to a section overview, from a page that is `noindex` and out of site search.

import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { join, dirname, relative, posix } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const src = join(root, 'src')
const dist = join(root, '.dist')

const walk = (dir) => readdirSync(dir, { withFileTypes: true })
	.flatMap((entry) => entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)])

// A page `srcExclude` keeps out of the build emits no ids, so checking a fragment link written in one is
// guaranteed to fail -- a required-check failure on a file the config deliberately excludes. The patterns are read
// out of config.mts rather than restated here; if that list is ever renamed away this falls back to checking
// everything, which is the behaviour before this check existed and fails loudly rather than silently.
const excluded = [...(readFileSync(join(root, '.vitepress', 'config.mts'), 'utf8')
	.match(/srcExclude:\s*\[([^\]]*)\]/)?.[1]
	.matchAll(/['"]([^'"]+)['"]/g) ?? [])].map(([, pattern]) => pattern.replace(/\*+$/, ''))

const isExcluded = (file) => {
	const path = relative(src, file).replaceAll('\\', '/')
	return excluded.some((prefix) => path.startsWith(prefix))
}

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

const pages = walk(src).filter((f) => f.endsWith('.md') && !isExcluded(f))

const addressOf = (file) => {
	const dir = relative(src, dirname(file)).replaceAll('\\', '/')
	return dir === '' ? '/' : `/${dir}/`
}

// A stub is a page that meta-refreshes away in its own front matter -- read only that block, so a page merely
// quoting the meta in its prose is not one. Derived rather than listed, so a stub added later is covered without
// anyone remembering to name it here.
const frontMatter = (text) => text.match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? ''

const stubs = new Set(pages
	.filter((file) => /http-equiv:\s*refresh/.test(frontMatter(readFileSync(file, 'utf8'))))
	.map(addressOf))

const broken = []
const atStub = []
let checked = 0

for (const file of pages) {

	const text = readFileSync(file, 'utf8')
	const base = addressOf(file)
	const resolve = (target) => target.startsWith('/') ? target : posix.normalize(base + target)

	// Markdown links carrying a fragment. Bare `#anchor` means this page.
	for (const [, href] of text.matchAll(/\]\(([^)\s]*#[^)\s]+)\)/g)) {

		const [target, anchor] = href.split('#')
		const resolved = target === '' ? base : resolve(target)
		const page = (resolved.replace(/\/+$/, '') || '/').slice(1)

		const ids = idsOf(page)
		checked++

		if (ids === null) broken.push(`${relative(root, file)} -> ${href}   (no such page in the build)`)
		else if (!ids.has(anchor)) broken.push(`${relative(root, file)} -> ${href}   (page exists, heading does not)`)

	}

	// Every in-site link, fragment or not: none of them may land on a redirect stub.
	for (const [, href] of text.matchAll(/\]\(([^)\s]+)\)/g)) {

		if (href.startsWith('#') || /^[a-z][a-z0-9+.-]*:/i.test(href)) continue

		const resolved = resolve(href.split('#')[0])
		const address = resolved.endsWith('/') ? resolved : `${resolved}/`

		if (stubs.has(address) && addressOf(file) !== address) atStub.push(`${relative(root, file)} -> ${href}   (${address})`)

	}

}

if (broken.length > 0) {
	console.error('\nThese links point at headings that were not emitted:\n')
	for (const line of broken) console.error(`  ${line}`)
	console.error('\nCheck the id in .dist -- VitePress slugify is not GitHub slugify.\n')
}

if (atStub.length > 0) {
	console.error('\nThese links point at a redirect stub instead of at the page they mean:\n')
	for (const line of atStub) console.error(`  ${line}`)
	console.error('\nA stub exists so an address already published in a package README keeps answering. Link at the\n' +
		'page that holds the content -- the stub is noindex, out of site search, and shows an interstitial.\n')
}

// Both lists are reported before exiting: a page with one of each would otherwise hide the second behind the first.
if (broken.length > 0 || atStub.length > 0) process.exit(1)

console.log(`All ${checked} anchor links resolve to a heading, and none of them lands on a redirect stub.`)
