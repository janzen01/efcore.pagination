// Materialises docs/archive/<version>/ from git, so the site can serve one frozen copy per released line.
//
// The archive is a build artefact, not repository content: it is gitignored and rebuilt before every `vitepress
// dev` and `vitepress build`. That is the whole point. A committed archive would put a second copy of all 28
// pages in the tree for every line ever released, and every grep, every editor search and every code-reading
// tool would then have to be told to ignore them. Generating instead means there is nothing to ignore.
//
// Why a copy per line exists at all: the URLs in this site ship inside the package READMEs, and nuget.org
// renders those per version forever. `docs/src` is served at the root and is the newest release, so the 10.0.0
// READMEs -- which advertise unversioned paths -- keep landing on something current. Every README from 10.1.0
// on advertises `/v<major>.<minor>.x/...` instead, which is what this script publishes.
//
// The boundary is the *library's* breaking-change component, not the release number: within one line the third
// component only ever adds surface, so a frozen link can name something newer than the reader has, but never
// something that no longer exists. Removing needs a new line, which gets its own copy.

import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const docs = join(dirname(fileURLToPath(import.meta.url)), '..')
const root = join(docs, '..')
const src = join(docs, 'src')
const archive = join(docs, 'archive')

// The newest line has no tag of its own, so it is whatever docs/src holds right now. That is correct in both
// places it matters: locally it is what you are editing, and in CI the deploy runs on `release: published`,
// where the checkout *is* the tag being released. A line only gets pinned to a tag once a newer line exists.
const WORKING_TREE = null

// Segment -> git ref. The segment is the URL path verbatim, and it is frozen the moment a README advertising
// it reaches nuget.org, so it is never renamed -- only added to.
const versions = [
	{ segment: 'v10.1.x', ref: WORKING_TREE },
	{ segment: 'v10.0.x', ref: 'v10.0.3' }
]

// Content that is not part of a version. `public/` is site chrome, served from the site root where one copy
// serves every version. `cs/` was an abandoned Czech landing page: it is gone from docs/src, but tags up to
// v10.0.3 still carry it, and without this skip those archives would publish it as an English-locale page.
const skipped = (path) => path.startsWith('cs/') || path.startsWith('public/')

// The only root-absolute in-site links the content actually contains, measured rather than assumed. Anything
// else that looks absolute is reported instead of rewritten -- see `strays`.
const SECTIONS = 'guide|integrations|recipes|reference'

// `/icon.svg` is the one root-absolute path that must survive unprefixed: it is a public asset, served from
// the site root for every version.
const SHARED_ASSETS = new Set(['/icon.svg'])

const git = (args) => execFileSync('git', args, { cwd: root, maxBuffer: 1 << 28 })

const filesAt = (ref) => git(['ls-tree', '-r', '--name-only', ref, '--', 'docs/src'])
	.toString('utf8')
	.split('\n')
	.filter(Boolean)
	.map((path) => path.slice('docs/src/'.length))

const walk = (dir) => readdirSync(dir, { withFileTypes: true })
	.flatMap((entry) => entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)])

const contentsOf = (ref) => ref === WORKING_TREE
	? walk(src).map((file) => [relative(src, file).replaceAll('\\', '/'), readFileSync(file)])
	: filesAt(ref).map((path) => [path, git(['show', `${ref}:docs/src/${path}`])])

// A link this script does not know how to version. Left unrewritten it would silently point back at the root
// -- at the newest release rather than at this version -- and nothing downstream would catch it: the target
// exists, so `ignoreDeadLinks` is satisfied and verify-anchors resolves it to a real page.
const strays = (text) => [
	...[...text.matchAll(/\]\((\/[^)\s]*)\)/g)],
	...[...text.matchAll(/^\s*(?:link|src):\s+(\/\S*)/gm)]
]
	.map(([, link]) => link)
	.filter((link) => !new RegExp(`^/(?:${SECTIONS})/`).test(link) && !SHARED_ASSETS.has(link))

// The post-condition, checked on the rewritten text: every section path in an archived page must sit behind
// this version's segment. This is the check that actually bites. An unversioned section link is not a dead
// link -- it resolves, to the same path in the *current* version -- so `ignoreDeadLinks` is satisfied and
// verify-anchors finds a real heading on a real page. Measured before being written: the content mentions
// these paths only inside links, never in prose, so this is exact rather than noisy.
const leaked = (text, segment) =>
	[...text.matchAll(new RegExp(`(.{0,${segment.length + 1}})/(?:${SECTIONS})/`, 'g'))]
		.filter(([, before]) => !before.endsWith(`/${segment}`))
		.map(([match]) => match.trim())

const versioned = (text, segment) => text
	// Markdown links written root-absolute, which is how the guide crosses a section boundary.
	.replace(new RegExp(`\\]\\((/(?:${SECTIONS})/)`, 'g'), `](/${segment}$1`)
	// The home layout carries its links in front matter instead, as `link:` under hero actions and features.
	.replace(new RegExp(`^(\\s*link:\\s+)(/(?:${SECTIONS})/)`, 'gm'), `$1/${segment}$2`)
	// The four redirect stubs meta-refresh to an absolute target, `base` included.
	.replace(/url=\/efcore\.pagination\//g, `url=/efcore.pagination/${segment}/`)

const found = []

rmSync(archive, { recursive: true, force: true })

for (const { segment, ref } of versions) {

	let entries
	try {
		entries = contentsOf(ref)
	} catch (error) {
		console.error(`\nCannot read docs/src at ${ref ?? 'the working tree'} for ${segment}.\n`)
		console.error('A shallow clone has no tags, and this needs them. Fetch them with `git fetch --tags`,\n' +
			'or in a workflow give actions/checkout `fetch-depth: 0`.\n')
		console.error(`${error.message}\n`)
		process.exit(1)
	}

	for (const [path, contents] of entries) {

		if (skipped(path)) continue

		const target = join(archive, segment, path)
		mkdirSync(dirname(target), { recursive: true })

		if (!path.endsWith('.md')) {
			writeFileSync(target, contents)
			continue
		}

		const text = contents.toString('utf8')
		for (const link of strays(text)) found.push(`  ${segment}/${path} -> ${link}   (unknown section)`)

		const rewritten = versioned(text, segment)
		for (const link of leaked(rewritten, segment)) found.push(`  ${segment}/${path} -> ${link}   (still unversioned)`)

		writeFileSync(target, rewritten)

	}

}

if (found.length > 0) {
	console.error('\nThese links would leave an archived page pointing back out of its own version:\n')
	for (const line of found) console.error(line)
	console.error('\nAn unversioned link lands on the same path in the current version -- a different release\n' +
		'under a frozen URL -- and nothing downstream catches it, because the page it reaches exists.\n' +
		'"unknown section": add it to SECTIONS, or to SHARED_ASSETS if it is served from the site root.\n' +
		'"still unversioned": the link uses a syntax `versioned` does not rewrite yet.\n')
	process.exit(1)
}

const built = versions.map(({ segment }) => segment).join(', ')
const pages = versions.reduce((total, { segment }) =>
	total + walk(join(archive, segment)).filter((file) => file.endsWith('.md')).length, 0)

console.log(`Archived ${pages} pages across ${versions.length} versions (${built}).`)
