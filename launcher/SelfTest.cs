using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
namespace FrostboundPlus;
static class SelfTest
{
    sealed class FakeHttp(byte[] payload,bool ignoreRange=false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){long start=ignoreRange?0:request.Headers.Range?.Ranges.First().From??0;var response=new HttpResponseMessage(start>0?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(payload[(int)start..])};if(start>0)response.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(start,payload.Length-1,payload.Length);return Task.FromResult(response);}
    }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"frostbound-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var data=Enumerable.Range(0,10000).Select(i=>(byte)i).ToArray();var hash=Convert.ToHexString(SHA256.HashData(data));var info=new DownloadInfo("https://example.com/client.zip",hash,data.Length,"zip");
            foreach(var ignore in new[]{false,true}){var path=Path.Combine(root,"download");File.WriteAllBytes(path,data[..321]);using var http=new HttpClient(new FakeHttp(data,ignore));await Services.Download(http,info,path,new Progress<string>(),CancellationToken.None);Assert(File.ReadAllBytes(path).SequenceEqual(data),"Resume/restart corrupted download");}
            using(var http=new HttpClient(new FakeHttp(data))){var bad=Path.Combine(root,"bad");try{await Services.Download(http,info with{Sha256=new string('0',64)},bad,new Progress<string>(),CancellationToken.None);throw new Exception("Accepted invalid checksum");}catch(InvalidDataException){Assert(!File.Exists(bad),"Kept invalid archive");}}
            var zip=Path.Combine(root,"unsafe.zip");using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)){using var writer=new StreamWriter(a.CreateEntry("../escape.txt").Open());writer.Write("bad");}try{Services.ExtractZip(zip,Path.Combine(root,"unpack"),CancellationToken.None);throw new Exception("Accepted traversal");}catch(InvalidDataException){}Assert(!File.Exists(Path.Combine(root,"escape.txt")),"Traversal escaped staging");
            var client=Path.Combine(root,"client");Directory.CreateDirectory(Path.Combine(client,"WTF"));File.WriteAllText(Path.Combine(client,"WTF","Config.wtf"),"SET gxResolution \"1920x1080\"\nSET realmlist \"old\"\n");Services.Configure(client,"realm.example.com");var config=File.ReadAllText(Path.Combine(client,"WTF","Config.wtf"));Assert(config.Contains("1920x1080")&&!config.Contains("\"old\""),"Graphics configuration was not preserved");
            try{Services.VerifyClient(client);throw new Exception("Accepted missing executable");}catch(InvalidDataException){}
            try{Manifest.RequirePublicHttps("https://localhost");throw new Exception("Accepted localhost");}catch(InvalidDataException){}
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-results.txt"),"PASS: range resume, full-response restart, checksum rejection, archive traversal rejection, configuration preservation, client validation, public URL validation.");
        }finally{Directory.Delete(root,true);}
    }
}
