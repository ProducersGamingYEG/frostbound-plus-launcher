# Frostbound Plus account service

ASP.NET 9 service for CMaNGOS Classic (client 5875, level cap 60). Never point this service at Wrath databases. All database names are explicitly Classic. Register and recovery routes match the launcher contract; send-code must include the intended username. Successful operations return a `message` and registration/reset also return `success: true`.

## Private deployment configuration

Use environment.example as a setting-name reference; keep actual environment files and TLS certificates outside this repository. Configure a private, least-privileged MySQL identity, independent random CODE_HMAC_KEY, and the existing Gmail sender's SMTP credentials. Gmail uses STARTTLS on port 587 with normal system certificate validation. Mount the HTTPS PFX read-only and supply its password privately. Do not disable certificate validation. HTTPS is required for deployment. Restrict SQL access to the service host. The server does not install certificates or alter the database.

Review migration.sql, back up account data, and apply manually before startup. CMaNGOS ships account/realmcharacters as MyISAM; this service requires InnoDB so code use and account changes commit together. Startup checks this requirement and fails closed. No migrations run automatically. The sample grants are comments, not executable identity creation. Status identifies bots by the standard RNDBOT username prefix; adapt deliberately if the deployment uses another bot account convention.

Only incoming user signup/recovery requests enqueue mail. A bounded in-memory queue returns the same response before account lookup/SMTP, reducing recovery timing enumeration; queued work can be lost on restart and users may request a new code after 60 seconds. No startup/test mail is sent. Emails contain six-digit codes, expire after ten minutes, and allow five attempts. Per-IP routes permit twelve requests per fifteen minutes; durable per-email/purpose counters permit five sends per day and at least sixty seconds between sends. Do not trust forwarded IP headers from arbitrary clients. If behind a proxy, its shared socket IP conservatively receives the limit until trusted-proxy support is deliberately configured. Codes are HMAC-bound to purpose, email and username, and consumed transactionally. Privileged accounts cannot use self-service recovery. Recovery invalidates the saved session key; already-connected game clients are not forcibly disconnected.

No request/exception logging providers are enabled. Arrange only aggregate process-health monitoring at deployment; never enable request-body/SQL-parameter logging. Periodically remove verification records whose window_start is older than two days via a separately privileged maintenance identity to bound retained email data (the service does not require DELETE permission).

## Verification

Run `dotnet run --project tests/Frostbound.Tests.csproj -c Release -m:2` after review/commit. These pure tests make no database connections and send no mail. The verifier vector was independently calculated from CMaNGOS SRP6 SHA1, integer endianness and modular exponentiation using Python.

For integration validation, use only a disposable Classic schema clone and an injected ICodeMailer fake capturing codes in memory. Do not use the deployed account database or Gmail. Validate these scenarios before exposing publicly:

- Apply migration twice; startup rejects missing/nontransactional tables.
- Send code with fake mailer, register, and verify account gmlevel=0, expansion=0, exact SRP6 fields, email and all realmcharacters rows.
- Reject wrong username/email/purpose/code, expiry and sixth guess; concurrent submissions consume a code exactly once.
- Verify six sends/day and repeat within sixty seconds are suppressed across two AccountService instances; cooldown counters survive use of a valid code.
- Duplicate username creation leaves existing account unchanged. Force realmcharacters insertion failure and verify account/code consumption roll back.
- Recovery for unknown, wrong-email and privileged accounts returns the same HTTP response, and no fake mail is sent. Valid recovery changes v/s and clears sessionkey without changing account flags or privileges.
- Fake SMTP failure expires the code; no exception or personal data is returned or logged. Queue capacity exhaustion also keeps the generic response.
- World port closed gives online=false; SQL/count failure gives online=false; a reachable world with populated online Classic character fixtures gives expected human/bot counts.
- Inspect HTTPS certificate chain and endpoint host using the launcher; never turn off certificate validation.

The .NET 9 runtime is retained to match the authorized project stack; refresh the base runtime before long-term public hosting as part of deployment maintenance.

### Executable disposable integration suite

The integration suite is implemented in tests/Integration.cs and ran successfully against an isolated MariaDB 10.11 container. It creates Classic-named fixture databases from scratch and refuses existing schemas by using CREATE DATABASE without IF NOT EXISTS. It uses a hardcoded **test-only** password and loopback address; never configure it against an existing database. Start an isolated container with an ephemeral loopback port and the password `disposable-test-only`, set `FROSTBOUND_DISPOSABLE_TEST_PORT` only for the test process, then run the same test command. Remove that test container and its anonymous volume afterward. With no test-port environment variable, only the pure suite runs. The fake transport never opens an SMTP connection.

Validated: 14 pure assertions and 22 integration assertions, including SQL rollback, concurrent one-time recovery, expiry/attempt limits, cooldown, no privileged creation, bot counts and fake SMTP failure. HTTP/TLS deployment checks still belong to the runtime owner.
