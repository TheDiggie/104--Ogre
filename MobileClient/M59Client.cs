using System;
using System.Collections.Generic;
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
    /// <summary>
    /// The client version reported at login. If the server wants a
    /// different one it answers with GetClient and asks us to patch, so
    /// this is settable rather than compiled in - guessing wrong should
    /// cost a config change, not a rebuild.
    /// </summary>
    public byte VersionMajor { get; set; } = 5;
    public byte VersionMinor { get; set; } = 0;

    public override byte AppVersionMajor => VersionMajor;
    public override byte AppVersionMinor => VersionMinor;

    /// <summary>Character to use. Empty means the first non-empty slot.</summary>
    public string PreferredCharacter { get; set; } = "";

    /// <summary>Raised for anything the user should see: errors, server notices.</summary>
    public event Action<string> Notice;

    /// <summary>Raised once a character has been sent and play begins.</summary>
    public event Action<string> EnteredGame;

    /// <summary>
    /// Raised when the account has several characters and nothing said
    /// which one to use. Nothing is sent until <see cref="UseCharacter"/>
    /// is called with one of them, so the UI has as long as it needs.
    /// </summary>
    public event Action<IList<CharSelectItem>> ChooseCharacter;

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

        if (pick == null)
        {
            var real = chars.Where(c => !c.IsEmptySlot).ToList();
            if (real.Count == 0) { Say("All character slots are empty."); return; }

            // One character is not a choice; several is, and picking the
            // first silently would log you in as the wrong one.
            if (real.Count > 1 && ChooseCharacter != null)
            {
                Say($"{real.Count} characters on this account.");
                ChooseCharacter(real);
                return;
            }
            pick = real[0];
        }

        UseCharacter(pick);
    }

    /// <summary>Enters the world as this character.</summary>
    public void UseCharacter(CharSelectItem pick)
    {
        if (pick == null) return;
        Say($"Entering the world as {pick.Name}...");
        SendUseCharacterMessage(pick, true, pick.Name);
        EnteredGame?.Invoke(pick.Name);
    }

    protected override void HandleGetClientMessage(GetClientMessage Message)
    {
        // The server wants a different client build than we claim to be.
        Say($"Server refused version {VersionMajor}.{VersionMinor} and wants a patch. " +
            "Either log in once with the classic client to update, or set " +
            "Version Major / Version Minor on the node to match what the " +
            "server expects.");
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
