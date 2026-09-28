# Meridian59.Net8Play

Headless login and world probe. Connects with the same `M59Client` the
Godot view uses, runs the handshake, and prints what the server sends:
room changes, object counts, the avatar's position.

    set M59USER=yourname
    set M59PASS=yourpassword
    dotnet run --project Tools/Meridian59.Net8Play -- "%LOCALAPPDATA%\Meridian-104\resource"

Credentials come from the environment, never arguments - arguments end
up in shell history.

This separates the networking from the rendering. If it reaches "in room
N" with a sane object count, the client half works and anything wrong is
in the view.

It also found a portability bug worth knowing about: `ServerConnection`
created an IPv6 dual-stack socket unconditionally, which throws
"Address family not supported by protocol" on any IPv4-only host before
a connection is even attempted. That is now a fallback, not a hard
requirement.
