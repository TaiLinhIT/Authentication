using LicenseGenerator.Services;
using LicenseShared;
using System.IO;

namespace LicenseSelfTest;

/// <summary>
/// End-to-end check that wires the real issuer services to the shared verification library
/// (the same code path a protected client performs): create product and key pair, publish the
/// public bundle, issue a license, verify and store it, then confirm the negative paths a
/// customer can hit.
/// </summary>
internal static class EndToEnd
{
    public static void Run(string root, string password, Action<string, bool, string?> check)
    {
        const string productId = "GPT9000-E2E";
        const string productName = "GPT-9000 Safety Test System";

        var products = new ProductStore(Path.Combine(root, "data", "products.json"));
        var records = new LicenseRecordStore(Path.Combine(root, "data", "issued-licenses.json"));
        var keys = new KeyStore(Path.Combine(root, "keys"));
        var issuer = new IssuerService(keys, records);

        var product = new ProductDefinition
        {
            ProductId = productId,
            ProductName = productName,
            Algorithm = SigningAlgorithm.Rsa,
            KeySize = 3072,
            KeygenMode = KeygenMode.ActivationCodes
        };

        keys.CreateKeyPair(product, password);
        products.Add(product);
        check("E2E issuer keeps the product key pair", keys.HasKeyPair(productId), keys.DirectoryFor(productId));

        string fingerprint;
        try
        {
            fingerprint = MachineFingerprint.GetCurrent();
        }
        catch (InvalidOperationException ex)
        {
            check("E2E machine fingerprint", false, ex.Message);
            return;
        }

        string deviceId = MachineFingerprint.GetShortDeviceId();
        check("E2E machine identity is available", fingerprint.Length == 64, fingerprint);
        // Publish the bundle the way the issuer's Settings screen does, then read it back from
        // disk exactly like a client build would.
        string bundlePath = Path.Combine(root, "client", "Keys", "public-keys.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
        File.WriteAllText(bundlePath, new ExportService(keys).BuildPublicKeyBundle(products.Load()));

        var bundle = PublicKeyBundleSerializer.Deserialize(File.ReadAllText(bundlePath));
        check("E2E public key bundle round trips through disk",
            bundle is not null && bundle.Keys.Count == 1 && bundle.Keys[0].ProductId == productId,
            bundlePath);

        // Verify a license the way a protected client does: known key -> signature -> rules.
        LicenseValidationResult VerifyEnvelope(SignedLicense envelope, DateTimeOffset now)
        {
            var key = bundle?.Keys.FirstOrDefault(k =>
                string.Equals(k.KeyId, envelope.KeyId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.ProductId, productId, StringComparison.OrdinalIgnoreCase));

            if (key is null)
                return LicenseValidationResult.Fail(
                    LicenseStatus.UnknownKey, "The vendor public key for this license is not installed.");

            if (!LicenseCrypto.Verify(envelope, key.PublicKeyPem, out var payload, out string verifyError))
                return LicenseValidationResult.Fail(LicenseStatus.InvalidSignature, verifyError);

            return LicenseRules.Evaluate(payload!, productId, fingerprint, deviceId, now);
        }

        LicenseValidationResult Verify(string licenseText, DateTimeOffset now)
        {
            if (!LicenseCodec.TryParseLicenseText(licenseText, out var envelope, out string parseError))
                return LicenseValidationResult.Fail(LicenseStatus.Malformed, parseError);

            return VerifyEnvelope(envelope!, now);
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        var good = new IssueRequest
        {
            CustomerName = "E2E Customer",
            StationName = "WS-E2E-001",
            BindingMode = DeviceBindingMode.MachineFingerprint,
            DeviceId = fingerprint,
            LicenseType = LicenseType.Days30,
            StartDate = today.AddDays(-7),
            ExpiryDate = today.AddDays(29)
        };

        var record = issuer.Issue(product, good, password);
        check("E2E issued a serial number", record.LicenseId.StartsWith("WLC-", StringComparison.Ordinal), record.LicenseId);

        if (!LicenseCodec.TryParseLicenseText(record.LicenseText, out var envelope, out string error))
        {
            check("E2E license text parses", false, error);
            return;
        }

        var activated = Verify(record.LicenseText, DateTimeOffset.UtcNow);
        check("E2E issued license is valid on the bound machine", activated.IsValid, activated.Message);

        string code = LicenseCodec.CreateActivationCode(envelope!);
        check("E2E activation code round trips",
            LicenseCodec.TryDecodeActivationCode(code, out var codeEnvelope, out error)
            && VerifyEnvelope(codeEnvelope!, DateTimeOffset.UtcNow).IsValid, error);

        // Store the license like a client does, then re-check on the next start.
        var store = new LicenseStore(productId, Path.Combine(root, "licenses"));
        store.Save(record.LicenseText, fingerprint);
        check("E2E stored license is DPAPI protected",
            !File.ReadAllText(store.FilePath).Contains(productId, StringComparison.Ordinal), store.FilePath);

        string? stored = store.Load(fingerprint);
        var restart = stored is null
            ? LicenseValidationResult.Fail(LicenseStatus.Missing, "No license is installed for this machine.")
            : Verify(stored, DateTimeOffset.UtcNow);
        check("E2E stored license validates on the next start", restart.IsValid, restart.Message);
        check("E2E stored license rejects another machine's DPAPI context",
            store.Load(new string('B', 64)) is null, null);

        // Wrong machine: issue for a different fingerprint, the client must refuse it.
        var otherMachine = new IssueRequest
        {
            CustomerName = good.CustomerName,
            StationName = good.StationName,
            BindingMode = DeviceBindingMode.MachineFingerprint,
            DeviceId = new string('C', 64),
            LicenseType = LicenseType.Days30,
            StartDate = today,
            ExpiryDate = today.AddDays(29)
        };
        var foreign = issuer.Issue(product, otherMachine, password);
        var foreignResult = Verify(foreign.LicenseText, DateTimeOffset.UtcNow);
        check("E2E license for another machine is rejected",
            foreignResult.Status == LicenseStatus.WrongMachine, foreignResult.Status.ToString());

        // Expired license issued in the past.
        var past = new IssueRequest
        {
            CustomerName = good.CustomerName,
            StationName = good.StationName,
            BindingMode = DeviceBindingMode.MachineFingerprint,
            DeviceId = fingerprint,
            LicenseType = LicenseType.Days30,
            StartDate = today.AddDays(-60),
            ExpiryDate = today.AddDays(-31)
        };
        var expired = issuer.Issue(product, past, password);
        var expiredResult = Verify(expired.LicenseText, DateTimeOffset.UtcNow);
        check("E2E expired license is rejected",
            expiredResult.Status == LicenseStatus.Expired, expiredResult.Status.ToString());

        // Minute-precision expiry issued through the real issuer service.
        var precise = new IssueRequest
        {
            CustomerName = good.CustomerName,
            StationName = good.StationName,
            BindingMode = DeviceBindingMode.MachineFingerprint,
            DeviceId = fingerprint,
            LicenseType = LicenseType.Custom,
            StartDate = today,
            ExpiryDate = today.AddDays(3),
            ExpiryTime = new TimeOnly(18, 45)
        };
        var preciseRecord = issuer.Issue(product, precise, password);
        check("E2E expiry honours the requested minute",
            preciseRecord.ExpiresAtUtc.UtcDateTime.TimeOfDay == new TimeSpan(18, 45, 0),
            preciseRecord.ExpiresAtUtc.ToString("O"));
        check("E2E minute-precision license is valid before that minute",
            Verify(preciseRecord.LicenseText,
                preciseRecord.ExpiresAtUtc.AddMinutes(-1)).IsValid, null);

        var zone = TimeZoneInfo.CreateCustomTimeZone("Test UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
        var macRequest = new IssueRequest
        {
            BindingMode = DeviceBindingMode.MacAddress,
            DeviceId = "c4-c6-e6-9e-17-5b",
            LicenseType = LicenseType.Custom,
            StartDate = new DateOnly(2026, 10, 23),
            ExpiryDate = new DateOnly(2026, 10, 23),
            ExpiryTime = new TimeOnly(16, 0, 0),
            InputTimeZone = zone
        };
        var macRecord = issuer.Issue(product, macRequest, password);
        check("E2E 16:00 local becomes 09:00 UTC", macRecord.ExpiresAtUtc ==
            new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero), macRecord.ExpiresAtUtc.ToString("O"));
        check("E2E local start is previous UTC day", macRecord.NotBeforeUtc ==
            new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero), null);
        check("E2E local expiry round trip stays 16:00", TimeZoneInfo.ConvertTime(macRecord.ExpiresAtUtc, zone).Hour == 16, null);
        check("E2E normalized MAC saved", macRecord.DeviceId == "C4C6E69E175B", macRecord.DeviceId);
        LicenseCodec.TryParseLicenseText(macRecord.LicenseText, out var macEnvelope, out _);
        check("E2E MAC signature verifies", LicenseCrypto.Verify(macEnvelope!, bundle!.Keys[0].PublicKeyPem,
            out var macPayload, out var macError), macError);
        var matchingMacs = new[] { "02:11:22:33:44:55", "C4:C6:E6:9E:17:5B" };
        check("E2E MAC matches any local adapter", LicenseRules.Evaluate(macPayload!, productId, fingerprint, deviceId,
            macRecord.ExpiresAtUtc, matchingMacs).IsValid, null);
        check("E2E MAC rejects wrong adapter", LicenseRules.Evaluate(macPayload!, productId, fingerprint, deviceId,
            macRecord.ExpiresAtUtc, new[] { "02:11:22:33:44:55" }).Status == LicenseStatus.WrongMachine, null);
        check("E2E missing adapters fail closed", LicenseRules.Evaluate(macPayload!, productId, fingerprint, deviceId,
            macRecord.ExpiresAtUtc, Array.Empty<string>()).Status == LicenseStatus.DeviceIdentityUnavailable, null);
        check("E2E expired one tick after local expiry", LicenseRules.Evaluate(macPayload!, productId, fingerprint, deviceId,
            macRecord.ExpiresAtUtc.AddTicks(1), matchingMacs).Status == LicenseStatus.Expired, null);
        byte[] macSecret = OfficeLicenseFile.CreateSecret();
        byte[] macOffice = new ExportService(keys).BuildOfficeFile(macRecord, macSecret);
        check("E2E MAC office.dat opens with normalized address", OfficeLicenseFile.TryOpen(macOffice, productId,
            "C4C6E69E175B", macSecret, out _, out _), null);
        check("E2E MAC office.dat rejects other address", !OfficeLicenseFile.TryOpen(macOffice, productId,
            "021122334455", macSecret, out _, out _), null);
        string macCode = LicenseCodec.CreateActivationCode(macEnvelope!);
        check("E2E MAC activation code round trips", LicenseCodec.TryDecodeActivationCode(macCode, out var decodedMac, out _)
            && LicenseCrypto.Verify(decodedMac!, bundle.Keys[0].PublicKeyPem, out var decodedPayload, out _)
            && decodedPayload!.BindingMode == DeviceBindingMode.MacAddress, null);
        macRequest.ExpiryTime = null;
        var dayRecord = issuer.Issue(product, macRequest, password);
        check("E2E date-only ends local day", TimeZoneInfo.ConvertTime(dayRecord.ExpiresAtUtc, zone).TimeOfDay == TimeOnly.MaxValue.ToTimeSpan(), null);
        macRequest.DeviceId = "192.168.1.1";
        int countBeforeInvalid = records.Load().Count;
        bool invalidMacRejected = false;
        try { issuer.Issue(product, macRequest, password); }
        catch (InvalidOperationException) { invalidMacRejected = true; }
        check("E2E issuer rejects IP as MAC without saving", invalidMacRejected && records.Load().Count == countBeforeInvalid, null);

        // Tampered payload keeps the original signature and must fail verification.
        var tamperedPayload = LicenseCodec.DeserializePayload(Convert.FromBase64String(envelope!.PayloadBase64));
        tamperedPayload.ExpiresAtUtc = tamperedPayload.ExpiresAtUtc.AddYears(5);
        var tamperedEnvelope = new SignedLicense
        {
            Format = envelope.Format,
            Algorithm = envelope.Algorithm,
            KeyId = envelope.KeyId,
            PayloadBase64 = Convert.ToBase64String(LicenseCodec.SerializePayload(tamperedPayload)),
            SignatureBase64 = envelope.SignatureBase64
        };
        var tamperedResult = Verify(LicenseCrypto.SerializeEnvelope(tamperedEnvelope), DateTimeOffset.UtcNow);
        check("E2E tampered license is rejected",
            tamperedResult.Status == LicenseStatus.InvalidSignature, tamperedResult.Status.ToString());

        // The stored license must survive the failed attempts above.
        check("E2E good license still validates after rejected attempts",
            store.Load(fingerprint) is string still && Verify(still, DateTimeOffset.UtcNow).IsValid, null);

        // Clock rollback: the guard should notice a jump backwards.
        var guard = new ClockGuard(productId, Path.Combine(root, "state"));
        var firstCheck = guard.Check(DateTimeOffset.UtcNow, fingerprint);
        check("E2E clock guard accepts the current time", !firstCheck.RollbackDetected, null);
        var rollback = guard.Check(DateTimeOffset.UtcNow.AddDays(-1), fingerprint);
        check("E2E clock rollback is rejected",
            rollback.RollbackDetected, rollback.LastSeenUtc.ToString("O"));
    }
}
