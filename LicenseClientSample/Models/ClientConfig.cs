using System.IO;

namespace LicenseClientSample.Models;

public sealed class ClientConfig
{
    public string ProductId { get; set; } = "WarehouseLabelCompareMaterial";
    public string ProductName { get; set; } = "WarehouseLabelCompareMaterial";
    public string Vendor { get; set; } = "Han Manufacturing Systems";
    public string Version { get; set; } = "1.0.0";

    public static ClientConfig Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return new ClientConfig();

            string json = File.ReadAllText(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<ClientConfig>(
                json,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            return config ?? new ClientConfig();
        }
        catch (Exception)
        {
            // A damaged config falls back to the compiled defaults.
            return new ClientConfig();
        }
    }
}
