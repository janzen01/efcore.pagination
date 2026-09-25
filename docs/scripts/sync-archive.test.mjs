// Covers the pure halves of sync-archive.mjs and src-exclude.mjs.
//
// Every case here is one that was found by review rather than imagined: each had been verified once by hand,
// by editing the config or the manifest, building, and reverting -- which left nothing behind to notice a
// regression. The two srcExclude cases are the ones that matter most, because both failure modes end the same
// way: a page the author excluded is kept out of the site root and published under a frozen version prefix,
// with the build green and nothing downstream able to tell.

import { test } from 'node:test'
import assert from 'node:assert/strict'

import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import {
	archiveWriter, blobsAt, isSkipped, leaked, parseBatchStream, siteAbsolute, strays, versioned
} from './sync-archive.mjs'
import { expandDirectoryPatterns, readSrcExcludePatterns, srcExcludeMatcher } from './src-exclude.mjs'
import { NAVIGATION, navigationIn, versionedNavigation } from './navigation.mjs'

const SEGMENT = 'v10.1.x'

test('a bare directory pattern excludes what is under it', () => {
	// VitePress hands srcExclude to tinyglobby as `ignore`, where this form excludes the whole directory.
	// Plain picomatch matches only the directory itself, which published the pages it was meant to withhold.
	for (const pattern of ['src/drafts', 'src/drafts/', 'src/drafts/**']) {
		assert.equal(srcExcludeMatcher([pattern])('src/drafts/index.md'), true, pattern)
	}

	assert.equal(srcExcludeMatcher(['src/drafts'])('src/guide/index.md'), false)
})

test('a wildcard directory pattern excludes what is under it', () => {
	// tinyglobby prunes a matching directory whole, wildcard or not -- measured on a fixture that actually has
	// one, which is what the earlier check lacked. Skipping expansion for anything merely ending in `*` left
	// this shape unexpanded, so the pages stayed out of the site root and were published under a frozen prefix.
	assert.equal(srcExcludeMatcher(['src/draft*'])('src/drafts/index.md'), true)
	assert.equal(srcExcludeMatcher(['src/draft*'])('src/guide/index.md'), false)
})

test('wildcard patterns keep their own meaning', () => {
	const excluded = srcExcludeMatcher(['*.md'])

	assert.equal(excluded('notes.md'), true)
	assert.equal(excluded('src/guide/index.md'), false, 'a top-level pattern must not reach into src/')
})

test('expanding a pattern leaves an existing wildcard alone', () => {
	assert.deepEqual(expandDirectoryPatterns(['src/drafts/**']), ['src/drafts/**'])
	assert.deepEqual(expandDirectoryPatterns(['src/drafts']), ['src/drafts', 'src/drafts/**'])
	assert.deepEqual(expandDirectoryPatterns(['src/draft*']), ['src/draft*', 'src/draft*/**'],
		'a single trailing star is a directory name, not a recursive match')
})

test('srcExclude is read out of a config', () => {
	assert.deepEqual(readSrcExcludePatterns("    srcExclude: ['*.md'],"), ['*.md'])
	assert.deepEqual(readSrcExcludePatterns("srcExclude: [\n  '*.md',\n  'src/drafts/**'\n],"), ['*.md', 'src/drafts/**'])
	assert.deepEqual(readSrcExcludePatterns('    cleanUrls: true,'), [], 'absent means nothing is excluded')
	assert.deepEqual(readSrcExcludePatterns('    srcExclude: [],'), [], 'an empty array is not a parse failure')

	// This config is half prose and quotes option values verbatim, so an unanchored match read a comment as
	// the option: the scripts then excluded one list while VitePress applied another.
	assert.deepEqual(
		readSrcExcludePatterns(`
	// withheld with srcExclude: ['cs/**'] until 10.1.0
	srcExclude: ['*.md', '.dist'],
`),
		['*.md', '.dist'],
		'a commented-out occurrence must not win over the real option')
})

test('a srcExclude this parser cannot read is an error, not an empty list', () => {
	// Failing open here is what publishes the excluded pages, so each of these has to throw.
	for (const config of ['    srcExclude: excluded,', '    srcExclude: [...extra],', '    srcExclude: [`*.md`],']) {
		assert.throws(() => readSrcExcludePatterns(config), /srcExclude/, config)
	}
})

test('archived copies skip shared chrome and excluded pages', () => {
	const excluded = srcExcludeMatcher(['src/drafts'])

	assert.equal(isSkipped('public/icon.svg', excluded), true)
	assert.equal(isSkipped('cs/index.md', excluded), true, 'old tags still carry the abandoned Czech page')
	assert.equal(isSkipped('drafts/index.md', excluded), true)
	assert.equal(isSkipped('guide/index.md', excluded), false)
})

test('links are rewritten to sit behind the version segment', () => {
	assert.equal(versioned('See [it](/guide/getting-started/).', SEGMENT), 'See [it](/v10.1.x/guide/getting-started/).')
	assert.equal(versioned('      link: /reference/query-string/', SEGMENT), '      link: /v10.1.x/reference/query-string/')
	assert.equal(
		versioned("      content: '0; url=/efcore.pagination/recipes/'", SEGMENT),
		"      content: '0; url=/efcore.pagination/v10.1.x/recipes/'")
})

test('what must survive the rewrite untouched', () => {
	const github = 'The [Releases](https://github.com/janzen01/efcore.pagination/releases) page.'
	assert.equal(versioned(github, SEGMENT), github)

	const asset = '    src: /icon.svg'
	assert.equal(versioned(asset, SEGMENT), asset)
	assert.deepEqual(strays(asset), [], '/icon.svg is served from the site root for every version')
})

test('leaked names the section path, and passes a versioned link', () => {
	assert.deepEqual(leaked('See [it](/guide/).', SEGMENT), ['/guide/'])
	assert.deepEqual(leaked('See [it](/v10.1.x/guide/).', SEGMENT), [])
	assert.deepEqual(leaked(versioned('[a](/guide/) [b](/recipes/)', SEGMENT), SEGMENT), [])
})

test('an absolute site URL is reported as its own case', () => {
	// None of the rewrites key off a hostname, so this stays unversioned; `leaked` alone could only describe
	// it as a fragment of the hostname.
	const page = 'See [it](https://janzen01.github.io/efcore.pagination/guide/).'

	assert.deepEqual(siteAbsolute(page), ['https://janzen01.github.io/efcore.pagination/guide/'])

	// Called a second time on matching input, because the pattern is a module-level `g` regex shared by every
	// page: `matchAll` works on a clone today, but switching this to `.test()` or `.exec()` would make it
	// stateful and silently skip every other page's URL -- a leak going unreported, which is the one outcome
	// this check exists to prevent. Asserting against non-matching input would pass either way.
	assert.deepEqual(siteAbsolute(page), ['https://janzen01.github.io/efcore.pagination/guide/'])

	assert.deepEqual(siteAbsolute('The [Releases](https://github.com/janzen01/efcore.pagination/releases) page.'), [])
})

test('a header with no usable size is refused as itself', () => {
	// Unchecked, the offset goes to NaN and the next read restarts from the top of the stream, so the failure
	// surfaces as the sha check complaining about response order -- a bug in git that is not there.
	assert.throws(
		() => parseBatchStream(Buffer.from('aaa blob\nx\n'), [{ sha: 'aaa', path: 'guide/index.md' }]),
		/no usable size for docs\/src\/guide\/index\.md/)
})

// `git cat-file --batch` framing. The contents are located by the length in each header rather than by
// scanning, because a blob may contain anything -- including a line shaped like a header.
const batch = (...blobs) => Buffer.concat(blobs.flatMap(([sha, body]) => [
	Buffer.from(`${sha} blob ${Buffer.byteLength(body)}\n`), Buffer.from(body), Buffer.from('\n')
]))

const asked = (...blobs) => blobs.map(([sha], index) => ({ sha, path: `page-${index}.md` }))

const read = (...blobs) => parseBatchStream(batch(...blobs), asked(...blobs))

test('the batch stream is split by length, not by scanning', () => {
	assert.deepEqual(read(['aaa', 'hello'], ['bbb', 'world']).map((b) => b.toString()), ['hello', 'world'])
})

test('an empty blob does not shift the files after it', () => {
	// Nothing in the repository is zero-byte today, so this branch never runs against real git output.
	assert.deepEqual(read(['aaa', 'one'], ['bbb', ''], ['ccc', 'three']).map((b) => b.toString()), ['one', '', 'three'])
})

test('a blob whose body looks like a header is read whole', () => {
	const body = 'text\ndeadbeef blob 99\nmore'

	assert.deepEqual(read(['aaa', body], ['bbb', 'after']).map((b) => b.toString()), [body, 'after'])
})

test('multi-byte content is measured in bytes', () => {
	const [parsed] = read(['aaa', 'příliš žluťoučký'])

	assert.equal(parsed.toString(), 'příliš žluťoučký')
	assert.equal(parsed.length, 23)
})

test('an object git cannot produce is refused, naming the file', () => {
	// A partial clone answers `<sha> missing`, which has no size: read naively that archives this page empty
	// and leaves every later one unaligned.
	assert.throws(
		() => parseBatchStream(Buffer.from('deadbeef missing\n'), [{ sha: 'deadbeef', path: 'guide/index.md' }]),
		/missing.*guide\/index\.md/s)

	assert.throws(() => parseBatchStream(batch(['aaa', 'one']), asked(['aaa', 'one'], ['bbb', 'two'])),
		/stopped after 1 of 2/)
})

test('an answer out of request order is refused rather than mapped', () => {
	// Positional mapping is the whole mechanism: silently accepting this would write each page's text under
	// another page's URL, and every one of those URLs is a real page, so nothing downstream would notice.
	assert.throws(
		() => parseBatchStream(batch(['bbb', 'second']), [{ sha: 'aaa', path: 'guide/index.md' }]),
		/answered for bbb where aaa \(docs\/src\/guide\/index\.md\)/)
})

test('strays reports a root-absolute link to an unknown section', () => {
	assert.deepEqual(strays('[a](/faq/)'), ['/faq/'])
	assert.deepEqual(strays('[a](/guide/)'), [], 'a known section is rewritten, not reported')
	assert.deepEqual(strays('[a](https://example.com/guide/)'), [], 'only root-absolute links')

	// `versioned` rewrites a titled link, so this had to see one too -- otherwise a titled link to a section
	// it does not know stayed pointing at the current version, archived under a frozen URL, unreported.
	assert.deepEqual(strays('[a](/faq/ "FAQ")'), ['/faq/'], 'a title must not hide the destination')
	assert.deepEqual(strays('[a](</faq/>)'), ['/faq/'], 'nor may angle brackets')
	assert.deepEqual(strays('[a](/guide/ "Guide")'), [], 'a known section is still rewritten, not reported')
})

// The three below are the writer's side effects, which the rest of this file deliberately does not have: every
// other case is a pure function. They had been verified once by hand and left nothing behind to notice a
// regression -- disabling prune, or the byte comparison in write, kept the suite green.

const scratch = (body) => {
	const root = mkdtempSync(join(tmpdir(), 'janzen-archive-'))
	try {
		body(root)
	} finally {
		rmSync(root, { recursive: true, force: true })
	}
}

test('an unchanged file is left alone and what the run did not produce is pruned', () => scratch((root) => {
	const page = join(root, 'guide', 'index.html')
	const stale = join(root, 'guide', 'gone', 'index.html')

	mkdirSync(join(root, 'guide', 'gone'), { recursive: true })
	writeFileSync(stale, 'old')

	const { write, prune } = archiveWriter()

	assert.equal(write(page, Buffer.from('one')), true, 'a new file is written')
	assert.equal(write(page, Buffer.from('one')), false, 'identical bytes must not be rewritten')
	assert.equal(write(page, Buffer.from('two')), true, 'changed bytes are written')

	prune(root)

	assert.equal(readFileSync(page, 'utf8'), 'two')
	assert.equal(existsSync(stale), false, 'a page this run did not produce is removed')
	assert.equal(existsSync(join(root, 'guide', 'gone')), false, 'and the directory it emptied goes with it')
}))

test('a case-only rename does not delete the page that replaced it', {
	skip: process.platform === 'win32' ? false : 'needs a case-insensitive filesystem'
}, () => scratch((root) => {
	// NTFS keeps the old directory name through mkdirSync, so the listing and `written` disagree on spelling
	// and an exact compare deleted the file this run had just written.
	mkdirSync(join(root, 'guide', 'FAQ'), { recursive: true })
	writeFileSync(join(root, 'guide', 'FAQ', 'index.html'), 'old')

	const { write, prune } = archiveWriter()

	write(join(root, 'guide', 'faq', 'index.html'), Buffer.from('new'))
	prune(root)

	assert.equal(readFileSync(join(root, 'guide', 'faq', 'index.html'), 'utf8'), 'new')
}))

test('a ref that carries no docs/src yields nothing rather than failing', () => {
	// v10.0.0 predates this site. `git ls-tree` exits 0 with empty output for a path a ref simply does not
	// have, so it is not an error the caller's catch can see: main() reads the empty result and refuses the
	// manifest entry. Without that guard the segment writes nothing and prune deletes the whole archived
	// version, with the run printing its success line and exiting 0.
	assert.deepEqual(blobsAt('v10.0.0'), [])
})

test('each archived version keeps its own navigation, keyed the way the plugin reads it', () => {
	const switcher = { component: 'VersionSwitcher' }
	const root = { nav: [{ text: 'Guide', link: '/guide/' }], sidebar: { '/guide/': [{ text: 'Guide', items: [] }] } }
	const own = { nav: [{ text: 'Old guide', link: '/guide/' }], sidebar: { '/reference/': [{ text: 'Old', items: [] }] } }

	const { nav, sidebar } = versionedNavigation(root, [[SEGMENT, own]], switcher)

	// The bare segment: '/v10.1.x/' is not a nav key the plugin matches, and it falls back to the root nav
	// without a word. Every nav needs its own switcher, or that version's pages lose the version menu.
	assert.deepEqual(Object.keys(nav), ['root', SEGMENT])
	assert.deepEqual(nav[SEGMENT], [...own.nav, switcher])
	assert.deepEqual(nav.root, [...root.nav, switcher])

	// The segment goes in front of the sidebar key only. The links stay as authored because the plugin
	// prefixes them itself -- rewritten here they would publish /v10.1.x/v10.1.x/.
	assert.deepEqual(Object.keys(sidebar), ['/guide/', `/${SEGMENT}/reference/`])
	assert.equal(nav[SEGMENT][0].link, '/guide/')
})

test('a version without a navigation file has none, so the plugin falls back to the root one', () => scratch((root) => {
	assert.equal(navigationIn(root), undefined)

	writeFileSync(join(root, NAVIGATION), JSON.stringify({ nav: [], sidebar: {} }))
	assert.deepEqual(navigationIn(root), { nav: [], sidebar: {} })
}))
