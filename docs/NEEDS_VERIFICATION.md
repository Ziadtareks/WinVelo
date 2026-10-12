# Needs Verification

The following tweaks and settings are pending verification on real Windows builds before being added to the active catalog:

| ID / Setting | Proposed Category | Notes / Missing Info | Status |
|---|---|---|---|
| `network.tcp.autotuning-experimental` | network | Requires validation across Win 10 (22H2) and Win 11 (23H2/24H2) network stacks. | Pending |
| `performance.system.disable-startup-delay` | performance | Community tweak (`StartupDelayInMSec` under Explorer\Serialize). Lacks official Microsoft Learn documentation. | Pending |
| `performance.system.clear-pagefile-shutdown` | performance | High risk of slow shutdown; requires testing on systems with large RAM/pagefile configurations. | Pending |
| `performance.memory.svchost-split-override` | performance | Overriding service host splitting behavior on modern Windows requires testing to avoid service crashes. | Pending |
| `system.kernel.mitigations-speculative-execution` | system | Disabling CPU vulnerability mitigations is dangerous and pending isolation. | Pending |
| `ui.explorer.compact-view` | ui | Relies on undocumented File Explorer state flags. Lacks official MDM/CSP or MS Learn documentation. | Pending |
| `ui.system.disable-snap-assist` | ui | Undocumented registry values for Snap Assist layout popups across Windows 11 feature updates. | Pending |
