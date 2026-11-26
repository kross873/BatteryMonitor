# Interactive Domovoi — prototype

This is a cosmetic mascot inside the settings window, shared across the four existing content tabs. It does not modify the taskbar widget, scanner, update protocol or installer. It is enabled by default for this prototype. Right-click the mascot to hide/reset it; the Appearance checkbox previews visibility immediately. Use Apply to save visibility. Drag position is saved locally on release and restored proportionally when the content area resizes.

## Windows smoke test (not executed here)

1. On Windows x64 with the .NET 8 SDK, run build-windows.cmd in this branch. Start the portable output, without replacing your existing installed build.
2. Open Settings; check the character's initial position and drag it to all four edges. Release the mouse outside the window; it should remain within the content area.
3. Navigate through Devices, Appearance, Updates and Compatibility; the mascot should remain at the same position and not reset on a device refresh.
4. Resize the window and test 100% and 150% display scaling; the mascot should remain within bounds. Check light/dark themes and RU/UK/EN.
5. Right-click to reset, then hide; verify controls previously under the mascot are reachable. Re-enable it in Appearance and press Apply. Close and reopen; verify visibility and position persistence.
6. Verify normal device refresh, compatibility links, appearance controls, update display and tray widget still work. If any connected device reports 0–20%, check the mascot's worried eyebrows. Unknown readings should not trigger the expression.
7. Run test-updates.cmd if available in this checkout. No production installer or update manifest is changed by this branch.

Known limitations: custom vector art rather than the final mascot sprites, simple idle animation, no autonomous walking, no charge-state reaction, no keyboard dragging. Windows compilation and interaction tests remain required.
