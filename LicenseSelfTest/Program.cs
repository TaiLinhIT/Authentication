using LicenseShared;
using System.IO;
using System.Text;

namespace LicenseSelfTest;

/// <summary>
/// Headless tests for the shared license library: signatures, tampering, binding,
/// expiry, activation-code round trips, DPAPI storage and clock rollback detection.
/// Run with: dotnet run --project LicenseSelfTest
/// </summary>
internal static class Program
{
    private static int _failed;
    private static int _passed;
    private static string _root = "";

    private static int Main()
    {
        _root = Path.Combine(Path.GetTempPath(), "wlc-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        LicensePaths.Root = _root;
        LicensePaths.EnsureBaseDirectories();

        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        const string product = "WarehouseLabelCompareMaterial";
        const string fingerprint = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899";
        string deviceId = "DEV-" + fingerprint[..16];

        try
        {
            var rsaProduct = new ProductDefinition
            {
                ProductId = product,
                ProductName = "WarehouseLabelCompareMaterial",
                Algorithm = SigningAlgorithm.Rsa,
                KeySize = 3072,
                KeygenMode = KeygenMode.SerialNumbers
            };

            string rsaPrivate = Path.Combine(_root, "rsa-private.p8");
            string rsaPublic = Path.Combine(_root, "rsa-public.pem");
            string password = "correct-horse-battery";

            string rsaKeyId = LicenseCrypto.CreateKeyPair(
                SigningAlgorithm.Rsa, 3072, rsaPrivate, rsaPublic, password);
            Check("RSA key pair created", rsaKeyId.Length == LicenseCrypto.KeyIdHexLength, rsaKeyId);
            Check("Public key id matches", LicenseCrypto.ComputeKeyIdFromPublicPem(File.ReadAllText(rsaPublic)) == rsaKeyId);
            Check("Password unlocks key", LicenseCrypto.VerifyPrivateKeyPassword(rsaPrivate, password));
            Check("Wrong password rejected", !LicenseCrypto.VerifyPrivateKeyPassword(rsaPrivate, "wrong-password-12"));

            var payload = new LicensePayload
            {
                LicenseId = "WLC-2026-ABC123",
                ProductId = product,
                ProductName = rsaProduct.ProductName,
                CustomerName = "Han Manufacturing",
                StationName = "WS-TEST-001",
                DeviceId = fingerprint,
                BindingMode = DeviceBindingMode.MachineFingerprint,
                LicenseType = LicenseType.Days90,
                IssuedAtUtc = now,
                NotBeforeUtc = now,
                ExpiresAtUtc = LicenseDurations.EndOfDayUtc(new DateOnly(2027, 1, 19))
            };

            var envelope = LicenseCrypto.Sign(payload, rsaPrivate, password, SigningAlgorithm.Rsa);
            Check("Envelope uses key id", envelope.KeyId == rsaKeyId);
            Check("Envelope algorithm", envelope.Algorithm == "RSA-3072-PSS-SHA256", envelope.Algorithm);

            string publicPem = File.ReadAllText(rsaPublic);

            Check("Signature verifies",
                LicenseCrypto.Verify(envelope, publicPem, out var verified, out string error), error);
            Check("Payload round trips", verified?.LicenseId == payload.LicenseId && verified?.DeviceId == fingerprint);

            var rules = LicenseRules.Evaluate(verified!, product, fingerprint, deviceId, now);
            Check("License is valid on the bound machine", rules.IsValid, rules.Message);

            var wrongMachine = LicenseRules.Evaluate(verified!, product, new string('A', 64), deviceId, now);
            Check("Different fingerprint rejected", wrongMachine.Status == LicenseStatus.WrongMachine, wrongMachine.Message);

            var wrongProduct = LicenseRules.Evaluate(verified!, "OtherProduct", fingerprint, deviceId, now);
            Check("Different product rejected", wrongProduct.Status == LicenseStatus.WrongProduct, wrongProduct.Message);

            var expired = LicenseRules.Evaluate(verified!, product, fingerprint, deviceId, new DateTimeOffset(2027, 1, 20, 0, 0, 0, TimeSpan.Zero));
            Check("Expired license rejected", expired.Status == LicenseStatus.Expired, expired.Message);

            var notYet = LicenseRules.Evaluate(verified!, product, fingerprint, deviceId, now.AddDays(-30));
            Check("Future license rejected", notYet.Status == LicenseStatus.NotYetValid, notYet.Message);

            // Manual device id binding
            var manual = Clone(payload);
            manual.BindingMode = DeviceBindingMode.ManualDeviceId;
            manual.DeviceId = deviceId;
            var manualEnvelope = LicenseCrypto.Sign(manual, rsaPrivate, password, SigningAlgorithm.Rsa);
            Check("Manual device id verifies", LicenseCrypto.Verify(manualEnvelope, publicPem, out var manualPayload, out error), error);
            Check("Manual binding accepted",
                LicenseRules.Evaluate(manualPayload!, product, fingerprint, deviceId, now).IsValid);

            // Tampering
            var tampered = Clone(payload);
            tampered.ExpiresAtUtc = payload.ExpiresAtUtc.AddYears(10);
            string tamperedJson = LicenseCrypto.SerializeEnvelope(new SignedLicense
            {
                Format = envelope.Format,
                Algorithm = envelope.Algorithm,
                KeyId = envelope.KeyId,
                PayloadBase64 = Convert.ToBase64String(LicenseCodec.SerializePayload(tampered)),
                SignatureBase64 = envelope.SignatureBase64
            });
            Check("Tampered expiry rejected",
                LicenseCrypto.TryDeserializeEnvelope(tamperedJson, out var tamperedEnvelope, out error)
                && !LicenseCrypto.Verify(tamperedEnvelope!, publicPem, out _, out _), "signature must fail");

            // Activation codes
            string code = LicenseCodec.CreateActivationCode(envelope);
            Console.WriteLine($"      RSA-3072 activation code: {code.Length} characters");
            Check("RSA activation code has prefix", code.StartsWith("WLC1-", StringComparison.Ordinal));

            Check("Activation code parses",
                LicenseCodec.TryDecodeActivationCode(code, out var fromCode, out error), error);
            Check("Activation code verifies",
                LicenseCrypto.Verify(fromCode!, publicPem, out var codePayload, out error), error);
            Check("Code payload matches", codePayload?.LicenseId == payload.LicenseId);

            string lowerCase = code.ToLowerInvariant().Replace("-", " ");
            Check("Code accepts lowercase and spaces",
                LicenseCodec.TryDecodeActivationCode(lowerCase, out var lowered, out error)
                && LicenseCrypto.Verify(lowered!, publicPem, out _, out _), error);

            // Flip a character in the middle
            char[] chars = code.ToCharArray();
            int index = code.Length / 2;
            chars[index] = chars[index] == 'A' ? 'B' : 'A';
            bool corruptedRejected = !LicenseCodec.TryDecodeActivationCode(new string(chars), out var corrupted, out _)
                                     || !LicenseCrypto.Verify(corrupted!, publicPem, out _, out _);
            Check("Corrupted activation code rejected", corruptedRejected);

            string json = LicenseCrypto.SerializeEnvelope(envelope);
            Check("License JSON parses",
                LicenseCodec.TryParseLicenseText(json, out var parsedEnvelope, out error)
                && LicenseCrypto.Verify(parsedEnvelope!, publicPem, out _, out _), error);

            // ECDSA P-256, used for compact activation codes
            string ecPrivate = Path.Combine(_root, "ec-private.p8");
            string ecPublic = Path.Combine(_root, "ec-public.pem");
            string ecKeyId = LicenseCrypto.CreateKeyPair(SigningAlgorithm.Ecdsa, 256, ecPrivate, ecPublic, password);
            var ecPayload = Clone(payload);
            var ecEnvelope = LicenseCrypto.Sign(ecPayload, ecPrivate, password, SigningAlgorithm.Ecdsa);
            string ecPem = File.ReadAllText(ecPublic);
            Check("ECDSA envelope key id", ecEnvelope.KeyId == ecKeyId);
            Check("ECDSA signature verifies",
                LicenseCrypto.Verify(ecEnvelope, ecPem, out var ecVerified, out error), error);
            Check("ECDSA payload preserved", ecVerified?.LicenseId == payload.LicenseId);

            string ecCode = LicenseCodec.CreateActivationCode(ecEnvelope);
            Console.WriteLine($"      ECDSA-P256 activation code: {ecCode.Length} characters");
            Check("ECDSA code round trips",
                LicenseCodec.TryDecodeActivationCode(ecCode, out var ecFromCode, out error)
                && LicenseCrypto.Verify(ecFromCode!, ecPem, out _, out error), error);
            Check("ECDSA code is compact", ecCode.Length < 600, ecCode.Length.ToString());

            // Perpetual license
            var perpetual = Clone(payload);
            perpetual.LicenseType = LicenseType.Perpetual;
            perpetual.ExpiresAtUtc = DateTimeOffset.MaxValue;
            var perpetualEnvelope = LicenseCrypto.Sign(perpetual, rsaPrivate, password, SigningAlgorithm.Rsa);
            var perpetualPayload = LicenseCrypto.Verify(perpetualEnvelope, publicPem, out var perpetualVerified, out error)
                ? perpetualVerified : null;
            Check("Perpetual license valid in 2099",
                perpetualPayload is not null &&
                LicenseRules.Evaluate(perpetualPayload, product, fingerprint, deviceId,
                    new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)).IsValid, error);

            // DPAPI storage
            var store = new LicenseStore(product, Path.Combine(_root, "licenses"));
            store.Save(json, fingerprint);
            Check("Stored license loads", store.Load(fingerprint) == json);
            Check("Stored license is not plaintext",
                !Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Contains("WarehouseLabelCompareMaterial", StringComparison.Ordinal));
            Check("Stored license rejects another machine", store.Load(new string('B', 64)) is null);

            // Clock guard
            var guard = new ClockGuard(product, Path.Combine(_root, "state"));
            Check("Clock guard first run", !guard.Check(now, fingerprint).RollbackDetected);
            Check("Clock guard normal progression", !guard.Check(now.AddHours(2), fingerprint).RollbackDetected);
            var rollback = guard.Check(now.AddMinutes(-70), fingerprint);
            Check("Clock rollback detected", rollback.RollbackDetected, rollback.LastSeenUtc.ToString("O"));
            Check("Small clock skew tolerated", !guard.Check(now.AddHours(2).AddMinutes(-2), fingerprint).RollbackDetected);

            // Key bundle
            var bundle = new PublicKeyBundle
            {
                Keys =
                {
                    new PublicKeyEntry
                    {
                        ProductId = product,
                        ProductName = rsaProduct.ProductName,
                        KeyId = rsaKeyId,
                        Algorithm = rsaProduct.AlgorithmLabel,
                        PublicKeyPem = publicPem
                    }
                }
            };
            string bundleJson = PublicKeyBundleSerializer.Serialize(bundle);
            var parsedBundle = PublicKeyBundleSerializer.Deserialize(bundleJson);
            Check("Public key bundle round trips", parsedBundle?.Keys.Count == 1 && parsedBundle.Keys[0].KeyId == rsaKeyId);

            // End-to-end: real issuer services -> real client validator
            EndToEnd.Run(_root, password, Check);

            // WPF theme/dialog resources (compiled BAML)
            UiSmokeTests.Run(Check);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL  Unhandled exception: " + ex);
        }
        finally
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static LicensePayload Clone(LicensePayload source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        LicenseId = source.LicenseId,
        ProductId = source.ProductId,
        ProductName = source.ProductName,
        CustomerName = source.CustomerName,
        StationName = source.StationName,
        DeviceId = source.DeviceId,
        BindingMode = source.BindingMode,
        LicenseType = source.LicenseType,
        IssuedAtUtc = source.IssuedAtUtc,
        NotBeforeUtc = source.NotBeforeUtc,
        ExpiresAtUtc = source.ExpiresAtUtc,
        KeyId = source.KeyId
    };

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("PASS  " + name);
            return;
        }

        _failed++;
        Console.WriteLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : $"  ({detail})"));
    }
}
