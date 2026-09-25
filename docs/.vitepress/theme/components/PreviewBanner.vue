<script setup lang="ts">
// Shown at the site root while the newest release is a prerelease. The root is the newest release of any line,
// so during a new major's preview a reader arriving from a bare URL -- or from an older package whose project
// link still names the root -- would otherwise take preview documentation for the release they installed.
// The newer packages point at their own versioned copy and never land here by accident.
//
// Only in the root locale: every archived version inherits the top-level themeConfig, and a banner there would
// call a stable line's own pages a preview. The data is `themeConfig.release`, derived in config.mts.
//
// Rendered only after mount, for the reason VersionSwitcher.vue spells out: `localeIndex` does not line up
// between the server render and the client's first one, and a mismatch makes Vue re-create the page below it.
import { computed, onMounted, ref } from 'vue'
import { useData, withBase } from 'vitepress'

const { site, theme } = useData()
const mounted = ref(false)

onMounted(() => {
    mounted.value = true
})

const release = computed(() => theme.value.release)

const visible = computed(() =>
    mounted.value && site.value.localeIndex === 'root' && release.value?.prerelease && release.value?.stable !== undefined
)
</script>

<template>
    <div v-if="visible" class="preview-banner">
        This is the documentation for <strong>{{ release.version }}</strong>, a prerelease. The stable line is
        <a :href="withBase(`/${release.stable}/`)">{{ release.stable }}</a>.
    </div>
</template>

<style scoped>
.preview-banner {
    margin: 0 0 24px;
    padding: 8px 16px;
    border-left: 4px solid var(--vp-c-warning-1);
    border-radius: 8px;
    background-color: var(--vp-c-warning-soft);
    color: var(--vp-c-text-1);
    font-size: 14px;
    line-height: 24px;
}

.preview-banner a {
    color: var(--vp-c-brand-1);
    font-weight: 500;
    text-decoration: underline;
}
</style>
