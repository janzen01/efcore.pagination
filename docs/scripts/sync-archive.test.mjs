// Covers the pure halves of sync-archive.mjs and src-exclude.mjs.
//
// Every case here is one that was found by review rather than imagined: each had been verified once by hand,
// by editing the config or the manifest, building, and reverting -- which left nothing behind to notice a
// regression. The two srcExclude cases are the ones that matter most, because both failure modes end the same
// way: a page the author excluded is kept out of the site root and published under a frozen version prefix,
// with the build green and nothing downstream able to tell.

import { test } from 'node:test'
import assert from 'node:assert/strict'

import { isSkipped, leaked, parseBatchStream, siteAbsolute, strays, versioned } from './sync-archive.mjs'
import { expandDirectoryPatterns, readSrcExcludePatterns, srcExcludeMatcher } from './src-exclude.mjs'

const SEGMENT = 'v10.1.x'

test('a bare directory pattern excludes what is under it', () => {
	// VitePress hands srcExclude to tinyglobby as `ignore`, where this form excludes the whole directory.
	// Plain picomatch matches only the directory itself, which published the pages it was meant to withhold.
	for (const pattern of ['src/drafts', 'src/drafts/', 'src/drafts/**']) {
		assert.equal(srcExcludeMatcher([pattern])('src/drafts/index.md'), true, pattern)
	}

	assert.equal(srcExcludeMatcher(['src/drafts'])('src/guide/index.md'), false)
})

test('wildcard patterns keep their own meaning', () => {
	const excluded = srcExcludeMatcher(['*.md'])

	assert.equal(excluded('notes.md'), true)
	assert.equal(excluded('src/guide/index.md'), false, 'a top-level pattern must not reach into src/')
})

test('expanding a pattern leaves an existing wildcard alone', () => {
	assert.deepEqual(expandDirectoryPatterns(['src/drafts/**']), ['src/drafts/**'])
	assert.deepEqual(expandDirectoryPatterns(['src/drafts']), ['src/drafts', 'src/drafts/**'])
})

test('srcExclude is read out of a config', () => {
	assert.deepEqual(readSrcExcludePatterns("    srcExclude: ['*.md'],"), ['*.md'])
	assert.deepEqual(readSrcExcludePatterns("srcExclude: [\n  '*.md',\n  'src/drafts/**'\n],"), ['*.md', 'src/drafts/**'])
	assert.deepEqual(readSrcExcludePatterns('    cleanUrls: true,'), [], 'absent means nothing is excluded')
	assert.deepEqual(readSrcExcludePatterns('    srcExclude: [],'), [], 'an empty array is not a parse failure')
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
	assert.deepEqual(
		siteAbsolute('See [it](https://janzen01.github.io/efcore.pagination/guide/).'),
		['https://janzen01.github.io/efcore.pagination/guide/'])

	assert.deepEqual(siteAbsolute('The [Releases](https://github.com/janzen01/efcore.pagination/releases) page.'), [])
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
})
