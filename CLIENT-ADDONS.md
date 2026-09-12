# Optional Vanilla interface addons

Use addons for original Vanilla 1.12.1 / Interface 11200, not retail or Classic 1.13.

## Quest helper

[pfQuest 7.0.1](https://github.com/shagu/pfQuest/releases/tag/7.0.1) provides quest locations and objectives. Use the Vanilla full-language release and place its pfQuest folder inside Interface/AddOns. At character selection, choose AddOns and set Script Memory to 0 (no addon-memory cap), as required by the upstream setup guide. Fully exit and relaunch after adding new folders. Use /db show to open its interface.

## ElvUI

The local Frostbound setup uses [ElvUI Modernized](https://github.com/Bluewhale1337/ElvUIModernized/tree/7acad19ae8274eeffd316087bce0b3ea8b92f686), a Vanilla backport: ElvUI0.85, Config1.01. Install !Compatibility, ElvUI, and ElvUI_Config together. Fully relaunch, then use /ec. Preserve your existing saved settings and graphics. The locally prepared copy disables automatic UI scaling to preserve the chosen scale. Optional APIs require an in-game compatibility check on the unmodified5875 client; no DLL/client patch is part of this setup.

The launcher game download comes from the selected SoloCraft mirror. These optional addons are separate; game installation does not silently install them or overwrite an existing UI.
