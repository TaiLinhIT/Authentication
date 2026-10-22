using LicenseClientSample.Models;
using LicenseClientSample.Security;
using LicenseClientSample.Services;
using LicenseGenerator.Services;
using LicenseShared;
using System.IO;

namespace LicenseSelfTest;

/// <summary>
/// End-to-end check that wires the real issuer services to the real client validator:
/// create product and key pair, publish the public bundle, issue a license, activate it,
/// then confirm the negative paths a customer can hit.
/// </summary>
internal static class EndToEnd
{
    public static void Run(string root, string password, Action<string, bool, string?> check)
    {
        const string productId = "WarehouseLabelCompareMaterial-E2E";
        const string productName = "WarehouseLabelCompareMaterial";

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

        // Publish the bundle the way the issuer's Settings screen does. The client loader reads
        // public-keys.json next to the executable, which is this test's output folder.
        string keysDir = Path.Combine(AppContext.BaseDirectory, "Keys");
        string bundlePath = Path.Combine(keysDir, "public-keys.json");
        Directory.CreateDirectory(keysDir);
        File.WriteAllText(bundlePath, new ExportService(keys).BuildPublicKeyBundle(products.Load()));

        try
        {
            var config = new ClientConfig { ProductId = productId, ProductName = productName };
            var validator = new LicenseValidator(config, PublicKeyProvider.LoadDefault());

            check("E2E client loads the published public key", !validator.PublicKeys.IsEmpty,
                string.Join(", ", validator.PublicKeys.Sources));
            check("E2E client reads this machine identity", validator.HasMachineIdentity, validator.MachineDeviceId);

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            var good = new IssueRequest
            {
                CustomerName = "E2E Customer",
                StationName = "WS-E2E-001",
                BindingMode = DeviceBindingMode.MachineFingerprint,
                DeviceId = validator.MachineFingerprint,
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

            string code = LicenseCodec.CreateActivationCode(envelope!);
            var activated = validator.Activate(code, DateTimeOffset.UtcNow);
            check("E2E activation code activates on the bound machine", activated.IsValid, activated.Message);

            var restart = validator.ValidateInstalled(DateTimeOffset.UtcNow);
            check("E2E stored license validates on the next start", restart.IsValid, restart.Message);
            check("E2E stored license is DPAPI protected",
                !File.ReadAllText(new LicenseStore(productId).FilePath).Contains(productId, StringComparison.Ordinal),
                new LicenseStore(productId).FilePath);

            // Wrong machine: issue for a different fingerprint, the client must refuse to store it.
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
            LicenseCodec.TryParseLicenseText(foreign.LicenseText, out var foreignEnvelope, out _);
            var foreignCode = LicenseCodec.CreateActivationCode(foreignEnvelope!);
            var foreignResult = validator.Activate(foreignCode, DateTimeOffset.UtcNow);
            check("E2E license for another machine is rejected",
                foreignResult.Status == LicenseStatus.WrongMachine, foreignResult.Status.ToString());

            // Expired license issued in the past.
            var past = new IssueRequest
            {
                CustomerName = good.CustomerName,
                StationName = good.StationName,
                BindingMode = DeviceBindingMode.MachineFingerprint,
                DeviceId = validator.MachineFingerprint,
                LicenseType = LicenseType.Days30,
                StartDate = today.AddDays(-60),
                ExpiryDate = today.AddDays(-31)
            };
            var expired = issuer.Issue(product, past, password);
            LicenseCodec.TryParseLicenseText(expired.LicenseText, out var expiredEnvelope, out _);
            var expiredResult = validator.Activate(
                LicenseCodec.CreateActivationCode(expiredEnvelope!), DateTimeOffset.UtcNow);
            check("E2E expired license is rejected",
                expiredResult.Status == LicenseStatus.Expired, expiredResult.Status.ToString());

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
            var tamperedResult = validator.Activate(
                LicenseCrypto.SerializeEnvelope(tamperedEnvelope), DateTimeOffset.UtcNow);
            check("E2E tampered license is rejected",
                tamperedResult.Status == LicenseStatus.InvalidSignature, tamperedResult.Status.ToString());

            // The stored license must survive the failed attempts above.
            check("E2E good license still validates after rejected attempts",
                validator.ValidateInstalled(DateTimeOffset.UtcNow).IsValid, null);

            // Clock rollback: the guard should notice a jump backwards.
            var rollback = validator.ValidateInstalled(DateTimeOffset.UtcNow.AddDays(-1));
            check("E2E clock rollback is rejected",
                rollback.Status == LicenseStatus.ClockRollback, rollback.Status.ToString());
        }
        finally
        {
            try { File.Delete(bundlePath); } catch (IOException) { }
        }
    }
}
