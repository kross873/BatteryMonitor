# Interactive Domovoi — prototype

This is a cosmetic mascot inside the settings window, shared across the four existing content tabs. It does not modify the taskbar widget, scanner, update protocol or installer. It is enabled by default for this prototype. Right-click the mascot to hide/reset it or toggle physics; the Appearance checkbox previews visibility immediately. Use Apply to save visibility. Position is saved locally when movement settles (or on release if physics is off) and restored proportionally when the content area resizes.

## Windows smoke test (not executed here)

1. On Windows x64 with the .NET 8 SDK, run build-windows.cmd in this branch. Start the portable output, without replacing your existing installed build.
2. Open Settings; check the character's initial position and drag it to all four edges. Release the mouse outside the window; it should remain within the content area.
3. Navigate through Devices, Appearance, Updates and Compatibility; the mascot should remain at the same position and not reset on a device refresh.
4. Resize the window and test 100% and 150% display scaling; the mascot should remain within bounds. Check light/dark themes and RU/UK/EN.
5. Right-click to reset, then hide; verify controls previously under the mascot are reachable. Re-enable it in Appearance and press Apply. Close and reopen; verify visibility and position persistence.
6. Verify normal device refresh, compatibility links, appearance controls, update display and tray widget still work. If any connected device reports 0–20%, check the mascot's worried eyebrows. Unknown readings should not trigger the expression.
7. Run test-updates.cmd if available in this checkout. No production installer or update manifest is changed by this branch.

Known limitations: series-derived vector art with simplified pose animation, no full-body idle breathing in this revision, no autonomous walking, no charge-state reaction, no keyboard dragging. Windows compilation and interaction tests remain required.

## Cartoon model revision

The mascot uses the same original head and body contours, palette and three eye poses as the animated series. Check that the shaped hit area, outlines and swinging feet render without clipping or excessive background seams at different DPI settings. The mascot remains within the current content area; no desktop overlay is created.

## Physics and repaint revision

With physics enabled (default), pick up and release the mascot to fall toward the bottom of the active content area. A quick mouse movement before release transfers momentum; walls and floor bounce with damping. Holding still for at least 100 ms before release clears throw velocity. Right-click and disable Domovoi physics to keep the mascot wherever it is dropped. The preference is stored locally. No collision with device cards or buttons is simulated.

Regression test on Windows: drag slowly and quickly across dark/light cards, labels, buttons and empty space; release, let the mascot bounce and settle; toggle physics; hide and re-show it; resize; switch tabs mid-motion; minimize/restore; close/reopen. Check that old pixels disappear and the mascot stays in bounds. This uses clipped native child-window redraw of old/new bounds; actual WinForms visual testing remains required.
