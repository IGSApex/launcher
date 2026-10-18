# SPT Launcher

Customized [SP-Tushonka launcher](https://github.com/SP-Tushonka/launcher) for the Community, targeting SPT 4.1.6. This repository is the active launcher working directory; `IGSApex/launcher-old` is the earlier implementation.

## Projects

- `SPTushonka.Core`: server communication, account/session handling, game paths and bundle downloads.
- `SPTushonka.Launcher`: MudBlazor launcher UI, server selection, profiles, account dialogs and Mod Manager.

## Launcher accounts and profiles

On servers that enable launcher accounts, selecting the server opens the account login/register flow. Account sessions and account-scoped profiles are managed by [IGSApex/server-csharp](https://github.com/IGSApex/server-csharp). Local/non-account servers continue using the standard profile workflow. Registration, login, profile listing and session validation are server requests, not client-side security boundaries.

**Remember Login** is per server. When selected, the launcher stores a session token and tries to restore it on reconnect; unchecking it removes the remembered credentials. The **current source** writes `%LOCALAPPDATA%/IGSApex/SPTLauncher/remembered-logins.json`. A move to `SPT_Runtime/user/Launcher/LauncherSavedLogin.json` has been requested but is **not implemented in the current source**. Never commit saved session files; treat tokens as credentials. The server can expire or revoke a session.

## Mod Manager

Open **Mod Manager** from the Profiles page. The left column shows files published by the connected server, and the right column shows the corresponding local files. Each manifest entry contains a name, version, relative file path, byte size and SHA-256 digest. The UI currently colors a client version red when the **file hash** differs or the file is missing, even if both displayed versions are identical.

- **Refresh** requests `GET /launcher/v2/client-mods` and scans the local `BepInEx/plugins` and `BepInEx/patchers` trees.
- **Update Mods to Match Server** fetches missing or different files from `/files/client-mod/{relative-path}`, verifies size and SHA-256, backs up existing files as `.backup` and replaces them.
- Relative directories are preserved, e.g. `plugins/Fika/Fika.Core.dll` and `patchers/WTT-ContentBackportPatcher/...`.
- The standard `plugins/spt/` modules and selected SPT patchers are excluded. Additional local mods are shown separately and are **not deleted**.

**Caution:** The server is the source of truth. If a player's locally modified DLL has the same version but a different hash, Update can replace it with the server copy. Back up installations and test with disposable game folders. The download path, backups, and all update behavior still require end-to-end testing. Files are executable code; only connect to trusted servers.

## Related repositories

- [IGSApex/server-csharp](https://github.com/IGSApex/server-csharp) — account APIs and client mod distribution.
- [IGSApex/Fika-Plugin](https://github.com/IGSApex/Fika-Plugin) — multiplayer client and squad UI.
- [IGSApex/Fika-Server-CSharp](https://github.com/IGSApex/Fika-Server-CSharp) — multiplayer server and squad state.

This is a customized community fork; upstream functionality and credits remain with their respective authors.
