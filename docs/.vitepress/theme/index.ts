import DefaultTheme from 'vitepress/theme'

import './mermaid.css'

// The default theme, plus the stylesheet next to this file. vitepress-plugin-mermaid registers its
// component through a Vite alias rather than through the theme, so extending here does not disturb it.
export default DefaultTheme
