using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrostboundPlus;
sealed class LauncherForm : Form
{
    readonly Label status=new(){AutoSize=true,MaximumSize=new Size(700,0),Text="Loading realm configuration…"};
    readonly TextBox folder=new(){Width=680,ReadOnly=true};
    readonly FlowLayoutPanel actions=new(){AutoSize=true,Width=710};
    readonly CancellationTokenSource lifetime=new(); CancellationTokenSource? operation; Manifest? manifest;
    public LauncherForm()
    {
        Text="Frostbound Plus • 0.1.1"; ClientSize=new Size(760,480); MinimumSize=new Size(780,510); BackColor=Color.FromArgb(17,27,39); ForeColor=Color.White; Font=new Font("Segoe UI",11);
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(24),AutoScroll=true};Controls.Add(layout);
        layout.Controls.Add(new Label{Text="FROSTBOUND PLUS",Font=new Font("Segoe UI",25,FontStyle.Bold),AutoSize=true});
        layout.Controls.Add(new Label{Text="Vanilla 1.12.1 • Build 5875 • Level 60 • Fresh accounts",AutoSize=true,Margin=new Padding(0,8,0,22)});
        layout.Controls.Add(status); layout.Controls.Add(new Label{Text="Your client folder",AutoSize=true,Margin=new Padding(0,20,0,4)});layout.Controls.Add(folder);layout.Controls.Add(actions);
        Add("Choose client",Choose);Add("Download client",()=>Run(()=>Install()));Add("Install archive",()=>Run(ImportArchive));Add("Open client source",()=>Run(OpenSource));Add("Register",()=>Account(false));Add("Recover account",()=>Account(true));Add("Refresh status",()=>Run(RefreshRealm));Add("Play",()=>Run(Play));
        var cancel=new Button{Text="Cancel download",AutoSize=true,BackColor=Color.FromArgb(45,64,82)};cancel.Click+=(_,_)=>operation?.Cancel();layout.Controls.Add(cancel);
        Directory.CreateDirectory(Services.StateDirectory);var settings=Path.Combine(Services.StateDirectory,"client-folder.txt");if(File.Exists(settings))folder.Text=File.ReadAllText(settings);
        Shown+=(_,_)=>Run(RefreshRealm);FormClosing+=(_,_)=>{operation?.Cancel();lifetime.Cancel();};
    }
    void Add(string name,Action action){var b=new Button{Text=name,AutoSize=true,Height=38,BackColor=Color.FromArgb(45,64,82),Margin=new Padding(0,12,10,0)};b.Click+=(_,_)=>action();actions.Controls.Add(b);}
    async void Run(Func<Task> task){actions.Enabled=false;try{await task();}catch(OperationCanceledException){status.Text="Cancelled. Download progress is saved for your next attempt.";}catch(Exception e){status.Text=e.Message;}finally{if(!IsDisposed)actions.Enabled=true;}}
    void Choose(){using var picker=new FolderBrowserDialog{Description="Select your Vanilla 1.12.1 client folder",UseDescriptionForTitle=true};if(picker.ShowDialog()!=DialogResult.OK)return;try{Services.VerifyClient(picker.SelectedPath);SaveFolder(picker.SelectedPath);status.Text="Vanilla 1.12.1 (5875) client verified.";}catch(Exception e){MessageBox.Show(this,e.Message,"Client selection");}}
    void SaveFolder(string path){folder.Text=path;File.WriteAllText(Path.Combine(Services.StateDirectory,"client-folder.txt"),path);}
    async Task RefreshRealm()
    {
        manifest=await Services.LoadManifest(lifetime.Token);status.Text=$"{manifest.RealmName} • Configuration loaded";
        if(string.IsNullOrWhiteSpace(manifest.RegistrationBaseUrl)){status.Text+=". Live status and account service are not configured.";return;}
        using var http=Services.CreateHttp(manifest);using var response=await http.GetAsync(manifest.RegistrationBaseUrl.TrimEnd('/')+"/realm/status",lifetime.Token);response.EnsureSuccessStatusCode();
        using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));var r=data.RootElement;
        status.Text=$"{manifest.RealmName} • {(r.GetProperty("online").GetBoolean()?"Online":"Offline")} • Players: {r.GetProperty("playersOnline")} • Bots: {r.GetProperty("botsOnline")}";
    }
    Task OpenSource(){var d=manifest?.ClientDownload??throw new InvalidOperationException("The realm operator has not configured a client source.");Manifest.RequirePublicHttps(d.Url);Process.Start(new ProcessStartInfo(d.Url){UseShellExecute=true});status.Text="Download the archive in your browser, then choose Install archive.";return Task.CompletedTask;}
    async Task ImportArchive(){using var picker=new OpenFileDialog{Title="Choose the approved client archive",Filter="Client archive (*.rar;*.zip)|*.rar;*.zip",CheckFileExists=true};if(picker.ShowDialog()==DialogResult.OK)await Install(picker.FileName);}
    async Task Install(string? localArchive=null)
    {
        var m=manifest??throw new InvalidOperationException("Refresh configuration before downloading.");var d=m.ClientDownload??throw new InvalidOperationException("The realm operator has not configured an approved client download. Use Choose client to import an existing Vanilla 1.12.1 installation.");
        using var picker=new FolderBrowserDialog{Description="Choose a parent folder. A new Frostbound Plus Client folder will be created.",UseDescriptionForTitle=true};if(picker.ShowDialog()!=DialogResult.OK)return;
        var destination=Path.Combine(picker.SelectedPath,"Frostbound Plus Client");if(Directory.Exists(destination))throw new IOException("Frostbound Plus Client already exists here. Choose another parent folder or import that client.");
        using var cts=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);operation=cts;
        var archive=localArchive??Path.Combine(Services.StateDirectory,d.Sha256.ToLowerInvariant()+"."+d.ArchiveType+".part");var staging=destination+".install-"+Guid.NewGuid().ToString("N");
        try
        {
            var progress=new Progress<string>(s=>status.Text=s);
            if(localArchive!=null)await Services.VerifyArchive(archive,d,progress,cts.Token);
            else{using var http=Services.CreateHttp();http.Timeout=Timeout.InfiniteTimeSpan;await Services.Download(http,d,archive,progress,cts.Token);}
            status.Text="Extracting verified client…";await Task.Run(()=>Services.ExtractArchive(archive,staging,d.ArchiveType,cts.Token),cts.Token);
            var matches=Directory.EnumerateFiles(staging,"WoW.exe",SearchOption.AllDirectories).ToArray();if(matches.Length!=1)throw new InvalidDataException("Archive must contain exactly one WoW.exe client.");
            var root=Path.GetDirectoryName(matches[0])!;Services.VerifyClient(root);Services.Configure(root,m.RealmAddress);Directory.Move(root,destination);SaveFolder(destination);status.Text="Client installed and verified. Ready to play.";
        }
        finally{operation=null;if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    Task Play(){var m=manifest??throw new InvalidOperationException("Refresh configuration before playing.");Services.VerifyClient(folder.Text);Services.Configure(folder.Text,m.RealmAddress);Process.Start(new ProcessStartInfo(Path.Combine(folder.Text,"WoW.exe")){WorkingDirectory=folder.Text,UseShellExecute=true});status.Text="Game started. Use your Frostbound Plus account to sign in.";return Task.CompletedTask;}
    void Account(bool recovery){if(manifest==null){MessageBox.Show(this,"Refresh configuration first.");return;}if(string.IsNullOrWhiteSpace(manifest.RegistrationBaseUrl)){MessageBox.Show(this,"The realm operator has not configured the account service.");return;}using var dialog=new AccountForm(manifest,recovery);dialog.ShowDialog(this);}
}
sealed class AccountForm : Form
{
    readonly TextBox username=new(),email=new(),password=new(){UseSystemPasswordChar=true},code=new();readonly Label feedback=new(){AutoSize=true,MaximumSize=new Size(410,0)};readonly Manifest manifest;readonly bool recovery;
    public AccountForm(Manifest m,bool recover)
    {
        manifest=m;recovery=recover;Text=recover?"Recover Frostbound Plus account":"Create Frostbound Plus account";ClientSize=new Size(470,500);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(22)};Controls.Add(panel);
        foreach(var pair in new[]{("Account name (1–16 letters, numbers or underscores)",username),("Email",email),("Password (8–16 printable ASCII characters)",password),("Email verification code",code)}){panel.Controls.Add(new Label{Text=pair.Item1,AutoSize=true});pair.Item2.Width=410;panel.Controls.Add(pair.Item2);}
        var send=new Button{Text="Send verification code",AutoSize=true};var submit=new Button{Text=recover?"Reset password":"Create account",AutoSize=true};panel.Controls.Add(send);panel.Controls.Add(submit);panel.Controls.Add(feedback);
        send.Click+=async(_,_)=>await Execute(send,async()=>{ValidateFields(false);return await Services.Post(manifest,recovery?"/recovery/request":"/verification/send",recovery?new{username=username.Text.Trim(),email=email.Text.Trim()}:(object)new{username=username.Text.Trim(),email=email.Text.Trim(),purpose="register"},CancellationToken.None);});
        submit.Click+=async(_,_)=>await Execute(submit,async()=>{ValidateFields(true);return await Services.Post(manifest,recovery?"/recovery/complete":"/register",new{username=username.Text.Trim(),email=email.Text.Trim(),password=password.Text,verificationCode=code.Text.Trim()},CancellationToken.None);});
    }
    void ValidateFields(bool full){if(!Regex.IsMatch(username.Text.Trim(),"^[a-zA-Z0-9_]{1,16}$"))throw new InvalidOperationException("Use 1–16 letters, numbers or underscores for the account name.");if(!System.Net.Mail.MailAddress.TryCreate(email.Text.Trim(),out var parsed)||parsed.Address!=email.Text.Trim())throw new InvalidOperationException("Enter a valid email address.");if(full && (!Regex.IsMatch(password.Text,@"^[\x21-\x7E]{8,16}$")||string.IsNullOrWhiteSpace(code.Text)))throw new InvalidOperationException("Enter an 8–16 character password and the emailed verification code.");}
    async Task Execute(Button button,Func<Task<string>> action){button.Enabled=false;try{feedback.Text="Contacting account service…";feedback.Text=await action();}catch(Exception e){feedback.Text=e.Message;}finally{if(!IsDisposed)button.Enabled=true;}}
}

