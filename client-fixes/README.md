# Client display fix

`!FrostboundVideoFix` is an original, narrow Vanilla 1.12 addon. It keeps the resolution dropdown within the stock 32-button capacity and retains the current mode while translating selected menu IDs back to driver IDs. It does not write graphics CVars or globally suppress errors.

Evidence: the client's only existing third-party addon, SoloCraft PCP 1.2.0, contains no dropdown hooks. Vanilla's `UIDropDownMenu_AddButton` reaches line 156 when the number of buttons exceeds `UIDROPDOWNMENU_MAXBUTTONS=32`; its diagnostic attempts to concatenate the nil open-menu name during initialization. `OptionsFrameResolutionDropDown_LoadResolutions` adds one entry per driver resolution without limiting the count.

References inspected: https://github.com/MOUZU/Blizzard-WoW-Interface/tree/master/1.12.1/FrameXML and https://github.com/jhinzuo/another.ScreenResolutionDropdownFix . This implementation does not include that addon's unrelated hooks or general login error suppression.

Install only with WoW closed, after backing up the current configuration and addon inventory. A new addon folder requires a full client relaunch to discover reliably. After launch, `/fbvideo` reports driver and menu counts. Open Video Options, verify its dropdown, change a resolution and cancel/confirm as appropriate to validate mapping. Restore the backup config and remove this newly added addon folder to roll back. No server restart is needed.
