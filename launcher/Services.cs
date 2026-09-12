using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace FrostboundPlus;
public sealed record DownloadInfo(string Url, string Sha256, long SizeBytes, string ArchiveType);
public sealed record Manifest(int SchemaVersion, string LauncherVersion, string RealmName, string RealmAddress, int ClientBuild, string? RegistrationBaseUrl, string? RegistrationCertificateSha256, DownloadInfo? ClientDownload)
{
    public void Validate()
    {
        if(SchemaVersion != 1 || ClientBuild != 5875 || string.IsNullOrWhiteSpace(RealmName)) throw new InvalidDataException("Unsupported launcher manifest or client build.");
        if(!Regex.IsMatch(RealmAddress ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9.\-]*(?::[0-9]{1,5})?$")) throw new InvalidDataException("Realm address is not configured correctly.");
        RequirePublicHttps(RegistrationBaseUrl, true);
        if(!string.IsNullOrEmpty(RegistrationCertificateSha256) && !Regex.IsMatch(RegistrationCertificateSha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid service certificate fingerprint.");
        if(ClientDownload is { } d) { RequirePublicHttps(d.Url); if(d.SizeBytes<=0 || !Regex.IsMatch(d.Sha256 ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Download must specify its size and SHA-256 checksum."); if(d.ArchiveType != "zip") throw new InvalidDataException("This launcher supports ZIP client archives. Ask the realm operator for a ZIP download."); }
    }
    public static void RequirePublicHttps(string? value, bool optional=false)
    {
        if(optional && string.IsNullOrWhiteSpace(value)) return;
        if(!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme!="https" || u.IsLoopback || u.Host.EndsWith(".localhost") || !string.IsNullOrEmpty(u.UserInfo)) throw new InvalidDataException("A public HTTPS URL is required.");
    }
}
public static class Services
{
    public const string ManifestUrl="https://github.com/ProducersGamingYEG/frostbound-plus-launcher/releases/latest/download/launcher-manifest.json";
    public static readonly string StateDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrostboundPlus");
    public static HttpClient CreateHttp(Manifest? manifest=null)
    {
        var handler=new HttpClientHandler();
        if(!string.IsNullOrWhiteSpace(manifest?.RegistrationCertificateSha256))
        {
            var expected=manifest.RegistrationCertificateSha256;
            handler.AllowAutoRedirect=false;
            handler.ServerCertificateCustomValidationCallback=(request,cert,chain,errors)=> cert!=null && request.RequestUri?.Host==new Uri(manifest.RegistrationBaseUrl!).Host && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),SHA256.HashData(cert.RawData));
        }
        var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(40)}; client.DefaultRequestHeaders.UserAgent.ParseAdd("FrostboundPlus/0.1.0"); return client;
    }
    public static async Task<Manifest> LoadManifest(CancellationToken ct)
    {
        using var http=CreateHttp(); var m=await http.GetFromJsonAsync<Manifest>(ManifestUrl,ct) ?? throw new InvalidDataException("Empty launcher manifest."); m.Validate(); return m;
    }
    public static void VerifyClient(string folder)
    {
        var exe=Path.Combine(folder,"WoW.exe"); if(!File.Exists(exe)) throw new InvalidDataException("Choose the folder containing WoW.exe from your Vanilla 1.12.1 client.");
        var v=FileVersionInfo.GetVersionInfo(exe);
        if(v.FileMajorPart!=1 || v.FileMinorPart!=12 || v.FileBuildPart!=1 || v.FilePrivatePart!=5875) throw new InvalidDataException($"This client is {v.FileVersion ?? "unknown"}. Frostbound Plus requires Vanilla 1.12.1 (5875).");
        if(!Directory.Exists(Path.Combine(folder,"Data"))) throw new InvalidDataException("The client's Data folder is missing.");
    }
    public static void Configure(string folder,string address)
    {
        if(!Regex.IsMatch(address,@"^[a-zA-Z0-9][a-zA-Z0-9.\-]*(?::[0-9]{1,5})?$")) throw new InvalidDataException("Invalid realm address.");
        File.WriteAllText(Path.Combine(folder,"realmlist.wtf"),$"set realmlist \"{address}\"\r\n");
        var wtf=Path.Combine(folder,"WTF"); Directory.CreateDirectory(wtf); var config=Path.Combine(wtf,"Config.wtf");
        var lines=File.Exists(config)?File.ReadAllLines(config).Where(x=>!Regex.IsMatch(x,@"^\s*SET\s+realmlist\s",RegexOptions.IgnoreCase)).ToList():new List<string>();
        lines.Add($"SET realmlist \"{address}\""); File.WriteAllLines(config,lines);
    }
    public static async Task Download(HttpClient http, DownloadInfo info,string output,IProgress<string> progress,CancellationToken ct)
    {
        var offset=File.Exists(output)?new FileInfo(output).Length:0; if(offset>info.SizeBytes){File.Delete(output);offset=0;}
        if(offset<info.SizeBytes)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,info.Url); if(offset>0) req.Headers.Range=new RangeHeaderValue(offset,null);
            using var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct); response.EnsureSuccessStatusCode();
            if(response.StatusCode==HttpStatusCode.PartialContent) { if(response.Content.Headers.ContentRange?.From!=offset || response.Content.Headers.ContentRange?.Length!=info.SizeBytes) throw new InvalidDataException("Download server returned an inconsistent resume range."); }
            else offset=0;
            await using var target=new FileStream(output,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,131072,true);
            await using var source=await response.Content.ReadAsStreamAsync(ct); byte[] buffer=new byte[131072]; int read; var last=DateTime.MinValue;
            while((read=await source.ReadAsync(buffer,ct))>0){if(offset+read>info.SizeBytes)throw new InvalidDataException("Download exceeds the approved size.");await target.WriteAsync(buffer.AsMemory(0,read),ct);offset+=read;if((DateTime.UtcNow-last).TotalMilliseconds>250){progress.Report($"Downloading {offset*100/info.SizeBytes}% — {offset/1048576:N0} / {info.SizeBytes/1048576:N0} MB");last=DateTime.UtcNow;}}
        }
        progress.Report("Verifying download checksum…"); await using var file=File.OpenRead(output);
        if(file.Length!=info.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(info.Sha256,StringComparison.OrdinalIgnoreCase)) { file.Close(); File.Delete(output); throw new InvalidDataException("Download verification failed. The unverified archive was removed; retry the download."); }
    }
    public static void ExtractZip(string archive,string destination,CancellationToken ct)
    {
        var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar; Directory.CreateDirectory(root);
        using var zip=ZipFile.OpenRead(archive); long expanded=0;
        foreach(var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested(); var path=Path.GetFullPath(Path.Combine(root,entry.FullName));
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':') || (entry.ExternalAttributes>>16 & 0xF000)==0xA000) throw new InvalidDataException("Archive contains an unsafe path or symbolic link.");
            expanded+=entry.Length; if(expanded>40L*1024*1024*1024) throw new InvalidDataException("Archive expands beyond the client size limit.");
            if(entry.Name.Length==0) { Directory.CreateDirectory(path);continue; } Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path,false);
        }
    }
    public static async Task<string> Post(Manifest m,string route,object body,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(m.RegistrationBaseUrl)) throw new InvalidOperationException("Account service has not been configured by the realm operator.");
        using var http=CreateHttp(m); using var response=await http.PostAsJsonAsync(m.RegistrationBaseUrl.TrimEnd('/')+route,body,ct);
        var text=await response.Content.ReadAsStringAsync(ct); string? message=null; try {using var json=JsonDocument.Parse(text); if(json.RootElement.TryGetProperty("message",out var p)) message=p.GetString();}catch(JsonException){}
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(message??$"Account service returned HTTP {(int)response.StatusCode}.");return message??"Request completed.";
    }
}
