# Trazo Admin Design System

The shared design system preserves the existing dark admin-console visual language. New tokens and primitives are the default for new work; existing feature styles are migrated incrementally rather than rewritten in one pass.

## Foundations

Global CSS custom properties live in `src/styles/_design-tokens.scss` and are loaded by `src/styles.scss`.

- Surfaces: `--trazo-bg`, `--trazo-surface`, `--trazo-surface-raised`, `--trazo-surface-hover`
- Text: `--trazo-text`, `--trazo-text-soft`, `--trazo-text-muted`
- Semantic colors: `--trazo-info`, `--trazo-success`, `--trazo-warning`, `--trazo-danger`
- Shape and layout: `--trazo-radius-*`, `--trazo-space-*`, `--trazo-content-max`
- Accessibility: `--trazo-focus-ring`

## Shared standalone primitives

Import only the primitives a standalone feature uses from `src/app/shared/ui`:

| Component | Purpose |
| --- | --- |
| `UiCardComponent` / `<trazo-card>` | Consistent surface, border, radius and padding |
| `UiButtonComponent` / `<trazo-button>` | Button variants, disabled/loading behavior and focus ring |
| `UiBadgeComponent` / `<trazo-badge>` | Neutral and semantic status labels |
| `UiFormFieldComponent` / `<trazo-form-field>` | Label, projected control, hint and validation error |
| `UiStateComponent` / `<trazo-state>` | Loading, empty, error and success feedback |
| `UiTableComponent` / `<trazo-table>` | Responsive table wrapper and consistent table typography |

## Usage rules

- Feature-specific styling may use the explicit modifier inputs `contentClass`, `fieldClass`, `buttonClass`, `badgeClass` and `tableClass`; keep semantic variants/tones in the shared component and use modifier classes only for page-specific layout or legacy styling during migration.
- `UiCardComponent` renders a semantic `article`; use `ariaLabelledBy` when the card has a visible heading that labels it.
- Prefer tokens over introducing new literal colors, spacing and radii.
- Use `primary` for the main action, `secondary` for neutral actions, `danger` for destructive actions and `ghost` for low-emphasis actions.
- Always associate a form label with the projected control using `controlId`.
- Use `error` state for actionable failures and `empty` state for a valid query with no results; do not conflate either with loading.
- Tables must retain semantic `thead`, `tbody`, `th` and `td` markup.
- Preserve keyboard focus visibility and responsive overflow behavior.
- Feature-specific layouts remain in feature styles; primitives define shared visual language, not page layout.
