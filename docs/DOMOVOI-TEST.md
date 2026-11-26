# Domovoi — anchored ragdoll / layered rendering prototype

This revision replaces motion-gesture rotation with physical angular suspension from the selected point of the actual painted body. The existing artwork, autonomous walking, moods, hide/reset and independent physics/walking switches are retained. No linear falling or bouncing.

- Each painted limb has a matching local-space transform and hit test. Grab the hat, torso, hand or boot. The center of mass hangs below the grabbed point. The selected point follows the mouse while other joints move. Fast mouse acceleration drives angular and limb motion. After release the character returns upright at its drop location (clamped near edges). At viewport edges the character may be partly clipped while held.
- Rendering now uses a small layered child HWND with a persistent 32-bit premultiplied-alpha DIB and cached vector paths. No region scan, new bitmap, or synchronous parent redraw per frame. A single 16 ms UI timer samples input, integrates in smaller steps and presents the pose. This targets up to 60 FPS; actual Windows timer/compositor speed varies. The manifest declares Windows 10 compatibility as required for layered child windows. Renderer errors hide the mascot and show an error rather than crash the monitor.
- Windows testing required: drag by each boot, each hand and top of hat; hold stationary and check the selected pixel stays under the pointer, and feet hang above the head. Move cursor rapidly and in circles; body should swing naturally. Release at center and edges; verify no trails, clipped sprite stays in bounds after release, background controls work through transparent areas, animation does not cover the desktop. Test at 100/150% DPI and light/dark themes. Check CPU, responsiveness, hide/show, tab switching and restart, walking and moods. Test normal battery updates and taskbar widget. Run test-updates.cmd.
- This is a 2D articulated body with one central angular mass and animated joints, not a full multi-body ragdoll solver. No collisions with cards or buttons. Windows build and UI test have not been run here.

## Polished interaction revision

The existing anchored drag and layered renderer are retained. Joint poses blend exponentially across mood changes; the actual blended pose is shared by grip hit testing and pinning. Angular spring damping and input filtering are retuned, active frames present on every UI timer tick (target about 60 FPS, not guaranteed), and walking starts/stops with a short speed ramp. No gravity after release.

New interactions: quick single click waves; double click gives happy eyes; repeated quick clicks give annoyed eyes; move the mouse back and forth across the hat/head without clicking for a petting reaction. Right-click > Domovoi actions: Wave, Dance, Stretch, Sleep. With autonomous walking enabled, occasional spontaneous short actions also occur. Physics and walking switches remain separate. Test each grab point during transitions, continuous dragging, edge clipping, quick click versus drag, all menu actions, sleep and walk; confirm no trails and compare responsiveness to previous commit. Actual Windows build and UI tests not run here.

## Character rig and rest actions

The previous alpha-layer renderer and body suspension remain. Four separate limb springs, a head spring and two secondary springs for hat and beard use different stiffness/damping values. The hat/beard transforms are also used by grip hit-testing and pinning. No gravity translation. Cursor tracking is local to the viewport and interpolates eye movement. Rest, tea, coding and explicit device-card perch are new actions; tea/coding are animated vector props, not functional apps. The perch resolves a visible card on the Devices page, follows its edge for a short time, cancels on tab/drag or disappearance, and is disabled when no suitable card fits. No full collision solver. Test the hat/boots/arms while rotated, including grabbing during animations, and verify all previous functions, menu translations and no visual trails. Check gaze, headwear and beard, props, walking and perch on scroll/resize. Windows build/visual testing still required.

## Community signs

Three explicit menu actions in Domovoi actions show GitHub (BatteryMonitor repository), TikTok (@domovoi.dev), and Telegram (@podkrovatyuuu). Each sign rises from the hands, holds for a few seconds, and retracts. The sign is rendered from the blended hand pose; text stays readable when mirrored. Signs can appear occasionally during autonomous idle behavior, with at least 120 seconds between sign starts; no links open automatically. A deliberate press and release on the fully revealed sign opens only its fixed HTTPS URL in the default browser. Moving the mouse while pressing cancels the link, and dragging the character outside the sign remains unchanged. Test all three menu actions, links, mirrored facing, dragging hat/limbs outside board, press-drag-release cancellation, capture loss, hide and resize. Windows compilation and visual tests not run here.


## Night Workshop visual pass (DEV)

- Verify all four settings pages in light/dark/system themes, RU/UK/EN, 100%/150% DPI, minimum window size and resize.
- Check rounded buttons (mouse, keyboard Tab/Enter/Space, disabled state), navigation selection and focus, device-card wrapping, real 0/20/100% and unknown values, no clipped controls or unreadable labels.
- Retest mascot dragging, transparent hit testing, perch on the resized device cards, all actions and links. No mascot engine changes are intended.
- Verify diagnostics, appearance preview, save/restart, update and compatibility workflows. Windows visual/build verification pending. No release publication.


## Assistant module (DEV)

- Feed only completed snapshots; validate thresholds 20/10/5, hysteresis reset at 25, unknown and failed scans, initial low charge, multiple devices, two missing scans, rising readings and 100% without claiming charging status.
- Check no repeated warnings for unchanged readings. Check session forecast only after 5 samples, 30 min and 5% decline; resets on rises, missing values and scan failures. No data transmitted or saved to disk.
- Check the assistant strip, details dialog, targeted card scrolling/highlight, clickable battery board, quick menu, all three languages and both themes. Check DND and opt-in outside-window alerts (no focus steal, rate limit, closes itself, click opens actual device).
- Check manual/automatic update notice, compatibility navigation, diagnostics, existing social signs and grip physics. No release until build and Windows test.


## Unique assistant choreography (DEV)

- Trigger each threshold 20/10/5: amber nod, orange urgent motion, red SOS crouch. Check exact charge, same sign hit geometry, release-to-click, mirrored labels.
- Verify rising (upward arrow), full (celebration), found (device check), missing (search), compatibility (magnifier), update (parcel), appearance (sliders), guide (pointing). Social signs and sitting/perch use distinct poses. Outside-window alerts reuse the correct threshold choreography in miniature.
- Check scene transitions, no clipping, dragging/physics, no extra polling or changes to real readings. Windows compile and visual check pending.
