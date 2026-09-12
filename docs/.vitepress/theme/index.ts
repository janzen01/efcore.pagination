import { h } from 'vue'

import DefaultTheme from 'vitepress/theme'

import {
    NolebaseEnhancedReadabilitiesMenu,
    NolebaseEnhancedReadabilitiesScreenMenu
} from '@nolebase/vitepress-plugin-enhanced-readabilities/client'

import '@nolebase/vitepress-plugin-enhanced-readabilities/client/style.css'

import VersionSwitcher from '@viteplus/versions/components/version-switcher.component.vue'

import './mermaid.css'

// The default theme, plus the stylesheet next to this file. vitepress-plugin-mermaid registers its
// component through a Vite alias rather than through the theme, so extending here does not disturb it.
//
// Nolebase enhanced-readabilities adds a reading-preferences menu: Layout Switch, which lets a reader
// widen the content to full width -- worth having on a reference site whose pages carry wide SQL blocks
// and eight-column tables -- and Spotlight, which dims everything but the line under the cursor. Both are
// per-reader and persisted client-side; nothing about the published pages changes.
//
// VersionSwitcher is referenced by name from the nav in config.mts (`{ component: 'VersionSwitcher' }`), which
// is why it has to be registered globally here rather than imported where it is used.
export default {
    extends: DefaultTheme,
    Layout: () =>
        h(DefaultTheme.Layout, null, {
            'nav-bar-content-after': () => h(NolebaseEnhancedReadabilitiesMenu),
            'nav-screen-content-after': () => h(NolebaseEnhancedReadabilitiesScreenMenu)
        }),
    enhanceApp: ({ app }) => {
        app.component('VersionSwitcher', VersionSwitcher)
    }
}
