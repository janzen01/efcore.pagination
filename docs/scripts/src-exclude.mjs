// The single reading of `srcExclude`, shared by sync-archive.mjs and verify-anchors.mjs.
//
// It exists because the same value was being interpreted three different ways. VitePress hands `srcExclude` to
// tinyglobby as its `ignore` list; this module has to agree with that, because the scripts use it to decide
// which pages are real. Where they disagreed, a page excluded from the site root was copied into every
// archived version and published there under a frozen URL -- with a green build, since nothing downstream can
// tell an unwanted page from a wanted one.
//
// Two behaviours are load-bearing and neither is what plain picomatch does on its own:
//
//   * A bare directory excludes everything under it. `ignore: ['src/drafts']` keeps `src/drafts/index.md` out
//     of a tinyglobby result, while `picomatch('src/drafts')` does not match that path. Measured against the
//     tinyglobby VitePress actually calls, for `src/drafts`, `src/drafts/` and `src/drafts/**`.
//   * A config this parser cannot read is an error, not an empty list. Reading it as "nothing is excluded"
//     fails in the direction that publishes the pages, which is the defect this module was written to close.
//
// Patterns are matched against paths **relative to docs/**, because the config sets no `srcDir` and VitePress
// therefore treats docs/ itself as the source root.

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

import picomatch from 'picomatch'

const docs = join(dirname(fileURLToPath(import.meta.url)), '..')

const configPath = join(docs, '.vitepress', 'config.mts')

/**
 * Scrapes the `srcExclude` array out of the VitePress config. Throws when the option is present but cannot be
 * read, so a reformatting that defeats the regex fails the build instead of silently excluding nothing.
 */
export const readSrcExcludePatterns = (config) => {
	// Anchored to the start of a line, so a `//`-commented occurrence cannot win over the real option. This
	// config is half prose and its comments quote option values verbatim -- `srcExclude: ['cs/**']` is a line
	// it used to carry -- and an unanchored match took the first one anywhere in the file. That is the same
	// fail-open this module exists to close: the scripts would exclude one list while VitePress applied another.
	const list = config.match(/^\s*srcExclude:\s*\[([^\]]*)\]/m)?.[1]

	if (list === undefined) {
		if (/\bsrcExclude\b/.test(config)) {
			throw new Error('`srcExclude` is in the config but not as a readable array literal. ' +
				'This parser reads quoted strings inside a `srcExclude: [ ... ]` that starts a line; a ' +
				'variable, a spread, a template literal or an option written inline after something else ' +
				'defeats it, and treating that as "nothing is excluded" publishes the pages the option ' +
				'exists to withhold.')
		}

		return []
	}

	const patterns = [...list.matchAll(/['"]([^'"]+)['"]/g)].map(([, pattern]) => pattern)

	if (patterns.length === 0 && list.trim() !== '') {
		throw new Error(`\`srcExclude\` holds something this parser cannot read: ${list.trim()}. ` +
			'Only quoted string literals are understood.')
	}

	return patterns
}

/**
 * Adds the `/**` form of every directory-shaped pattern, which is what makes this agree with the glob
 * VitePress runs. Harmless for a pattern that is already a wildcard.
 */
export const expandDirectoryPatterns = (patterns) => patterns.flatMap((pattern) => {
	const trimmed = pattern.replace(/\/+$/, '')

	// Only an already-recursive pattern is left alone. `endsWith('*')` was too broad: it also caught a
	// wildcard *directory* like `src/draft*`, which tinyglobby prunes whole and picomatch does not, so the
	// pages under it stayed excluded from the site root and were published under a frozen version prefix.
	return trimmed.endsWith('**') ? [pattern] : [pattern, `${trimmed}/**`]
})

/** A predicate over docs-root-relative paths. Never matches when nothing is excluded. */
export const srcExcludeMatcher = (patterns) =>
	patterns.length === 0 ? () => false : picomatch(expandDirectoryPatterns(patterns))

/**
 * The matcher for this repository's own config, which is what both scripts want. Both are command-line tools
 * that can only abort here, so the failure is reported once, in one shape -- left to throw, it reached one
 * script as a clean diagnostic and the other as a raw stack naming a file the reader never ran.
 */
export const configuredSrcExclude = () => {
	try {
		return srcExcludeMatcher(readSrcExcludePatterns(readFileSync(configPath, 'utf8')))
	} catch (error) {
		console.error('\nCannot read `srcExclude` from .vitepress/config.mts.\n')
		console.error(`${error.message}\n`)
		process.exit(1)
	}
}
