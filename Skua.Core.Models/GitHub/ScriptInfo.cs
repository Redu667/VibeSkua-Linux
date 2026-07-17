using Newtonsoft.Json;
using System.Security.Cryptography;

namespace Skua.Core.Models.GitHub;

public class ScriptInfo
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("tags")]
    public string[] Tags { get; set; }

    [JsonProperty("path")]
    public string FilePath { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("creationDate")]
    public DateTime? CreationDate { get; set; }

    [JsonProperty("sha256")]
    public string? Sha256 { get; set; }

    [JsonProperty("fileName")]
    public string FileName { get; set; }

    [JsonProperty("downloadUrl")]
    public string DownloadUrl { get; set; }

    public string RelativePath => FilePath == FileName ? "Scripts/" : $"Scripts/{FilePath.Replace(FileName, "")}";

    public string LocalFile => Path.Combine(ClientFileSources.SkuaScriptsDIR, FilePath);

    public string ManagerLocalFile => Path.Combine(ClientFileSources.SkuaScriptsDIR, FilePath);

    public bool Downloaded => File.Exists(LocalFile);

    public int LocalSize => Downloaded ? (int)new FileInfo(LocalFile).Length : 0;

    private string? _cachedSha256;
    private long _cachedSha256Size = -1;
    private DateTime _cachedSha256Time = DateTime.MinValue;

    public string? LocalSha256
    {
        get
        {
            if (!Downloaded) return null;
            try
            {
                var fileInfo = new FileInfo(LocalFile);
                if (_cachedSha256 != null && _cachedSha256Size == fileInfo.Length && _cachedSha256Time == fileInfo.LastWriteTimeUtc)
                    return _cachedSha256;

                using SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
                using FileStream stream = File.OpenRead(LocalFile);
                byte[] hash = sha256.ComputeHash(stream);
                _cachedSha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                _cachedSha256Size = fileInfo.Length;
                _cachedSha256Time = fileInfo.LastWriteTimeUtc;
                return _cachedSha256;
            }
            catch { return null; }
        }
    }

    public bool Outdated => Downloaded && (LocalSize != Size || (!string.IsNullOrEmpty(Sha256) && LocalSha256 != Sha256));

    public override string ToString()
    {
        return FileName;
    }
}