# WarcraftPlugin patches

*By ServerTracker.live (aka RevoltLive) — [wc2.servertracker.live](https://wc2.servertracker.live)*

WC2 Framework is designed to sit entirely on top of WarcraftPlugin without
touching it. In practice, this deployment carries a small number of direct,
focused patches to WarcraftPlugin itself — listed here in full so they're easy
to re-apply after pulling upstream updates, and so upstream can consider
merging any of them directly.

## 1. Shadowblade model path fix
The original model path (`ctm_st6_variantn`) doesn't resolve on CS2 (removed/
renamed asset). Repointed to a working model.
File: `Classes/Shadowblade.cs`

## 2. `DefaultModel` + `PreloadResources` added for all 12 classes
The base plugin didn't specify explicit per-class models/preload lists.
Every class now has an assigned custom model with its resources preloaded.
Files: `Classes/*.cs` (one entry per class)

## 3. Class model gets stomped by CS2's own bot re-dress behavior
**Symptom:** a bot's class-specific model would revert to a generic model
shortly after spawn.
**Cause:** CS2 automatically re-applies a bot's model ~0.3s after spawn (an
engine-level "redress" step), which raced against and overwrote any custom
model set earlier.
**Fix:** `SetDefaultAppearance` now does a delayed (+0.35s) re-apply that reads
`DefaultModel` **live** at that moment, rather than once at spawn time. This
also protects features like Shapeshifter, which swap models at runtime — a
snapshot-based re-apply would have stomped those too.
File: `Models/WarcraftClass.cs`

## 4. Missing "Close" option on `!class` and `!skills` menus
Both menus let you pick a class/spend skill points but had no way to back out
without spending something or disconnecting.
**Fix:** added a `"Close"` option that calls `MenuManager.CloseMenu(player)`,
matching the pattern already used internally when a selection is made.
Files: `Menu/WarcraftMenu/ClassMenu.cs`, `Menu/WarcraftMenu/SkillsMenu.cs`

**Note on `SkillsMenu` specifically:** this menu renders through a fixed-size
CenterHtml panel with no built-in scrolling. With enough abilities + long
descriptions, content silently overflows past the visible area. If you have
more than ~4-5 abilities per class with full descriptions, consider:
- Shrinking description font size (`FontSizes.FontSizeXs`)
- Or paginating manually (show N abilities per page, add "Next/Previous Page"
  options) — we prototyped this but reverted it after a crash tied to rapid
  page-navigation clicks; the cause was never fully root-caused, so if you
  revisit pagination here, test thoroughly under repeated rapid navigation
  before trusting it in production.

## Applying these patches
All are small, self-contained diffs against individual files — no shared
infrastructure changes. Diff each file against a clean upstream checkout to
extract a patch, or reference the descriptions above to reapply by hand after
an upstream update.
