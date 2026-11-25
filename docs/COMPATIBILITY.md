# Device compatibility

This list records tested device and connection combinations. A recognized vendor or detected USB receiver is not evidence of a working battery reading. Results may depend on firmware, drivers and Battery Monitor version.

[Submit a compatibility report](https://github.com/kross873/BatteryMonitor/issues/new?template=device-compatibility.yml)

| Manufacturer / model | Connection | Status | Evidence |
| --- | --- | --- | --- |
| AJAZZ AJ179 APEX | 2.4 GHz USB receiver / dock | Working | Developer's local testing in earlier builds; not independently verified |
| Logitech G435 | 2.4 GHz USB receiver | Detected, battery unavailable | Developer's local testing in earlier builds |
| Logitech G PRO X Superlight | 2.4 GHz USB receiver | Untested | Awaiting report |
| Razer DeathAdder V3 Pro | 2.4 GHz USB receiver | Untested | Awaiting report |
| Razer Viper V3 Pro | 2.4 GHz USB receiver | Untested | Awaiting report |
| HyperX Cloud III Wireless | 2.4 GHz USB receiver | Untested | Awaiting report |
| Keychron K2 | Bluetooth | Untested | Awaiting report |

Bluetooth Battery Service, standard HID battery fields and Windows battery properties are device-dependent. Do not infer compatibility for an entire brand or all revisions of a model.

Maintainers: link a reviewed report before adding a community-confirmed result; record the tested version and connection. Distinguish a verified percentage from detection alone.
