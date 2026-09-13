<script setup lang="ts">
// Adapted from @viteplus/versions' own version-switcher.component.vue, because the ordering and the labels
// are not configurable there: the menu is built straight off the `versions` Set, whose iteration order is
// whatever `readdirSync` returned (alphabetical, so oldest first), and each entry's text *is* its path
// segment. Two consequences this file exists to fix.
//
// **Newest first.** Sorted on the `X.Y` in `v<major>.<minor>.x` rather than as a string, so `v10.9.x` does
// not fall below `v10.10.x` the first time a line reaches double digits.
//
// **`latest` is a label, not a destination.** Upstream offers the current version as its own entry named
// after `versionsConfig.current`, so the menu read `latest`, `v10.0.x`, `v10.1.x` -- three items for two
// lines, with nothing saying that the first two are the same pages. The newest line's archived copy is
// byte-identical to the root while it is current, so the two collapse into one entry, `v10.1.x (Latest)`,
// and it links to the **root**: that is the address the sitemap advertises and the one every page's
// canonical names, so the switcher must not send readers to the duplicate instead.
//
// Vendored rather than wrapped: the labels would otherwise have to be smuggled in through the `versions`
// Set, whose members are also used as path segments. Keep it in step with upstream when the package moves --
// the path building below is theirs, unchanged, including the `/index` shape the canonical tag exists for.
import { computed, onMounted, ref } from 'vue'
import { useData, useRouter } from 'vitepress'
import VPFlyout from 'vitepress/dist/client/theme-default/components/VPFlyout.vue'
import VPMenuLink from 'vitepress/dist/client/theme-default/components/VPMenuLink.vue'

interface VersioningPlugin {
    versions: Set<string>
    currentVersion: string
}

interface Props {
    versioningPlugin: VersioningPlugin
    screenMenu?: boolean
}

const props = defineProps<Props>()
const router = useRouter()
const { site } = useData()
const isOpen = ref(false)

// Rendered only after mount, and that is not a nicety. Everything below reads router and site state --
// `relativePath`, `localeIndex`, and a `versions` Set the plugin injects as a nav-item prop -- which do not
// line up between the server render and the client's first render. Vue then bails out of hydrating the nav
// and re-creates the tree from there down, which left the server's copy of everything after the nav bar
// orphaned in the DOM: a second sidebar, a second content column and a second footer on every page of every
// version. Matching the server (render nothing) until mounted makes the first client render agree, so
// hydration completes and the switcher appears a tick later -- which is invisible for a nav control.
const mounted = ref(false)

onMounted(() => {
	mounted.value = true
})

const LATEST = ' (Latest)'

// `v10.9.x` before `v10.10.x`: compared as strings those two are the wrong way round, and a `Y` bump is
// exactly when this menu gets its next entry.
const rank = (version: string): number => {
    const parsed = /^v(\d+)\.(\d+)\./.exec(version)

    return parsed ? Number(parsed[1]) * 1_000_000 + Number(parsed[2]) : -1
}

const versionSet = computed(() => new Set([...props.versioningPlugin.versions]))
const hasVersions = computed(() => versionSet.value.size > 0)

const ordered = computed(() =>
    [...versionSet.value].sort((a, b) => rank(b) - rank(a) || b.localeCompare(a))
)

const newest = computed(() => ordered.value[0])

const currentLocale = computed(() =>
    site.value.locales[site.value.localeIndex]?.link?.replace(/\//g, '') || ''
)

const pathSegments = computed(() => router.route.data.relativePath.split('/'))

const activeVersion = computed(() => {
    const { currentVersion } = props.versioningPlugin
    const segments = pathSegments.value
    const locale = currentLocale.value

    const versionCandidate = segments[0] === locale ? segments[1] : segments[0]

    return versionSet.value.has(versionCandidate) ? versionCandidate : currentVersion
})

// The root and the newest line's archived copy are the same content, so they read as one place.
const activeLabel = computed(() =>
    activeVersion.value === props.versioningPlugin.currentVersion || activeVersion.value === newest.value
        ? `${newest.value}${LATEST}`
        : activeVersion.value
)

function isLocaleFirst(segments: string[]): boolean {
    return segments[0] === currentLocale.value
}

function removeVersionSegments(segments: string[]): string[] {
    return segments.filter((seg) => !versionSet.value.has(seg))
}

function buildVersionPath(version: string): string {
    const { currentVersion } = props.versioningPlugin
    const baseSegments = removeVersionSegments(pathSegments.value)
    const hasLocale = isLocaleFirst(baseSegments)

    let newSegments: string[]

    if (version === currentVersion) {
        newSegments = baseSegments
    } else if (hasLocale) {
        newSegments = [baseSegments[0], version, ...baseSegments.slice(1)]
    } else {
        newSegments = [version, ...baseSegments]
    }

    return `/${newSegments.join('/')}`
}

// Newest first, and the newest entry points at the root rather than at its own archived duplicate.
const items = computed(() =>
    ordered.value.map((version, index) => index === 0
        ? { text: `${version}${LATEST}`, link: buildVersionPath(props.versioningPlugin.currentVersion) }
        : { text: version, link: buildVersionPath(version) })
)

function toggle(): void {
    isOpen.value = !isOpen.value
}
</script>

<template>
    <template v-if="mounted && hasVersions">
        <!-- Desktop flyout -->
        <VPFlyout
            v-if="!screenMenu"
            class="VPVersionSwitcher"
            icon="vpi-versioning"
            :button="activeLabel"
            label="Switch Version"
        >
            <div class="items">
                <VPMenuLink v-for="item in items" :key="item.text" :item="item" />
            </div>
        </VPFlyout>

        <!-- Mobile dropdown -->
        <div v-else class="VPScreenVersionSwitcher" :class="{ open: isOpen }">
            <button
                class="button"
                type="button"
                aria-controls="navbar-group-version"
                :aria-expanded="isOpen"
                @click="toggle"
            >
                <span class="button-text">
                    <span class="vpi-versioning icon" />
                    Switch Version
                </span>
                <span class="vpi-plus button-icon" />
            </button>

            <div id="navbar-group-version" class="items">
                <VPMenuLink v-for="item in items" :key="item.text" :item="item" />
            </div>
        </div>
    </template>
</template>

<style>
.vpi-versioning.option-icon {
    margin-right: 2px !important;
}

.vpi-versioning {
    --icon: url("data:image/svg+xml;charset=utf-8;base64,PHN2ZyB3aWR0aD0iNjRweCIgaGVpZ2h0PSI2NHB4IiB2aWV3Qm94PSIwIDAgMjQgMjQiIHN0cm9rZS13aWR0aD0iMi4yIiBmaWxsPSJub25lIiB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIGNvbG9yPSIjMDAwMDAwIj48cGF0aCBkPSJNMTcgN0MxOC4xMDQ2IDcgMTkgNi4xMDQ1NyAxOSA1QzE5IDMuODk1NDMgMTguMTA0NiAzIDE3IDNDMTUuODk1NCAzIDE1IDMuODk1NDMgMTUgNUMxNSA2LjEwNDU3IDE1Ljg5NTQgNyAxNyA3WiIgc3Ryb2tlPSIjMDAwMDAwIiBzdHJva2Utd2lkdGg9IjIuMiIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5kIj48L3BhdGg+PHBhdGggZD0iTTcgN0M4LjEwNDU3IDcgOSA2LjEwNDU3IDkgNUM5IDMuODk1NDMgOC4xMDQ1NyAzIDcgM0M1Ljg5NTQzIDMgNSAzLjg5NTQzIDUgNUM1IDYuMTA0NTcgNS44OTU0MyA3IDcgN1oiIHN0cm9rZT0iIzAwMDAwMCIgc3Ryb2tlLXdpZHRoPSIyLjIiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIgc3Ryb2tlLWxpbmVqb2luPSJyb3VuZCI+PC9wYXRoPjxwYXRoIGQ9Ik03IDIxQzguMTA0NTcgMjEgOSAyMC4xMDQ2IDkgMTlDOSAxNy44OTU0IDguMTA0NTcgMTcgNyAxN0M1Ljg5NTQzIDE3IDUgMTcuODk1NCA1IDE5QzUgMjAuMTA0NiA1Ljg5NTQzIDIxIDcgMjFaIiBzdHJva2U9IiMwMDAwMDAiIHN0cm9rZS13aWR0aD0iMi4yIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiPjwvcGF0aD48cGF0aCBkPSJNNyA3VjE3IiBzdHJva2U9IiMwMDAwMDAiIHN0cm9rZS13aWR0aD0iMi4yIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiPjwvcGF0aD48cGF0aCBkPSJNMTcgN1Y4QzE3IDEwLjUgMTUgMTEgMTUgMTFMOSAxM0M5IDEzIDcgMTMuNSA3IDE2VjE3IiBzdHJva2U9IiMwMDAwMDAiIHN0cm9rZS13aWR0aD0iMi4yIiBzdHJva2UtbGluZWNhcD0icm91bmQiIHN0cm9rZS1saW5lam9pbj0icm91bmQiPjwvcGF0aD48L3N2Zz4=");
}
</style>

<style scoped>
.VPVersionSwitcher {
    display: flex;
    align-items: center;
}

.icon {
    padding: 8px;
}

.title {
    padding: 0 24px 0 12px;
    line-height: 32px;
    font-size: 14px;
    font-weight: 700;
    color: var(--vp-c-text-1);
}

.VPScreenVersionSwitcher {
    border-bottom: 1px solid var(--vp-c-divider);
    height: 48px;
    overflow: hidden;
    transition: border-color 0.5s;
}

.VPScreenVersionSwitcher .items {
    visibility: hidden;
}

.VPScreenVersionSwitcher.open {
    padding-bottom: 10px;
    height: auto;
}

.VPScreenVersionSwitcher.open .items {
    visibility: visible;
}

.VPScreenVersionSwitcher.open .button {
    padding-bottom: 6px;
    color: var(--vp-c-brand-1);
}

.VPScreenVersionSwitcher.open .button-icon {
    transform: rotate(45deg);
}

.VPScreenVersionSwitcher button .icon {
    margin-right: 8px;
}

.button {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 4px 11px 0;
    width: 100%;
    line-height: 24px;
    font-size: 14px;
    font-weight: 500;
    color: var(--vp-c-text-1);
    transition: color 0.25s;
}

.button:hover {
    color: var(--vp-c-brand-1);
}

.button-icon {
    transition: transform 0.25s;
}

.group:first-child {
    padding-top: 0;
}

.group + .group,
.group + .item {
    padding-top: 4px;
}
</style>
