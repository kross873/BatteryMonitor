# Interactive Domovoi — character animation revision

The mascot lives in the four content pages. It has no network calls or battery polling of its own. The production installer, update manifest and version remain unchanged.

## Behavior

- Drag the mascot, then release it anywhere. It stays there; no gravity or bouncing across the app.
- With physics enabled (default), fast mouse movement tilts the character; continuous circular gestures spin it. Angular and limb springs settle after release. The hat, beard and face are preserved from the series model; hands and feet have separate joint motion.
- A short horizontal walk occurs after around 12 seconds without mascot interaction, then occasionally until sleep. Hovering, clicking or opening the mascot menu pauses the walk. About 48 seconds of no mascot interaction gives a sleepy expression. Double-click for a happy expression; rapid rotation and release can give dizzy eyes; connected low battery gives worried eyes.
- Right-click to toggle pose physics or autonomous walking independently, hide or reset. Position is saved when released and when a walk ends. Apply saves visibility. Preferences are local.
- Alpha-derived hit regions follow each pose. Old and new rectangles are invalidated including underlying child controls, to prevent ghost trails. Windows visual confirmation is still required.

## Windows test (not run here)

1. Build with build-windows.cmd on Windows .NET 8, exit the old app and run publish/BatteryMonitor.exe.
2. Slowly drag over cards, text and buttons; no old copies should remain. Move fast left/right and then in circles; limbs should move and the body should rotate. Release at mid-window; it must stay there and settle upright.
3. Leave the mouse off the mascot for 12–15 seconds; check short walking cycle, alternating feet and pause on hover. Wait about 48 seconds for sleepy eyes. Double-click, spin and release, and test a connected device reporting <=20%.
4. Disable physics and walking independently in the right-click menu, verify both settings persist, then enable again. Hide/show in Appearance and Apply. Resize, switch tabs while moving, minimize, restore, test light/dark and different DPI settings.
5. Verify device refresh, taskbar widget, compatibility and update screens are unchanged. Run test-updates.cmd.

Limitations: 2D articulated artwork, no simulated collision with cards/buttons, no autonomous movement outside the content pages, no charging reaction. Rendering uses shaped WinForms controls, so actual repaint performance and edge quality must be tested on Windows. No binary built here.
