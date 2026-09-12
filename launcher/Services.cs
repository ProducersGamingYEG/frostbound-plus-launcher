using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using SharpCompress.Archives.Rar;
using SharpCompress.Readers;

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
        if(ClientDownload is { } d) { RequirePublicHttps(d.Url); if(d.SizeBytes<=0 || !Regex.IsMatch(d.Sha256 ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Download must specify its size and SHA-256 checksum."); if(d.ArchiveType is not ("zip" or "rar")) throw new InvalidDataException("The launcher supports ZIP and RAR client archives."); }
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
        var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(40)}; client.DefaultRequestHeaders.UserAgent.ParseAdd("FrostboundPlus/0.1.2"); return client;
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
    public static void InstallFeatures(string folder)
    {
        var addons=Path.GetFullPath(Path.Combine(folder,"Interface","AddOns"));
        var backup=Path.Combine(folder,"FrostboundAddonBackups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        using var source=typeof(Services).Assembly.GetManifestResourceStream("FrostboundAddons.zip") ?? throw new IOException("Bundled client features are unavailable.");
        using var archive=new ZipArchive(source,ZipArchiveMode.Read);
        foreach(var entry in archive.Entries)
        {
            if(string.IsNullOrEmpty(entry.Name)) continue;
            var target=SafeArchivePath(addons+Path.DirectorySeparatorChar,entry.FullName);
            for(var check=new DirectoryInfo(Path.GetDirectoryName(target)!);check!=null;check=check.Parent)
            {
                if(check.Exists && (check.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("An addon folder is redirected. Choose an ordinary client folder.");
                if(check.FullName==Path.GetFullPath(folder))break;
            }
            if(File.Exists(target) && (File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0) throw new IOException("An addon file is redirected.");
            using var input=entry.Open();using var memory=new MemoryStream();input.CopyTo(memory);var bytes=memory.ToArray();
            if(File.Exists(target))
            {
                if(File.ReadAllBytes(target).SequenceEqual(bytes))continue;
                var original=Path.Combine(backup,entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(original)!);File.Copy(target,original);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,bytes);
        }
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
            using var response=await OpenDownload(http,info.Url,offset,ct); response.EnsureSuccessStatusCode();
            if(response.StatusCode==HttpStatusCode.PartialContent) { if(response.Content.Headers.ContentRange?.From!=offset || response.Content.Headers.ContentRange?.Length!=info.SizeBytes) throw new InvalidDataException("Download server returned an inconsistent resume range."); }
            else offset=0;
            await using var target=new FileStream(output,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.None,131072,true);
            await using var source=await response.Content.ReadAsStreamAsync(ct); byte[] buffer=new byte[131072]; int read; var last=DateTime.MinValue;
            while((read=await source.ReadAsync(buffer,ct))>0){if(offset+read>info.SizeBytes)throw new InvalidDataException("Download exceeds the approved size.");await target.WriteAsync(buffer.AsMemory(0,read),ct);offset+=read;if((DateTime.UtcNow-last).TotalMilliseconds>250){progress.Report($"Downloading {offset*100/info.SizeBytes}% — {offset/1048576:N0} / {info.SizeBytes/1048576:N0} MB");last=DateTime.UtcNow;}}
        }
        progress.Report("Verifying download checksum…"); await using var file=File.OpenRead(output);
        if(file.Length!=info.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(info.Sha256,StringComparison.OrdinalIgnoreCase)) { file.Close(); File.Delete(output); throw new InvalidDataException("Download verification failed. The unverified archive was removed; retry the download."); }
    }
    public static string NormalizeDownloadUrl(string url)
    {
        var uri=new Uri(url); if(uri.Host!="drive.google.com") return url;
        var match=Regex.Match(uri.AbsolutePath,@"^/file/d/([a-zA-Z0-9_-]+)/");
        if(!match.Success)return url;
        return "https://drive.usercontent.google.com/download?id="+match.Groups[1].Value+"&export=download&confirm=t";
    }
    public static string? ParseGoogleConfirmation(string html)
    {
        var form=Regex.Match(html,@"<form\b[^>]*action\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<body>[\s\S]*?)</form>",RegexOptions.IgnoreCase);
        if(!form.Success)return null;
        if(!Uri.TryCreate(WebUtility.HtmlDecode(form.Groups["url"].Value),UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host is not ("drive.usercontent.google.com" or "drive.google.com") || !uri.AbsolutePath.EndsWith("/download")) return null;
        var fields=new List<string>();
        foreach(Match input in Regex.Matches(form.Groups["body"].Value,@"<input\b[^>]*>",RegexOptions.IgnoreCase))
        {
            var name=Regex.Match(input.Value,@"\bname\s*=\s*[""']([^""']*)[""']",RegexOptions.IgnoreCase);var value=Regex.Match(input.Value,@"\bvalue\s*=\s*[""']([^""']*)[""']",RegexOptions.IgnoreCase);
            if(name.Success&&value.Success)fields.Add(Uri.EscapeDataString(WebUtility.HtmlDecode(name.Groups[1].Value))+"="+Uri.EscapeDataString(WebUtility.HtmlDecode(value.Groups[1].Value)));
        }
        return fields.Count==0?null:uri.GetLeftPart(UriPartial.Path)+"?"+string.Join('&',fields);
    }
    static async Task<HttpResponseMessage> OpenDownload(HttpClient http,string url,long offset,CancellationToken ct)
    {
        var next=NormalizeDownloadUrl(url);
        for(int attempt=0;attempt<3;attempt++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,next);if(offset>0)request.Headers.Range=new RangeHeaderValue(offset,null);
            var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))return response;
            using(response)
            {
                var body=await response.Content.ReadAsStringAsync(ct);var host=response.RequestMessage?.RequestUri?.Host;
                var confirmation=host is "drive.google.com" or "drive.usercontent.google.com"?ParseGoogleConfirmation(body):null;
                if(confirmation==null)break;next=confirmation;
            }
        }
        throw new InvalidOperationException("The download host requires browser confirmation, sign-in, or has reached a download limit. Use Open client source to download the archive in your browser, then Install archive. The same checksum and client checks apply.");
    }
    public static async Task VerifyArchive(string path,DownloadInfo info,IProgress<string> progress,CancellationToken ct)
    {
        progress.Report("Verifying local archive checksum…");await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan);
        if(stream.Length!=info.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(info.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("This archive does not match the approved client download. It has not been installed or modified.");
    }
    public static void ExtractArchive(string archive,string destination,string archiveType,CancellationToken ct)
    {
        if(archiveType=="zip"){ExtractZip(archive,destination,ct);return;}
        if(archiveType!="rar")throw new InvalidDataException("Unsupported archive type.");
        var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;Directory.CreateDirectory(root);
        using var rar=RarArchive.OpenArchive(archive);using var reader=rar.ExtractAllEntries();long expanded=0;
        while(reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();var entry=reader.Entry;var key=entry.Key??throw new InvalidDataException("Archive entry has no path.");
            var path=SafeArchivePath(root,key);
            if(!string.IsNullOrEmpty(entry.LinkTarget) || (entry.Attrib.GetValueOrDefault() & (int)FileAttributes.ReparsePoint)!=0 || entry.IsEncrypted || entry.IsSplitAfter)throw new InvalidDataException("Linked, encrypted or split RAR entries are not supported.");
            expanded+=entry.Size;if(expanded>40L*1024*1024*1024)throw new InvalidDataException("Archive expands beyond the client size limit.");
            if(entry.IsDirectory){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write);using var source=reader.OpenEntryStream();
            byte[] buffer=new byte[131072];long written=0;int count;
            while((count=source.Read(buffer))>0){ct.ThrowIfCancellationRequested();written+=count;if(written>entry.Size)throw new InvalidDataException("Archive entry exceeds its declared size.");target.Write(buffer,0,count);}
            if(written!=entry.Size)throw new InvalidDataException("Incomplete RAR entry.");
        }
    }
    public static string SafeArchivePath(string root,string key)
    {
        var path=Path.GetFullPath(Path.Combine(root,key));
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||key.Contains(':')||Path.IsPathRooted(key)||key.Split('/','\\').Any(x=>x==".."||x.EndsWith('.')||x.EndsWith(' ')))throw new InvalidDataException("Archive contains an unsafe path.");
        return path;
    }
    public static void ExtractZip(string archive,string destination,CancellationToken ct)
    {
        var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar; Directory.CreateDirectory(root);
        using var zip=ZipFile.OpenRead(archive); long expanded=0;
        foreach(var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested(); var path=SafeArchivePath(root,entry.FullName);
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
