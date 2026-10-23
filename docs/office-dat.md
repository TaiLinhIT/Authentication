# Tài liệu kỹ thuật — File license mã hóa `office.dat`

Tài liệu này mô tả **đầy đủ** cơ chế mã hóa của file license `office.dat` do Warehouse License
Studio xuất ra, để bạn tự cài cơ chế đọc/giải mã vào phần mềm của mình (ví dụ app WinForms
GPT-9000). Tài liệu độc lập — bạn có thể copy-paste code bên dưới vào project mà **không cần**
tham chiếu assembly `LicenseShared.dll`.

---

## 1. Tổng quan

Khi cấp license trong tool, bấm **Lưu office.dat** (nút bên cạnh "Lưu file license" ở màn hình
kết quả, hoặc menu ⋯ trên danh sách license). File `office.dat` được tạo ra chứa **license đã
được ký số**, nhưng bị **niêm phong bằng mã hóa đối xứng AES-256-GCM**:

```
┌────────────────────────────────────────────────────────────┐
│  office.dat                                                │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ header (magic, version, salt, nonce, độ dài)         │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ AES-256-GCM ciphertext  ──►  license JSON đã ký      │  │
│  │ + tag xác thực 16 byte                              │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────┘
        ▲
        │ khóa = HKDF-SHA256(secret, salt, "OFFC1|ProductId|BindingValue")
        │
   secret (32 byte) nhúng trong app bạn  +  fingerprint của MÁY ĐANG CHẠY
```

### Lớp phòng vệ

| Lớp | Cơ chế | Chặn được gì |
|---|---|---|
| 1. Niêm phong | AES-256-GCM (authenticated encryption) | Khách hàng không đọc/nội suy được nội dung license; sửa 1 bit bất kỳ là GCM tag fail |
| 2. Buộc máy | Key derive gồm **fingerprint máy** và **ProductId** | Copy `office.dat` sang máy khác → không mở được |
| 3. Buộc app | Key derive gồm **vendor secret** (32 byte nhúng trong app) | App không nhúng secret → không mở được |
| 4. Chữ ký (mỏ neo tin cậy) | RSA-PSS / ECDSA-SHA256 trên payload | Kẻ gian **không thể tự cấp license**, kể cả khi đã có secret + đã bẻ được app |

> **Điểm mấu chốt về mô hình đe dọa:** lớp 1–3 là lớp "niêm phong + buộc máy", nâng cao đáng
> kể so với file JSON thô, nhưng **chữ ký số (lớp 4) mới là thứ thực sự bảo vệ**. Một kẻ tấn
> công có khả năng reverse-engineer app bạn vẫn có thể trích secret và fingerprint để *đọc*
> file — nhưng **không bao giờ cấp được license mới** vì không có private key. Do đó: **không
> đặt niềm tin vào secret như vào một DRM không thể bẻ**, mà đặt niềm tin vào chữ ký.

---

## 2. Bố cục file (layout từng byte)

File là binary thuần, **không** có BOM, **không** text. Đọc theo **little-endian** (thứ tự byte
của Windows/.NET).

| Offset | Số byte | Trường | Giá trị / ý nghĩa |
|---|---|---|---|
| 0 | 5 | `magic` | ASCII `OFFC1` (0x4F 0x46 0x46 0x43 0x31) — nhận diện file |
| 5 | 1 | `version` | `0x01` — phiên bản format |
| 6 | 16 | `salt` | Salt ngẫu nhiên cho HKDF (mỗi file một giá trị) |
| 22 | 12 | `nonce` | Nonce ngẫu nhiên cho AES-GCM (mỗi file một giá trị) |
| 34 | 4 | `length` | `int32` little-endian = độ dài ciphertext `N` (không gồm tag) |
| 38 | `N` | `ciphertext` | Ciphertext AES-256-GCM |
| 38+N | 16 | `tag` | GCM authentication tag |

**Tổng kích thước file** = `38 + N + 16 = 54 + N` byte.

Nội dung plaintext (trước khi mã hóa) chính là **chuỗi JSON của license envelope đã ký**
(cùng nội dung với file `.license.json` mà tool vẫn xuất). Xem mục 5.

### Kiểm tra hợp lệ khi đọc

1. `file.Length >= 54` (ngắn hơn = hỏng).
2. 5 byte đầu == `OFFC1`.
3. Byte thứ 5 == `0x01`.
4. `N = length` phải `> 0` và `file.Length == 54 + N`.
5. Bước 5 sai bất kỳ điều kiện nào → **từ chối**, không cố giải mã.

---

## 3. Công thứcderive khóa (KDF)

```
key (32 byte) = HKDF-SHA256(
      ikm   = secret,          // 32 byte vendor secret nhúng trong app
      salt  = salt,            // 16 byte đọc từ offset 6 của file
      info  = "OFFC1|" + ProductId.ToUpper() + "|" + BindingValue.ToUpper(),
      L     = 32
)
```

Trong đó:

- **`secret`** — 32 byte, dạng base64, do vendor (bạn) tạo. Lấy từ tool: **Settings →
  Client distribution → Export client secret** (lưu ra `office-secret.txt`). Nhúng giá trị
  base64 này vào app.
- **`ProductId`** — chuỗi định danh product (phải khớp **chính xác**, so sánh sau khi
  `.Trim().ToUpperInvariant()`).
- **`BindingValue`** — giá trị buộc máy, tùy loại license:
  - License buộc theo **fingerprint**: chính là fingerprint 64 ký tự hex.
  - License buộc theo **Device ID thủ công**: chính là chuỗi `DEV-XXXXXXXXXXXXXXXX`.
  - License buộc theo **MAC** (`bindingMode = 2`): MAC chuẩn hóa thành 12 ký tự hex viết hoa,
    không có dấu phân cách, ví dụ `C4C6E69E175B`. Client phải dùng `LicenseShared.dll` mới.

> **Quy tắc đồng bộ với tool:** tool dùng `Trim().ToUpperInvariant()` trên cả ProductId và
> BindingValue trước khi đưa vào `info`. Bạn PHẢI làm đúng như vậy khi đọc, nếu không sẽ derive
> sai khóa → GCM tag fail.

### .NET API

.NET 8 có sẵn `System.Security.Cryptography.HKDF` và `AesGcm` — không cần package ngoài:

```csharp
byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, info);
using var aes = new AesGcm(key, tagSizeInBytes: 16);
```

> Lưu ý: constructor `AesGcm(byte[], int)` là API .NET 8+ (chỉ định rõ độ dài tag). Nếu bạn
> target .NET 7 thì dùng `new AesGcm(key)` (tag mặc định 16 byte).

---

## 4. Giải mã — code C# hoàn chỉnh (copy-paste)

### 4.1 Bản độc lập (không cần `LicenseShared.dll`)

Dán nguyên file này vào project WinForms của bạn:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyCompany.Licensing;

/// <summary>
/// Đọc file license office.dat do Warehouse License Studio xuất ra.
/// Bố cục file:
///   0  5   magic   "OFFC1"
///   5  1   version 1
///   6  16  salt    (HKDF salt)
///   22 12  nonce   (AES-GCM nonce)
///   34 4   length  (int32 LE, độ dài ciphertext)
///   38 N   ciphertext
///   38+N   16      GCM tag
/// </summary>
public static class OfficeDatReader
{
    private static ReadOnlySpan<byte> Magic => "OFFC1"u8;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 5 + 1 + SaltSize + NonceSize + 4; // = 38
    private const int MaxPlaintextBytes = 64 * 1024;

    /// <summary>
    /// Mở file office.dat trên MÁY HIỆN TẠI. Trả về JSON license (đã ký) nếu thành công.
    /// productId: định danh product của app bạn.
    /// secretBase64: vendor secret (32 byte, base64) nhúng trong app.
    /// </summary>
    public static bool TryOpenOnThisMachine(
        string filePath,
        string productId,
        string secretBase64,
        out string licenseJson,
        out string error)
    {
        licenseJson = "";
        error = "";

        byte[]? file;
        try
        {
            file = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Không đọc được file: " + ex.Message;
            return false;
        }

        byte[]? secret;
        try
        {
            secret = Convert.FromBase64String(secretBase64.Trim());
        }
        catch (FormatException)
        {
            error = "Secret base64 không hợp lệ.";
            return false;
        }

        // Thử theo fingerprint (license buộc máy bình thường) rồi theo DEV- id.
        string fingerprint = GetMachineFingerprint();
        string shortId = GetShortDeviceId();

        if (TryOpen(file, productId, fingerprint, secret, out licenseJson, out error))
            return true;

        string fpError = error; // giữ message của nhánh fingerprint làm lỗi chính

        if (TryOpen(file, productId, shortId, secret, out licenseJson, out error))
            return true;

        foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
            byte[] mac = adapter.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6 || mac.All(b => b == 0) || (mac[0] & 1) != 0) continue;
            if (TryOpen(file, productId, Convert.ToHexString(mac), secret, out licenseJson, out error))
                return true;
        }

        error = fpError;
        return false;
    }

    /// <summary>Mở file với một binding value cụ thể (bản lõi).</summary>
    public static bool TryOpen(
        byte[]? file,
        string productId,
        string bindingValue,
        byte[]? secret,
        out string licenseJson,
        out string error)
    {
        licenseJson = "";
        error = "";

        if (file is null || file.Length < HeaderSize + TagSize)
        {
            error = "File license rỗng hoặc bị cắt cụt.";
            return false;
        }

        if (!file.AsSpan(0, 5).SequenceEqual(Magic))
        {
            error = "Đây không phải file office.dat.";
            return false;
        }

        if (file[5] != 1)
        {
            error = $"Phiên bản file không được hỗ trợ ({file[5]}).";
            return false;
        }

        if (secret is null || secret.Length != 32)
        {
            error = "Vendor secret bị thiếu hoặc sai độ dài.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(bindingValue))
        {
            error = "ProductId hoặc BindingValue rỗng.";
            return false;
        }

        int offset = 6;
        byte[] salt = file.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        byte[] nonce = file.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        int length = BitConverter.ToInt32(file, offset);
        offset += 4;

        if (length <= 0 || length > MaxPlaintextBytes || file.Length != HeaderSize + length + TagSize)
        {
            error = "File license bị hỏng.";
            return false;
        }

        byte[] ciphertext = file.AsSpan(offset, length).ToArray();
        offset += length;
        byte[] tag = file.AsSpan(offset, TagSize).ToArray();

        // Derive khóa: HKDF-SHA256(secret, salt, "OFFC1|PRODUCT|BINDING")
        string info = string.Format(
            CultureInfo.InvariantCulture,
            "OFFC1|{0}|{1}",
            productId.Trim().ToUpperInvariant(),
            bindingValue.Trim().ToUpperInvariant());
        byte[] infoBytes = Encoding.UTF8.GetBytes(info);

        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, infoBytes);

        byte[] plaintext = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);   // sai khóa/bị sửa → ném CryptographicException
        }
        catch (CryptographicException)
        {
            error = "Không mở được file trên máy này, hoặc file đã bị chỉnh sửa.";
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        licenseJson = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return true;
    }

    // ---------------------------------------------------------------- máy hiện tại

    /// <summary>
    /// Fingerprint = SHA256("WarehouseLicense|v1|" + MachineGuid + "|" + MachineName.ToUpper())
    /// thành hex chữ hoa (64 ký tự). MachineGuid lấy từ registry:
    /// HKLM\SOFTWARE\Microsoft\Cryptography  (giá trị string MachineGuid)
    /// </summary>
    public static string GetMachineFingerprint()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        string machineGuid = key?.GetValue("MachineGuid")?.ToString()
            ?? throw new InvalidOperationException("Không đọc được MachineGuid từ registry.");

        byte[] bytes = Encoding.UTF8.GetBytes(
            "WarehouseLicense|v1|" + machineGuid + "|" + Environment.MachineName.ToUpperInvariant());

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Device ID ngắn hiển thị trên app: "DEV-" + 16 ký tự đầu của fingerprint.</summary>
    public static string GetShortDeviceId() => "DEV-" + GetMachineFingerprint()[..16];
}
```

### 4.2 Cách dùng trong app WinForms

```csharp
// Nhúng một lần (const, không đổi giữa các bản build của CÙNG một "vendor"):
const string OfficeSecret = "DÁNG BASE64 TỪ office-secret.txt VÀO ĐÂY";
const string ProductId    = "GPT9000";   // phải khớp đúng productId khi cấp license

// Gọi lúc khởi động, TRƯỚC khi mở MainForm:
string datPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "office.dat");

if (!OfficeDatReader.TryOpenOnThisMachine(datPath, ProductId, OfficeSecret,
        out string licenseJson, out string error))
{
    MessageBox.Show("License không hợp lệ:\n" + error,
        "Bản quyền", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;   // KHÔNG mở app
}

// licenseJson là chuỗi JSON envelope đã ký — chuyển sang bước 5 để xác minh chữ ký.
```

### 4.3 Bản rút gọn (nếu bạn tham chiếu `LicenseShared.dll`)

Nếu bạn tham chiếu assembly `LicenseShared.dll` (project reference hoặc DLL copy tay), toàn bộ
phần trên gói gọn trong 1 dòng:

```csharp
using LicenseShared;

byte[] blob = File.ReadAllBytes("office.dat");
byte[] secret = Convert.FromBase64String(OfficeSecret);

bool ok = OfficeLicenseFile.TryOpenForThisMachine(blob, ProductId, secret,
    out string licenseJson, out string error);
```

`TryOpenForThisMachine` tự thử fingerprint, DEV- id rồi các MAC của máy (kể cả card không kết nối mạng). API đầy đủ của
`OfficeLicenseFile`:

| Thành phần | Mục đích |
|---|---|
| `TryOpenForThisMachine(file, productId, secret, out json, out error)` | Thường dùng nhất — mở trên máy hiện tại |
| `TryOpen(file, productId, bindingValue, secret, out json, out error)` | Mở với binding value tường minh (test, công cụ) |
| `Create(json, productId, bindingValue, secret)` | Seal license ra blob (dùng bên phía issuer) |
| `DeriveKey(secret, salt, productId, bindingValue)` | Derive khóa HKDF (test/tính toán riêng) |
| `CreateSecret()` | Sinh secret 32 byte ngẫu nhiên |
| `SecretSizeBytes` (=32), `DefaultFileName` ("office.dat") | Hằng số |

---

## 5. Xác minh sau khi giải mã (bắt buộc)

Sau khi có `licenseJson`, **bạn phải xác minh chữ ký** — đây là bước biến "chuỗi trong file"
thành "bằng chứng hợp lệ". Thứ tự đúng:

```
1. Parse JSON  →  SignedLicense { format, algorithm, keyId, payloadBase64, signatureBase64 }
2. Kiểm format == "WLC-1"
3. Verify chữ ký RSA-PSS/ECDSA-SHA256 trên payloadBytes với PUBLIC KEY của bạn
   → sai chữ ký = TỪ CHỐI (không tin nội dung)
4. Decode payloadBytes → LicensePayload { productId, deviceId, bindingMode,
   licenseType, notBeforeUtc, expiresAtUtc, ... }
5. Kiểm productId khớp
6. Kiểm deviceId khớp máy hiện tại (0 = fingerprint, 1 = DEV- id, 2 = MAC; từ chối mode khác)
7. Kiểm notBeforeUtc ≤ now ≤ expiresAtUtc (hoặc perpetual)
8. (Khuyến nghị) Kiểm chống lùi đồng hồ (ClockGuard)
```

Nếu bạn dùng `LicenseShared.dll` mới, bước 5–7 gọi 1 dòng (ClockGuard ở bước 8 cần gọi riêng):

```csharp
var result = LicenseRules.Evaluate(payload, ProductId, fingerprint, shortId, DateTimeOffset.UtcNow);
if (!result.IsValid) { /* từ chối */ }
```

Nếu tự viết, lưu ý:

- **Payload được ký dưới dạng binary** (`payloadBase64` = base64 của binary payload, **không**
  phải JSON). Xem `LicenseCodec.SerializePayload` / `DeserializePayload` trong source để biết
  thứ tự field chính xác.
- Dùng `RSASignaturePadding.Pss` cho RSA (không phải PKCS1).
- ECDSA dùng `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`.
- So sánh keyId bất phân biệt hoa/thường.

---

## 6. Quy trình vận hành (vendor side)

1. **Một lần duy nhất:** Settings → *Export client secret* → nhận `office-secret.txt`
   (chuỗi base64). Nhúng vào app bạn bán. **Giữ bí mật** — ai có secret + app có thể *đọc*
   license (nhưng không *phát hành* được).
2. **Mỗi khách:** khách gửi fingerprint, DEV- id hoặc MAC → tool → chọn đúng kiểu ràng buộc trong *Cấp license* →
   kết quả → **Lưu office.dat** → gửi file này cho khách.
3. Khách đặt `office.dat` cạnh exe (hoặc thư mục data của app) → app tự kiểm khi khởi động.
4. **Gia hạn:** tool → *Gia hạn* → lưu `office.dat` mới → khách thay file cũ.

Form cấp license nhập theo **giờ máy**, hiển thị rõ múi giờ; payload vẫn lưu UTC.
Ví dụ `2026-10-23 16:00:00` ở UTC+7 được lưu `2026-10-23 09:00:00Z`.
License cũ không được tự sửa. Nếu đã cấp nhầm 16:00 UTC (23:00 giờ Việt Nam), cần cấp file thay thế.
MAC có thể thay đổi hoặc bị giả mạo; thay card mạng hoặc bật MAC ngẫu nhiên có thể cần cấp lại license.

---

## 7. Tra cứu lỗi nhanh

| Thông báo | Nguyên nhân | Cách xử lý |
|---|---|---|
| `Đây không phải file office.dat` | File sai magic — đúng file chưa? | Khách gửi nhầm file `.license.json`/`.csv` |
| `Phiên bản file không được hỗ trợ` | File do bản tool mới hơn tạo | Nâng app đọc theo cùng bản source |
| `File license bị hỏng` | `length` không khớp kích thước file | File truyền tải bị cắt — gửi lại |
| `Không mở được file trên máy này...` | Sai secret / sai fingerprint / file bị sửa | Đối chiếu secret nhúng, kiểm khách có đúng máy không |
| `Vendor secret bị thiếu hoặc sai độ dài` | `secretBase64` rỗng hoặc decode ra ≠ 32 byte | Kiểm tra lại chuỗi nhúng, bỏ khoảng trắng thừa |

> Mọi trường hợp fail đều **từ chối mở app** — fail-closed, không có nhánh "mở tạm".

---

## 8. Hạn chế cần biết (trung thực)

1. **Không có DRM bất khả xâm phạm.** Offline licensing không thể chống kẻ có khả năng
   reverse-engineer + debug app. Mục tiêu thực tế: chống **chia sẻ license lậu** (copy file sang
   máy khác), chống **sửa hạn**, chống **đọc thông tin license**, và chống **tự phát hành
   license** (bằng chữ ký). Cái cuối là hàng rào vững nhất.
2. **Lưu file plaintext trên đĩa khách vẫn là lựa chọn hợp lý** sau khi mở: DPAPI-protect nó
   (`LicenseStore` trong `LicenseShared` làm việc này) để file không copy sang máy/account khác.
3. **MachineGuid đổi khi cài lại Windows** → khách mất quyền, cần cấp lại. Đây là đánh đổi
   cố ý của việc buộc máy; cần chính sách hỗ trợ.
4. **Đảo clock:** expiry là UTC timestamp. Kẻ gian lùi giờ hệ thống có thể kéo dài hạn — chống
   bằng `ClockGuard` (ghi mốc thời gian thấy lâu nhất, DPAPI-protected, phát hiện lùi giờ).
   File `.clock` bị xóa thì mất dấu — giới hạn cố hữu của licensing offline.
5. **Repo này (Warehouse License Studio) không liên quan gì đến keygen tự làm trong
   `Electronic_Safely\tools\keygen`** — hai định dạng key khác hẳn nhau. Chỉ chọn một hệ thống
   cho bản bán.

---

## 9. Nguồn code để đối chiếu

| File trong repo | Vai trò |
|---|---|
| `LicenseShared/OfficeLicenseFile.cs` | Codec `office.dat` — **nguồn chính xác nhất**, tài liệu này mô phỏng theo nó |
| `LicenseShared/LicenseCrypto.cs` | Ký/xác minh RSA-PSS, ECDSA, key id |
| `LicenseShared/LicenseCodec.cs` | Binary payload + mã kích hoạt `WLC1-...` |
| `LicenseShared/LicenseRules.cs` | Quy tắc productId/machine/dates + `PublicKeyBundleSerializer` |
| `LicenseShared/MachineFingerprint.cs` | Công thức fingerprint |
| `LicenseShared/ClockGuard.cs`, `LicenseStore.cs` | Chống lùi giờ + lưu license DPAPI |
| `LicenseGenerator/Services/OfficeSecretStore.cs` | Quản lý secret phía issuer |
| `LicenseSelfTest/Program.cs` | 100 test — mục `office.dat ...` kiểm chứng mọi nhánh lỗi |
