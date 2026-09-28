# Meridian59.Net8Verify

Smoke test for the core library running on .NET 8 (non-Windows).

    dotnet run --project Tools/Meridian59.Net8Verify [path-to-resource-dir]

Checks two things the Ogre client never exercised off Windows:

1. **Assets** — parses every `.bgf` and `.roo` under the resource
   directory and reports failures. Skipped if no path is given.
2. **Protocol** — serializes a real `LoginMessage`, verifies the
   MeridianMD5 password hash survives the wire format, then feeds
   signed server->client login messages back through
   `MessageControllerClient`, including one delivered in two TCP
   chunks to exercise the parser's reassembly buffer.

Exits non-zero if anything fails.
