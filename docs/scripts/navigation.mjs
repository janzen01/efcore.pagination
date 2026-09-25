// The site's navigation lives in docs/src/navigation.json rather than in .vitepress/config.mts, so it is
// versioned with the pages it links: scripts/sync-archive.mjs copies it into each archived line verbatim, and
// every version is then rendered with the sidebar it was written against. While config.mts owned it, one
// sidebar served every version, so the first page the newest line added, renamed or dropped would have
// shown up -- or gone missing -- in every older copy too.
//
// A tag from before this file existed (v10.1.1 and older) simply has no copy, and the plugin falls back to
// the root navigation for it, which is exactly what those versions were built with.

import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

export const NAVIGATION = 'navigation.json'

// The navigation a directory carries, or undefined when it has none.
export function navigationIn(directory) {
	const file = join(directory, NAVIGATION)
	return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : undefined
}

// Keyed the way @viteplus/versions reads it: `nav` by locale -- 'root', or the bare segment, since a key
// like '/v10.1.x/' falls back to the root nav without a word -- and `sidebar` by path with the segment in
// front. The plugin puts the segment in front of every link itself (a `base` on each sidebar group, a
// joined path on each nav link), so the links stay exactly as authored: rewriting them here would publish
// /v10.1.x/v10.1.x/... everywhere. Each nav carries its own switcher, because the plugin only injects the
// version list into a component item it finds in that locale's nav.
export function versionedNavigation(root, archives, switcher) {
	const nav = { root: [...root.nav, switcher] }
	const sidebar = { ...root.sidebar }

	for (const [segment, own] of archives) {
		nav[segment] = [...own.nav, switcher]
		for (const [path, groups] of Object.entries(own.sidebar)) sidebar[`/${segment}${path}`] = groups
	}

	return { nav, sidebar }
}
