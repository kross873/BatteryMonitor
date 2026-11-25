# Third-party acknowledgements and licensing

Battery Monitor's own source is licensed under GPL-3.0-only (see [LICENSE](../LICENSE)). Third-party material retains its respective copyright and license.

## AJAZZ Control Center

- Upstream: [Aiacos/ajazz-control-center](https://github.com/Aiacos/ajazz-control-center).
- Copyright (C) 2026 Lorenzo Argentieri and AJAZZ Control Center contributors, as stated in the upstream LICENSE.
- Upstream license: GPL-3.0-or-later ([upstream notice](https://github.com/Aiacos/ajazz-control-center/blob/develop/LICENSE)).
- The AJAZZ battery provider references upstream AJ-series protocol research. The current upstream license is compatible with GPLv3. The original protocol-reference revision has not been independently reconstructed; this acknowledgement is not a claim that the projects contain identical code.

## Taskbar Media Widget

- Author: uMk0 ([GitHub profile](https://github.com/umk0)).
- Upstream: [Taskbar Media Widget](https://windhawk.net/mods/taskbar-media-widget), [source](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-media-widget.wh.cpp).
- The inspected upstream mod v0.3.5 declares MIT in its source header; its Git blob is dc33117de284585fb89e668baaca51aabe152d63.
- The Battery Monitor Windhawk mod references its native XAML host-discovery approach. See [MIT notice](TASKBAR-MEDIA-WIDGET-MIT.txt). The exact earlier reference revision has not been independently established.

## Runtime and tools

- .NET and Windows Forms: [license information](https://github.com/dotnet/core/blob/main/license-information.md). Preserve the license and third-party notices accompanying the actual .NET runtime packs used for a self-contained release. Those components retain their own terms.
- [Windhawk](https://github.com/ramensoftware/windhawk) is a separately installed tool, not bundled by this installer. Its own GPLv3 license applies.
- [Inno Setup](https://jrsoftware.org/isinfo.php) is a separate build tool. Review the license of the actual compiler version used, including its redistribution terms and commercial-use licensing guidance.
- Windows APIs and Microsoft/product names are referenced for compatibility, not endorsement.

## Binary release checklist

Publish the exact corresponding Battery Monitor source for each distributed GPL-covered build, including installer scripts, build instructions and applicable notices. Keep private signing keys outside the repository; they are not needed to build an independent copy. Preserve runtime notices and include the project license and third-party acknowledgements in each installer or portable distribution. Review the actual built files before publishing. This source-level review does not certify the contents of any previously uploaded executable.
