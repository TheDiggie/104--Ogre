# Meridian59.Net8Live

Live protocol probe for the .NET 8 port. Opens a real socket and runs the
server's actual bytes through the ported `MessageControllerClient`.

    dotnet run --project Tools/Meridian59.Net8Live -- 3.141.65.36 5959

Prints each message the server sends, decoded. It exits non-zero if nothing
parsed. A `LENGTH MISMATCH` line means the framing disagrees with the server
and is the thing to look at first.

Logging in is optional and off by default - just connecting is enough to
prove the socket, framing and parser work end to end. To go further, set
both environment variables first (do not pass them as arguments, they end
up in shell history):

    set M59USER=yourname
    set M59PASS=yourpassword
    dotnet run --project Tools/Meridian59.Net8Live -- 3.141.65.36 5959
