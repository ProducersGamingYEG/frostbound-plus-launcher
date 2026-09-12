using Microsoft.Extensions.Configuration;
using MySqlConnector;
public static class Integration
{
    public static async Task Run(string port)
    {
        var connection=$"Server=127.0.0.1;Port={int.Parse(port)};User ID=root;Password=disposable-test-only;SslMode=None";
        await using var db=new MySqlConnection(connection); await db.OpenAsync();
        async Task Sql(string sql) { await using var cmd=new MySqlCommand(sql,db); await cmd.ExecuteNonQueryAsync(); }
        async Task<object?> Scalar(string sql) { await using var cmd=new MySqlCommand(sql,db); return await cmd.ExecuteScalarAsync(); }
        static void Assert(bool ok,string name) { if(!ok) throw new Exception("FAILED: "+name); Console.WriteLine("PASS: "+name); }
        // CREATE fails if fixtures already exist; never drop/overwrite an existing schema.
        await Sql("CREATE DATABASE classicrealmd; CREATE DATABASE classiccharacters;");
        await Sql("CREATE TABLE classicrealmd.account (id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,username VARCHAR(32) UNIQUE,email TEXT,v TEXT,s TEXT,joindate DATETIME,expansion INT,gmlevel INT DEFAULT 0,sessionkey TEXT) ENGINE=InnoDB; CREATE TABLE classicrealmd.realmlist (id INT PRIMARY KEY) ENGINE=InnoDB; INSERT INTO classicrealmd.realmlist VALUES(1),(2); CREATE TABLE classicrealmd.realmcharacters (realmid INT,acctid INT,numchars INT,PRIMARY KEY(realmid,acctid)) ENGINE=InnoDB; CREATE TABLE classiccharacters.characters (account INT,online INT) ENGINE=InnoDB;");
        await Sql(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"migration.sql")));
        var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["AUTH_DATABASE"]=connection,["CODE_HMAC_KEY"]="disposable-integration-hmac-test-key-only" }).Build();
        var fake=new FakeMailer(); var svc=new AccountService(cfg,fake); await svc.ValidateStorage();
        await svc.Send("EXPLORER","e@example.com","register"); var code=fake.Code; Assert(fake.Calls==1,"fake transport capture");
        await svc.Send("EXPLORER","e@example.com","register"); Assert(fake.Calls==1,"durable cooldown suppresses resend");
        Assert(!await svc.Complete("EXPLORER","e@example.com","TestPass123","000000","register"),"wrong code rejected");
        Assert(!await svc.Complete("OTHER","e@example.com","TestPass123",code,"register"),"code username mismatch");
        Assert(await svc.Complete("EXPLORER","e@example.com","TestPass123",code,"register"),"registration succeeds");
        Assert(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM classicrealmd.realmcharacters"))==2,"all realm links created");
        Assert(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM classicrealmd.account WHERE username='EXPLORER' AND gmlevel=0 AND expansion=0"))==1,"no privileges or expansion granted");
        Assert(!await svc.Complete("EXPLORER","e@example.com","TestPass123",code,"register"),"code single use");
        await svc.Send("MISSING","none@example.com","recover"); Assert(fake.Calls==1,"unknown recovery sends no mail");
        await Sql("UPDATE classicrealmd.account SET sessionkey='OLD_SESSION'");
        await svc.Send("EXPLORER","e@example.com","recover"); code=fake.Code;
        var results=await Task.WhenAll(svc.Complete("EXPLORER","e@example.com","NewPass123",code,"recover"),new AccountService(cfg,fake).Complete("EXPLORER","e@example.com","NewPass123",code,"recover"));
        Assert(results.Count(x=>x)==1,"concurrent recovery consumes exactly once");
        Assert((string?)await Scalar("SELECT sessionkey FROM classicrealmd.account WHERE username='EXPLORER'")=="","recovery clears session key");
        await svc.Send("ATTEMPTS","attempts@example.com","register"); code=fake.Code;
        for(var i=0;i<5;i++) Assert(!await svc.Complete("ATTEMPTS","attempts@example.com","TestPass123","000000","register"),"incorrect attempt "+(i+1));
        Assert(!await svc.Complete("ATTEMPTS","attempts@example.com","TestPass123",code,"register"),"sixth attempt denied even with correct code");
        await svc.Send("EXPIRED","expired@example.com","register"); code=fake.Code;
        await Sql("UPDATE classicrealmd.frostbound_verification SET expires_at=DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 SECOND) WHERE username='EXPIRED'");
        Assert(!await svc.Complete("EXPIRED","expired@example.com","TestPass123",code,"register"),"expired code rejected");
        fake.Fail=true; await svc.Send("FAILMAIL","fail@example.com","register");
        Assert(!await svc.Complete("FAILMAIL","fail@example.com","TestPass123",fake.Code,"register"),"failed transport invalidates code"); fake.Fail=false;
        await svc.Send("ROLLBACK","rollback@example.com","register"); code=fake.Code;
        await Sql("CREATE TRIGGER classicrealmd.fail_links BEFORE INSERT ON classicrealmd.realmcharacters FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='fixture failure'");
        try { await svc.Complete("ROLLBACK","rollback@example.com","TestPass123",code,"register"); throw new Exception("Expected SQL failure"); } catch(MySqlException) { }
        Assert(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM classicrealmd.account WHERE username='ROLLBACK'"))==0,"account insert rolls back when realm link fails");
        await Sql("DROP TRIGGER classicrealmd.fail_links");
        Assert(await svc.Complete("ROLLBACK","rollback@example.com","TestPass123",code,"register"),"rolled-back code remains usable");
        await Sql("INSERT INTO classiccharacters.characters SELECT id,1 FROM classicrealmd.account; INSERT INTO classicrealmd.account(username,email,gmlevel,expansion) VALUES('RNDBOT1','',0,0); INSERT INTO classiccharacters.characters SELECT id,1 FROM classicrealmd.account WHERE username='RNDBOT1'");
        var counts=await svc.Counts(); Assert(counts.players==2 && counts.bots==1,"Classic player and bot counts");
        Console.WriteLine("Integration passed against disposable fixtures only; fake transport sent no emails.");
    }
    sealed class FakeMailer : ICodeMailer
    {
        public int Calls; public string Code=""; public bool Fail;
        public Task Send(string email,string code,string purpose) { Calls++; Code=code; if(Fail) throw new Exception("Fake failure"); return Task.CompletedTask; }
    }
}
