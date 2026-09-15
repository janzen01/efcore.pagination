// Materialises docs/archive/<version>/ from git, so the site can serve one frozen copy per released line.
//
// The archive is a build artefact, not repository content: it is gitignored and rebuilt before every `vitepress
// dev` and `vitepress build`. That is the whole point. A committed archive would put a second copy of all 26
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
//
// Writes are idempotent: a file is only touched when its bytes change, and anything the run did not produce is
// pruned. That is what lets the dev server re-run this on every edit without a storm of HMR updates, and what
// keeps a `docs:build` in one terminal from yanking the archive out from under a `docs:dev` in another.

import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { configuredSrcExclude } from './src-exclude.mjs'

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
	{ segment: 'v11.0.x', ref: WORKING_TREE },
	{ segment: 'v10.1.x', ref: 'v10.1.0' },
	{ segment: 'v10.0.x', ref: 'v10.0.3' }
]

// `public/` is site chrome, served from the site root where one copy serves every version. `cs/` was an
// abandoned Czech landing page: it is gone from docs/src, so no `srcExclude` pattern covers it any more, but
// tags up to v10.0.3 still carry it and those archives would otherwise publish it as an English-locale page.
const ALWAYS_SKIPPED = ['cs/', 'public/']

/**
 * Whether a path relative to docs/src is kept out of an archived copy. `excluded` is a matcher over
 * docs-root-relative paths, which is the shape `srcExclude` is written in.
 */
export const isSkipped = (path, excluded) =>
	ALWAYS_SKIPPED.some((prefix) => path.startsWith(prefix)) || excluded(`src/${path}`)

// The only root-absolute in-site links the content actually contains, measured rather than assumed. Anything
// else that looks absolute is reported instead of rewritten -- see `strays`.
const SECTIONS = 'guide|integrations|recipes|reference'

// `/icon.svg` is the one root-absolute path that must survive unprefixed: it is a public asset, served from
// the site root for every version.
const SHARED_ASSETS = new Set(['/icon.svg'])

const SITE = 'https://janzen01.github.io/efcore.pagination/'

const SECTION_PATH = new RegExp(`^/(?:${SECTIONS})/`)

// `matchAll` works on an internal clone and `replace` resets `lastIndex`, so a `g`-flagged pattern is safe to
// share. The segment-dependent ones are built once per version rather than once per page: `versioned` and
// `leaked` are called for every file, and recompiling three fixed patterns 78 times a run bought nothing.
const SITE_URL = new RegExp(`${SITE.replace(/[.]/g, '\\.')}\\S*`, 'g')

const patterns = new Map()

const patternsFor = (segment) => {
	if (!patterns.has(segment)) {
		patterns.set(segment, {
			leak: new RegExp(`(.{0,${segment.length + 1}})(/(?:${SECTIONS})/)`, 'g'),
			markdownLink: new RegExp(`\\]\\((/(?:${SECTIONS})/)`, 'g'),
			frontMatterLink: new RegExp(`^(\\s*link:\\s+)(/(?:${SECTIONS})/)`, 'gm')
		})
	}

	return patterns.get(segment)
}

// A root-absolute link to something this script does not know how to version. Left unrewritten it would
// silently point back at the root -- at the newest release rather than at this version -- and nothing
// downstream would catch it: the target exists, so `ignoreDeadLinks` is satisfied and verify-anchors resolves
// it to a real page.
export const strays = (text) => [
	// The destination is read without requiring the closing paren, because a link may carry a title
	// (`](/faq/ "FAQ")`) or wrap the target in angle brackets. Demanding `)` made this blind to exactly those
	// two forms while `versioned` rewrote them happily -- so a titled link to an unknown section was archived
	// still pointing at the current version, which is the one thing this check exists to refuse.
	...[...text.matchAll(/\]\(<?(\/[^)\s>]*)/g)],
	...[...text.matchAll(/^\s*(?:link|src):\s+(\/\S*)/gm)]
]
	.map(([, link]) => link)
	.filter((link) => !SECTION_PATH.test(link) && !SHARED_ASSETS.has(link))

// The site's own absolute URL, which none of the rewrites touch: they all key off a leading slash. It is the
// same leak wearing a hostname, and it is easy to write by accident because every package README is full of
// them -- so it is reported as its own case rather than left to `leaked`, which could only describe it as a
// fragment of the hostname.
export const siteAbsolute = (text) => [...text.matchAll(SITE_URL)]
	.map(([link]) => link.replace(/[).,;:!?]+$/, ''))

// The post-condition, checked on the rewritten text: every section path in an archived page must sit behind
// this version's segment. This is the check that actually bites. An unversioned section link is not a dead
// link -- it resolves, to the same path in the *current* version -- so `ignoreDeadLinks` is satisfied and
// verify-anchors finds a real heading on a real page. Measured before being written: the content mentions
// these paths only inside links, never in prose, so this is exact rather than noisy.
export const leaked = (text, segment) =>
	[...text.matchAll(patternsFor(segment).leak)]
		.filter(([, before]) => !before.endsWith(`/${segment}`))
		.map(([, , path]) => path)

export const versioned = (text, segment) => text
	// Markdown links written root-absolute, which is how the guide crosses a section boundary.
	.replace(patternsFor(segment).markdownLink, `](/${segment}$1`)
	// The home layout carries its links in front matter instead, as `link:` under hero actions and features.
	.replace(patternsFor(segment).frontMatterLink, `$1/${segment}$2`)
	// The four redirect stubs meta-refresh to an absolute target, `base` included.
	.replace(/url=\/efcore\.pagination\//g, `url=/efcore.pagination/${segment}/`)

const git = (args, options) => execFileSync('git', args, { cwd: root, maxBuffer: 1 << 28, ...options })

const walk = (dir) => readdirSync(dir, { withFileTypes: true })
	.flatMap((entry) => entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)])

/**
 * Splits a `git cat-file --batch` stream into one buffer per requested object. Each answer is
 * `<sha> <type> <size>\n`, the bytes, then a newline, so the contents are found by length rather than by
 * scanning -- a blob may contain anything, header-shaped lines included.
 *
 * Refusing a non-`blob` answer is the point of the type check. Git reports an object it cannot produce as
 * `<sha> missing`, which has no size: read naively that yields an empty buffer for this file and leaves every
 * later one unaligned, so the run would archive a blank page and then die without naming anything.
 *
 * The sha in each header is checked against the one that was asked for. Positional mapping is the whole
 * mechanism here, and if it ever slipped every page's text would be written under a different page's URL --
 * both of them real pages, so `ignoreDeadLinks`, verify-anchors and verify-frozen-urls would all stay green.
 * Git answers one line per request, in order; this is what makes that an assumption the run can rely on.
 */
export const parseBatchStream = (stream, entries) => {
	const contents = []
	let offset = 0

	while (contents.length < entries.length) {
		const { sha, path } = entries[contents.length]
		const headerEnd = stream.indexOf(0x0a, offset)

		if (headerEnd === -1) {
			throw new Error(`git cat-file --batch stopped after ${contents.length} of ${entries.length} objects.`)
		}

		const header = stream.toString('utf8', offset, headerEnd)
		const [answered, type, rawSize] = header.split(' ')

		if (type !== 'blob') {
			throw new Error(`git cat-file --batch answered "${type}" for docs/src/${path}. ` +
				'A partial clone fetches blobs on demand and reports one missing when it cannot; clone without ' +
				'a filter, or run `git fetch` to bring the objects down.')
		}

		if (answered !== sha) {
			throw new Error(`git cat-file --batch answered for ${answered} where ${sha} ` +
				`(docs/src/${path}) was asked for. The responses are no longer in request order, so every ` +
				'file after this one would be archived under the wrong path.')
		}

		const size = Number(rawSize)

		// Checked because the failure is otherwise reported as something it is not: a non-numeric size makes
		// the offset NaN, the next read restarts from the top of the stream, and what surfaces is the sha
		// check complaining about response order -- sending the reader after a bug in git that is not there.
		if (!Number.isInteger(size) || size < 0) {
			throw new Error(`git cat-file --batch gave no usable size for docs/src/${path}: "${header}".`)
		}

		contents.push(stream.subarray(headerEnd + 1, headerEnd + 1 + size))
		offset = headerEnd + 1 + size + 1
	}

	return contents
}

// One `git ls-tree` and one `git cat-file --batch`, rather than a `git show` per file. `-z` keeps git from
// quoting a path it considers unusual, which would otherwise be sliced apart as if it were a plain name.
export const blobsAt = (ref) => {
	let listing

	// Only the ref lookup can fail for want of a shallow clone's missing tags, so only it carries that remedy.
	// Reading the blobs fails for its own reasons and says so itself; sharing one handler put the wrong advice
	// first, telling a reader to fetch tags they already had.
	try {
		listing = git(['ls-tree', '-r', '-z', ref, '--', 'docs/src']).toString('utf8')
	} catch (error) {
		throw new Error(`git could not read ${ref} -- a shallow clone has no tags, and this needs them. ` +
			'Fetch them with `git fetch --tags`, or in a workflow give actions/checkout `fetch-depth: 0`.\n\n' +
			error.message)
	}

	const entries = listing
		.split('\0')
		.filter(Boolean)
		.map((record) => {
			const [meta, path] = record.split('\t')

			return { sha: meta.split(' ')[2], path: path.slice('docs/src/'.length) }
		})

	if (entries.length === 0) return []

	const stream = git(['cat-file', '--batch'], { input: `${entries.map(({ sha }) => sha).join('\n')}\n` })

	return parseBatchStream(stream, entries).map((contents, index) => [entries[index].path, contents])
}

// The newest line is read from disk rather than from a ref, because it is the content being edited -- but
// only the files git knows about. Archiving whatever happens to sit under docs/src would put an untracked
// draft into the archive, build it, and let verify-frozen-urls report a package README's URL as satisfied
// from a page CI does not have -- and that URL is what the next release freezes on nuget.org forever.
// Filtering the walk rather than reading the index directly keeps a locally deleted file simply absent.
const contentsOf = (ref) => {
	if (ref !== WORKING_TREE) return blobsAt(ref)

	const tracked = new Set(git(['ls-files', '-z', '--', 'docs/src']).toString('utf8')
		.split('\0')
		.filter(Boolean)
		.map((path) => path.slice('docs/src/'.length)))

	return walk(src)
		.map((file) => [relative(src, file).replaceAll('\\', '/'), file])
		.filter(([path]) => tracked.has(path))
		.map(([path, file]) => [path, readFileSync(file)])
}

const fail = (lines) => {
	for (const line of lines) console.error(line)
	process.exit(1)
}

// NTFS keeps a directory's existing name through `mkdirSync({ recursive: true })`, so after a case-only
// rename in docs/src the listing still returns the old spelling while `written` holds the new one. Compared
// exactly, prune then deleted the very file this run had just written -- the page vanished from the archive
// and the build died on the sidebar link to it. Compare the way the filesystem does.
//
// Known ceiling: the surviving file keeps the old directory's spelling, so the local archive serves it at the
// old-cased URL. Invisible on Windows, and CI builds the archive from git where the spelling is right; the
// full fix is renaming the directory, which is more than the defect is worth. `rm -rf docs/archive` clears it.
const sameFile = process.platform === 'win32'
	? (path) => path.toLowerCase()
	: (path) => path

/**
 * The archive writer: idempotent writes plus a prune of whatever the run did not produce. Together they are
 * what lets the dev server re-run this on every save -- an unchanged page costs nothing, and a page renamed or
 * deleted in docs/src cannot linger in an archived copy and keep being published at its frozen URL.
 */
export const archiveWriter = () => {
	const written = new Set()

	const write = (target, contents) => {
		written.add(sameFile(target))
		if (existsSync(target) && readFileSync(target).equals(contents)) return false
		mkdirSync(dirname(target), { recursive: true })
		writeFileSync(target, contents)
		return true
	}

	const prune = (dir) => {
		if (!existsSync(dir)) return
		for (const entry of readdirSync(dir, { withFileTypes: true })) {
			const path = join(dir, entry.name)
			if (!entry.isDirectory()) {
				if (!written.has(sameFile(path))) rmSync(path)
				continue
			}
			prune(path)
			if (readdirSync(path).length === 0) rmSync(path, { recursive: true })
		}
	}

	return { write, prune, written }
}

const main = () => {

	const excluded = configuredSrcExclude()
	const { write, prune, written } = archiveWriter()

	const found = []

	for (const { segment, ref } of versions) {

		let entries
		try {
			entries = contentsOf(ref)
		} catch (error) {
			fail([
				`\nCannot read docs/src at ${ref ?? 'the working tree'} for ${segment}.\n`,
				`${error.message}\n`
			])
		}

		// `git ls-tree` exits 0 with no output for a ref that simply has no such path, so this is not an error
		// the catch above can see. It is a real possibility rather than paranoia: v10.0.0 predates this site
		// and carries nothing under docs/src, and the release checklist invites adding older lines.
		if (entries.length === 0) {
			fail([
				`\n${segment} would be empty: ${ref ?? 'the working tree'} carries no files under docs/src.\n`,
				'A tag from before the VitePress site existed has nothing to archive -- v10.0.0 is one of those.\n' +
				'Point the entry at a ref that has the documentation, or drop it from `versions`.\n'
			])
		}

		for (const [path, contents] of entries) {

			if (isSkipped(path, excluded)) continue

			const target = join(archive, segment, path)

			if (!path.endsWith('.md')) {
				write(target, contents)
				continue
			}

			const text = contents.toString('utf8')
			const rewritten = versioned(text, segment)

			const report = (links, reason) => {
				for (const link of new Set(links)) found.push(`  ${segment}/${path} -> ${link}   (${reason})`)
			}

			report(strays(text), 'unknown section')
			report(siteAbsolute(text), 'absolute site URL')
			report(leaked(rewritten, segment), 'still unversioned')

			write(target, Buffer.from(rewritten, 'utf8'))

		}

	}

	// Pruning the whole archive rather than each segment is what drops a segment that has left the manifest.
	prune(archive)

	if (found.length > 0) {
		fail([
			'\nThese links would leave an archived page pointing back out of its own version:\n',
			...found,
			'\nAn unversioned link lands on the same path in the current version -- a different release\n' +
			'under a frozen URL -- and nothing downstream catches it, because the page it reaches exists.\n' +
			'"unknown section": add it to SECTIONS, or to SHARED_ASSETS if it is served from the site root.\n' +
			'"absolute site URL": write it as a root-relative link, so the rewrites can version it.\n' +
			'"still unversioned": the link uses a syntax `versioned` does not rewrite yet.\n'
		])
	}

	const built = versions.map(({ segment }) => segment).join(', ')
	const pages = [...written].filter((file) => file.endsWith('.md')).length

	console.log(`Archived ${pages} pages across ${versions.length} versions (${built}).`)

}

// Importable for scripts/sync-archive.test.mjs, which exercises the pure helpers above without the side effects.
if (process.argv[1] !== undefined && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))) main()
