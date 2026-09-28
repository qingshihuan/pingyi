# Browser extension visual contract

The approved product name is 截屏释义 (Screen Insight). Keep the reference popup's compact hierarchy and two-column tools; colors now follow the desktop app's `ApplePalette.axaml` resources.

- Width 380 px; 22 px gutters; 12–16 px panel radii; no remote fonts/assets.
- Light: page #F8F9FC, card #FFFFFF, text #1D1D1F, muted #62646C, border #E0E2E8, brand #0865D9.
- Dark: page #191B20, card #23252B, text #F2F2F7, muted #AFB2BE, border #3D404A, brand #75ADFF.
- Both themes: primary action #0765DB, hover #0756BB, focus #408AEE. White primary-button text. `theme.css` follows `prefers-color-scheme`.
- Approved icon: blue/orange/white intertwined speech shapes with black framing and right panel. Use a white rounded backing in dark headers so black edges remain visible.
- Primary action is webpage translation. Language direction, desktop provider and bilingual/translation-only choices remain visible.
- Service configuration stays in the desktop app; connection errors and upload boundaries use plain language.
- Only real functionality is shown: no login, subscription, document translation or AI upgrade decoration.
- Hover uses Alt + dwell, selection offers an explicit button, and remote screenshot confirmation is separate.
- Visible keyboard focus, labeled controls, reduced motion and safe text rendering apply.
