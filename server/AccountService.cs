using MySqlConnector;
using System.Security.Cryptography;
public sealed class AccountService(IConfiguration config, ICodeMailer mailer)
{
    string Secret => config["CODE_HMAC_KEY"]!;
    async Task<MySqlConnection> Open()
    {
        var c = new MySqlConnection(config["AUTH_DATABASE"]);
        await c.OpenAsync(); return c;
    }
    static MySqlCommand Command(MySqlConnection c, MySqlTransaction? t, string sql, params (string, object)[] values)
    {
        var cmd = new MySqlCommand(sql, c, t);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    public async Task ValidateStorage()
    {
        await using var c = await Open();
        await using var cmd = Command(c, null, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='classicrealmd' AND table_name IN ('account','realmcharacters','frostbound_verification') AND engine='InnoDB'");
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 3) throw new InvalidOperationException("Required transactional schema migration is missing.");
    }
    public async Task Send(string username, string email, string purpose)
    {
        await using var c = await Open();
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose + "\n" + email));
        // Cross-instance cooldown and per-email daily cap. Codes are bound to the exact username.
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var hash = Credentials.CodeHash(Secret, purpose, username, email, code);
        await using (var tx = await c.BeginTransactionAsync())
        {
            await using var seed = Command(c, tx, "INSERT IGNORE INTO classicrealmd.frostbound_verification (id,username,email,purpose,code_hash,expires_at,sent_at,window_start,sends,attempts) VALUES (@id,@u,@e,@p,@h,UTC_TIMESTAMP(),DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 DAY),UTC_TIMESTAMP(),0,0)", ("@id",key),("@u",username),("@e",email),("@p",purpose),("@h",hash));
            await seed.ExecuteNonQueryAsync();
            await using var update = Command(c, tx, "UPDATE classicrealmd.frostbound_verification SET username=@u,code_hash=@h,expires_at=DATE_ADD(UTC_TIMESTAMP(),INTERVAL 10 MINUTE),sent_at=UTC_TIMESTAMP(),attempts=0,sends=IF(window_start<DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 DAY),1,sends+1),window_start=IF(window_start<DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 DAY),UTC_TIMESTAMP(),window_start) WHERE id=@id AND sent_at<=DATE_SUB(UTC_TIMESTAMP(),INTERVAL 60 SECOND) AND (sends<5 OR window_start<DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 DAY))", ("@u",username),("@h",hash),("@id",key));
            if (await update.ExecuteNonQueryAsync() != 1) { await tx.RollbackAsync(); return; }
            await tx.CommitAsync();
        }
        if (purpose == "recover")
        {
            await using var match = Command(c, null, "SELECT COUNT(*) FROM classicrealmd.account WHERE username=@u AND LOWER(email)=@e AND gmlevel=0", ("@u",username),("@e",email));
            if (Convert.ToInt32(await match.ExecuteScalarAsync()) != 1) return;
        }
        try { await mailer.Send(email, code, purpose == "recover" ? "password recovery" : "registration"); }
        catch
        {
            await using var invalidate = Command(c, null, "UPDATE classicrealmd.frostbound_verification SET expires_at=UTC_TIMESTAMP() WHERE id=@id AND code_hash=@h", ("@id",key),("@h",hash));
            await invalidate.ExecuteNonQueryAsync();
            // Do not reveal account existence through SMTP failure responses.
        }
    }
    public async Task<bool> Complete(string username, string email, string password, string code, string purpose)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return false;
        var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose + "\n" + email));
        await using var c = await Open();
        await using var tx = await c.BeginTransactionAsync();
        byte[] expected;
        await using (var read = Command(c, tx, "SELECT code_hash FROM classicrealmd.frostbound_verification WHERE id=@id AND username=@u AND expires_at>UTC_TIMESTAMP() AND attempts<5 FOR UPDATE", ("@id",key),("@u",username)))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return false;
            expected = (byte[])reader[0];
        }
        await using var attempt = Command(c, tx, "UPDATE classicrealmd.frostbound_verification SET attempts=attempts+1 WHERE id=@id", ("@id",key));
        await attempt.ExecuteNonQueryAsync();
        if (!CryptographicOperations.FixedTimeEquals(expected, Credentials.CodeHash(Secret,purpose,username,email,code))) { await tx.CommitAsync(); return false; }
        var (salt, verifier) = Credentials.Verifier(username,password);
        if (purpose == "register")
        {
            await using var insert = Command(c, tx, "INSERT IGNORE INTO classicrealmd.account (username,email,v,s,joindate,expansion,gmlevel) VALUES (@u,@e,@v,@s,UTC_TIMESTAMP(),0,0)", ("@u",username),("@e",email),("@v",verifier),("@s",salt));
            if (await insert.ExecuteNonQueryAsync() != 1) { await tx.CommitAsync(); return false; }
            var id = insert.LastInsertedId;
            await using var realms = Command(c, tx, "INSERT INTO classicrealmd.realmcharacters (realmid,acctid,numchars) SELECT id,@id,0 FROM classicrealmd.realmlist", ("@id",id));
            await realms.ExecuteNonQueryAsync();
        }
        else
        {
            await using var reset = Command(c, tx, "UPDATE classicrealmd.account SET v=@v,s=@s,sessionkey='' WHERE username=@u AND LOWER(email)=@e AND gmlevel=0", ("@u",username),("@e",email),("@v",verifier),("@s",salt));
            if (await reset.ExecuteNonQueryAsync() != 1) { await tx.CommitAsync(); return false; }
        }
        // Retain cooldown counters after one-time consumption.
        await using var consume = Command(c, tx, "UPDATE classicrealmd.frostbound_verification SET expires_at=UTC_TIMESTAMP(),attempts=5 WHERE id=@id", ("@id",key));
        await consume.ExecuteNonQueryAsync();
        await tx.CommitAsync(); return true;
    }
    public async Task<(int players, int bots)> Counts()
    {
        await using var c = await Open();
        await using var cmd = Command(c,null,"SELECT COALESCE(SUM(a.username NOT LIKE 'RNDBOT%'),0),COALESCE(SUM(a.username LIKE 'RNDBOT%'),0) FROM classiccharacters.characters ch JOIN classicrealmd.account a ON a.id=ch.account WHERE ch.online=1");
        await using var r = await cmd.ExecuteReaderAsync(); await r.ReadAsync();
        return (r.GetInt32(0),r.GetInt32(1));
    }
}
