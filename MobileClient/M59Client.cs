using System;
using System.Linq;
using Meridian59.Client;
using Meridian59.Common;
using Meridian59.Data;
using Meridian59.Data.Models;
using Meridian59.Files;
using Meridian59.Protocol.GameMessages;

/// <summary>
/// Concrete game client.
///
/// BaseClient already implements the connection, the message dispatch and
/// the whole data layer - room, objects, inventory, the avatar - so this
/// only has to name the version it reports and drive the login handshake,
/// which is abstract because a real client would put a UI in the middle of
/// it. Here it runs automatically from the configured connection.
///
/// The sequence, once connected:
///   server -&gt; GetLogin        we reply with the username and password
///   server -&gt; LoginOK         accepted
///   server -&gt; Characters      we pick one and send UseCharacter
///   then the server starts sending room and object messages.
/// </summary>
public class M59Client : BaseClient<GameTick, ResourceManager, DataController, Config>
{
    public override byte AppVersionMajor => 5;
    public override byte AppVersionMinor => 0;

    /// <summary>Character to use. Empty means the first non-empty slot.</summary>
    public string PreferredCharacter { get; set; } = "";

    /// <summary>Raised for anything the user should see: errors, server notices.</summary>
    public event Action<string> Notice;

    /// <summary>Raised once a character has been sent and play begins.</summary>
    public event Action<string> EnteredGame;

    void Say(string s) => Notice?.Invoke(s);

    protected override void HandleGetLoginMessage(GetLoginMessage Message)
    {
        ConnectionInfo info = Config.SelectedConnectionInfo;
        if (info == null) { Say("No connection selected."); return; }
        if (string.IsNullOrEmpty(info.Username)) { Say("No username configured."); return; }
        Say($"Logging in as {info.Username}...");
        SendLoginMessage(info.Username, info.Password);
    }

    protected override void HandleLoginOKMessage(LoginOKMessage Message)
    {
        Say("Login accepted.");
    }

    protected override void HandleCharactersMessage(CharactersMessage Message)
    {
        var chars = Message.WelcomeInfo?.Characters;
        if (chars == null || chars.Count == 0) { Say("No characters on this account."); return; }

        CharSelectItem pick = null;
        if (!string.IsNullOrWhiteSpace(PreferredCharacter))
            pick = chars.FirstOrDefault(c =>
                !c.IsEmptySlot &&
                string.Equals(c.Name, PreferredCharacter, StringComparison.OrdinalIgnoreCase));

        pick ??= chars.FirstOrDefault(c => !c.IsEmptySlot);
        if (pick == null) { Say("All character slots are empty."); return; }

        Say($"Entering the world as {pick.Name}...");
        SendUseCharacterMessage(pick, true, pick.Name);
        EnteredGame?.Invoke(pick.Name);
    }

    protected override void HandleGetClientMessage(GetClientMessage Message)
    {
        // The server wants a different client build than we claim to be.
        Say("Server reports a client version mismatch - it wants a patch. " +
            "Log in with the classic client once to update, or adjust " +
            "AppVersionMajor/Minor.");
    }

    protected override void HandleLoginModeMessageMessage(LoginModeMessageMessage Message)
    {
        Say(Message.Message);
    }

    protected override void OnServerConnectionException(Exception Error)
    {
        Say($"Connection error: {Error.GetType().Name}: {Error.Message}");
    }
}
