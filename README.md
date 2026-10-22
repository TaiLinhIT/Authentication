# Warehouse License Studio — offline WPF licensing

A complete, offline licensing system for Windows WPF applications:

- **Issuer tool** (`LicenseGenerator`) — "Warehouse License Studio": registers products, holds the
  private keys, issues signed licenses, and exports the public keys the client trusts.
- **Protected client** (`LicenseClientSample`) — a WPF app that refuses to show its main window
  until a valid license is installed. Ships with public keys only.
- **Shared library** (`LicenseShared`) — models, crypto, codec, machine fingerprint, DPAPI storage,
  clock guard and validation rules used by both sides.
- **Localization library** (`LicenseLocalization`) — the English and Vietnamese string catalogs and
  the runtime language switcher used by both applications.
- **Self-test** (`LicenseSelfTest`) — headless test suite covering the whole pipeline.

There is no server and no network call. Everything is verified locally.

## Requirements

- Windows 10/11
- .NET 8 SDK (the projects target `net8.0-windows`; a newer SDK also builds them)
- Visual Studio 2022 17.x or the `dotnet` CLI

## Build and run

```powershell
dotnet build WpfLicenseGenerator.sln

dotnet run --project LicenseGenerator      # issuer tool
dotnet run --project LicenseClientSample   # protected client
dotnet run --project LicenseSelfTest       # headless test suite
```

## How it works

1. The issuer creates **one key pair per product**.
2. A license is a small JSON envelope containing a binary payload plus a signature.
3. The client verifies, in order: envelope format → known public key → signature → product id →
   machine binding → start/expiry dates → clock-rollback guard. Only then does it create its
   main window.
4. Licenses can be delivered either as an **activation code** (`WLC1-XXXXX-…`) or as a
   `.license.json` file. Both carry the same signed content.

### Algorithms

Each product chooses its own signing algorithm when it is created:

| Algorithm    | Typical license size          | Notes                                        |
| ------------ | ----------------------------- | -------------------------------------------- |
| RSA-3072     | ~1155 characters (activation) | Recommended baseline, universally compatible |
| RSA-2048/4096| smaller / larger              | Also supported                               |
| ECDSA P-256  | ~508 characters               | Best for typed or printed activation codes    |
| ECDSA P-384  | ~730 characters               | Higher security, still compact               |

RSA uses PSS with SHA-256; ECDSA uses SHA-256/SHA-384 according to the curve.

### Machine binding

Two modes are available per license:

- **Machine fingerprint** — `SHA256("WarehouseLicense|v1|MachineGuid|MACHINENAME")`, shown as a
  64-character hex string. Strongest binding; survives app reinstalls, may change after a Windows
  reinstall or machine rename.
- **Device ID** — the short `DEV-XXXXXXXXXXXXXXXX` form derived from the same fingerprint. Easier to
  read over the phone; slightly easier to guess, so use it only when convenience matters.

## Languages (English / Vietnamese)

Both applications are fully bilingual. Use the small **VI** / **EN** button in the header to switch
languages instantly; every open window updates in place.

- The issuer shows the button next to **Issue license**.
- The client shows it in the activation window and in the protected main window.
- The choice is saved to `%LOCALAPPDATA%\WarehouseLicense\ui-language.txt` and restored on the next
  start. English is the default.
- Strings live in `LicenseLocalization\Strings.en.xaml` and `Strings.vi.xaml`. XAML uses
  `{DynamicResource S_Key}`; code-behind and view models use `LicenseLocalization.LanguageManager`
  (`T`, `Format`) and subscribe to `LanguageChanged` for lists and labels built in code.
- The self-test enforces that both catalogs expose exactly the same keys and that neither language
  has an empty value, so a missing translation is caught before release.

To add another language, copy `Strings.en.xaml`, translate the values, keep the `S_*` keys, add the
new code to `AppLanguage` and `LanguageManager.Apply`, and extend the self-test parity check.

## Issuer: first run

1. Start `LicenseGenerator`.
2. Create a product:
   - display name (the product id is derived from it, e.g. `WarehouseLabelCompareMaterial`),
   - signing algorithm and key size,
   - delivery style (serial numbers or activation codes).
3. Set a signing password (minimum length is enforced) when the key pair is created. The password
   unlocks the private key for the current session only; it is never stored.
4. **Back up** `%LOCALAPPDATA%\WarehouseLicense\keys\<productId>\private-key.p8` together with the
   password, offline. If both are lost, existing licenses can never be renewed with the same key.
   If the private key leaks, every license for that product can be forged.

Key files:

```
%LOCALAPPDATA%\WarehouseLicense\
  keys\<productId>\private-key.p8   encrypted PKCS#8 (never ship this)
  keys\<productId>\public-key.pem   distribute with the client
  data\products.json                product catalog + key ids
  data\issued-licenses.json         register of issued licenses
  ui-language.txt                   last UI language (en / vi)
  exports\                          exported license files
```

## Issuing a license

1. Pick the product. If the session is locked, enter the signing password.
2. Enter customer and station names.
3. Paste the fingerprint or device ID copied from the target PC
   (client → **License** window or activation screen → **Copy**).
4. Choose the term: Trial (14 days), 30 days, 90 days, 1 year, perpetual, or a custom date range.
   Expiry is inclusive through the end of the selected date, evaluated in UTC.
5. Issue. The tool produces:
   - a serial (`WLC-YYYY-XXXXXX`) recorded in the register,
   - an activation code (`WLC1-…`) for typed delivery,
   - a `.license.json` envelope for file delivery.
6. Send the code or file to the customer. Nothing secret leaves the issuer.

Renewal works the same way: issue a new license for the same device. The old one stays valid until
its own expiry and the client keeps whichever license is installed last.

## Provisioning the client

1. In the issuer, open **Settings → Export public keys** and save the bundle as
   `LicenseClientSample\Keys\public-keys.json`.
2. Keep `appsettings.json` in sync with the issuer's product:

   ```json
   {
     "productId": "WarehouseLabelCompareMaterial",
     "productName": "WarehouseLabelCompareMaterial",
     "vendor": "Han Manufacturing Systems",
     "version": "1.0.0"
   }
   ```

   `productId` must match exactly; a license for another product is rejected.
3. Rebuild. The bundle is embedded in the assembly **and** copied next to the executable. If the
   file is missing, the client reports that no vendor key is installed and refuses activation.

For your own application, keep `App.xaml.cs`'s startup gate, `LicenseValidator`,
`PublicKeyProvider`, `ClientConfig` and the two windows; replace the demo "label comparison" panel
with your real modules, and re-check the license before entering protected features.

## Client behaviour

- **No main window without a valid license.** `App.OnStartup` validates first and shows
  `ActivationWindow` when the license is missing, expired, invalid, bound to another machine, or
  when the clock looks rolled back.
- **Activation window** accepts a pasted code, an imported file, or a dropped `.license.json`.
  The key is masked by default; **Show key** reveals it for checking.
- **Storage** is DPAPI-protected at
  `%LOCALAPPDATA%\WarehouseLicense\licenses\<productId>\license.dat`, with entropy derived from the
  product id and this machine's fingerprint. Copying the file to another PC or account fails.
- **Re-checks** every 30 minutes and immediately from the **Re-check** button. If a check fails, the
  app returns to activation and closes if the user cancels.
- **Clock guard** records the highest UTC time it has seen, DPAPI-protected, and rejects a clock
  that moves backwards beyond a small tolerance.
- **License information** dialog shows the serial, product, customer, station, term, dates, binding,
  device id, this machine's fingerprint, algorithm/key id, and storage path.

## Self-test

`LicenseSelfTest` runs entirely offline against a temporary data root and prints one line per
assertion:

```powershell
dotnet run --project LicenseSelfTest
```

Covered: key creation and password checks, signing and verification, payload round-trip, wrong
machine, wrong product, expired and future licenses, tampering, activation codes (RSA and ECDSA,
including lowercase/space tolerance and corruption), perpetual licenses, DPAPI storage isolation,
clock rollback and skew tolerance, and public-key bundle serialization. The UI checks load both
themes, both language catalogs, verify catalog parity and placeholder preservation, then switch
English → Vietnamese → English and confirm that both DynamicResource text and code-built labels
(navigation, dashboard cards, counts, client status messages) update at runtime.

It also runs an end-to-end pass through the **real** issuer services and the **real** client
validator: create product and key pair → publish the public bundle → issue → activate → restart →
reject another machine's license → reject an expired license → reject a tampered license → reject a
clock rollback. The end-to-end pass writes `Keys\public-keys.json` into the test output folder and
removes it afterwards; no state outside the temporary test root is touched.

## Security notes and limits

- The private key is encrypted PKCS#8 (AES-256-CBC + PBKDF2) and never leaves the issuer's machine.
  Only public keys are shipped.
- Integrity comes from the signature. Product id, device binding and dates are all inside the signed
  payload, so editing the stored license or the JSON envelope breaks verification.
- DPAPI ties the stored copy to the Windows account and machine; it is not a substitute for the
  signature, which is checked on every load.
- Offline enforcement cannot stop a determined attacker with local admin rights from patching the
  binary or virtualising the clock. The clock guard only catches naive rollbacks. For stronger
  control, add periodic online validation or server-issued, short-lived licenses.
- The fingerprint is a practical binding signal, not a hardware root of trust. For TPM-backed
  identity, replace `MachineFingerprint` with a TPM key attestation.
- Revocation is recorded in the issuer register, but an offline client cannot learn about it. Use
  short license terms when revocation matters.
- Never log signing passwords, full license text, or private key material. The issuer tool keeps the
  password in memory for the session only.

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| "No vendor public key is installed" | `Keys\public-keys.json` is missing or was not exported after the product was created. Export and rebuild. |
| "The vendor public key for this license is not installed" | The license was issued with a different key (different product or recreated key). Re-export the bundle. |
| "This license is for another machine" | Fingerprint/device id in the license does not match this PC. Issue a new license for the correct id. |
| "The system clock appears to have been moved backwards" | Set the clock correctly and retry; the guard stores the highest time seen. |
| Activation succeeds but the app still asks next start | The license file could not be written (permissions/antivirus). Check `licenses\<productId>\license.dat`. |
| Issuer cannot sign | The session is locked or the key pair is missing. Enter the password; a deleted private key cannot be recreated without invalidating old licenses. |
# Authentication
