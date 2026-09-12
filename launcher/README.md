# Frostbound Plus launcher 0.1.0

Windows launcher for the fresh Frostbound Plus Vanilla 1.12.1 (5875), level-60 realm. No game assets or account secrets are included.

The launcher loads `https://github.com/ProducersGamingYEG/frostbound-plus-launcher/releases/latest/download/launcher-manifest.json` on startup. Set `clientDownload` to null until an approved download exists. Existing clients can be imported with **Choose client**. Downloads support ZIP only and require an exact byte size and SHA-256 digest. An interrupted download resumes from the per-user cache; a server that ignores Range safely restarts the download. Extraction runs one archive at a time into a unique staging folder before client validation and final installation.

Manifest shape:

```json
{
  "schemaVersion": 1,
  "launcherVersion": "0.1.0",
  "realmName": "Frostbound Plus",
  "realmAddress": "realm.example.com",
  "clientBuild": 5875,
  "registrationBaseUrl": "https://accounts.example.com",
  "registrationCertificateSha256": null,
  "clientDownload": null
}
```

For an approved ZIP, replace null with `{ "url": "https://…/client.zip", "sha256": "64 hex characters", "sizeBytes": 123, "archiveType": "zip" }`. Public HTTPS is required for service and download URLs. With no certificate fingerprint, Windows certificate validation applies. If configured, the exact SHA-256 DER certificate fingerprint is required and account-service redirects are disabled. Never configure a fingerprint for an untrusted certificate.

The selected installation must contain a versioned `WoW.exe` 1.12.1.5875 and a Data folder. Play sets `realmlist.wtf` and the realmlist setting in `WTF/Config.wtf`, preserving graphics and other settings. WoW launches with the selected installation as its working directory. Passwords and verification codes are never persisted.

Build after committing the candidate version:

```powershell
dotnet build launcher/FrostboundPlus.csproj -c Release -m:2
launcher/bin/Release/net9.0-windows/FrostboundPlus.exe --self-test
dotnet publish launcher/FrostboundPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -m:2 -o launcher/publish
```

`--self-test` makes no external calls or account changes. It writes a pass report beside the executable, or `self-test-error.txt` and exits 1. The checks cover download resume, servers ignoring Range, corrupted checksum rejection, unsafe ZIP traversal, configuration preservation, missing client rejection and localhost URL rejection. Build outputs are not source.
