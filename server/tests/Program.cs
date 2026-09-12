using System.Security.Cryptography;
static void Check(bool condition,string name) { if (!condition) throw new Exception("FAILED: "+name); Console.WriteLine("PASS: "+name); }
static void Reject(Action action,string name) { try { action(); } catch (ArgumentException) { Console.WriteLine("PASS: "+name); return; } throw new Exception("FAILED: "+name); }
Check(Credentials.User("Explorer_1")=="EXPLORER_1","username canonicalization");
Reject(()=>Credentials.User("name';--"),"reject SQL punctuation");
Reject(()=>Credentials.User(new string('A',17)),"classic username maximum");
Reject(()=>Credentials.Password("short"),"password minimum");
Reject(()=>Credentials.Password("hello world"),"password space rejected");
Reject(()=>Credentials.Password("Passwordé"),"non-ASCII password rejected");
Check(Credentials.Password("TestPass123")=="TESTPASS123","WoW password case convention");
Reject(()=>Credentials.Email("Name <test@example.com>"),"display-name mailbox rejected");
Check(Credentials.Email(" Test@Example.com ")=="test@example.com","email canonicalization");
var salt=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();
var actual=Credentials.Verifier("Explorer","TestPass123",salt);
// Independently calculated with Python hashlib SHA1 and modular pow against CMaNGOS constants.
Check(actual.Salt=="9F1E1D1C1B1A191817161514131211100F0E0D0C0B0A09080706050403020100","salt SQL hex endianness");
Check(actual.Verifier=="797968BFEC8E3F13F77E9244ACD0A2A3435FE10290AEC23496E068FFDDE416F2","Classic SRP6 verifier vector");
var hash=Credentials.CodeHash("test-secret", "register","EXPLORER","e@example.com","123456");
Check(!CryptographicOperations.FixedTimeEquals(hash,Credentials.CodeHash("test-secret","recover","EXPLORER","e@example.com","123456")),"code purpose binding");
Check(!CryptographicOperations.FixedTimeEquals(hash,Credentials.CodeHash("test-secret","register","OTHER","e@example.com","123456")),"code username binding");
Check(!CryptographicOperations.FixedTimeEquals(hash,Credentials.CodeHash("test-secret","register","EXPLORER","other@example.com","123456")),"code email binding");
Console.WriteLine("All pure tests passed. No database connections or mail transports used.");
