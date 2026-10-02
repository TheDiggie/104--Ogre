using System;
using Godot;

/// <summary>
/// Tells the player when there is a newer build, and takes them to it.
///
/// THE PROBLEM. This client is sideloaded, so there is no store to
/// notice a new version and no notification when one exists. A player
/// finds out because someone mentions it, and then fetches an APK by
/// hand. That is the whole of "people do not want to uninstall and
/// reinstall".
///
/// WHAT THIS DOES AND DOES NOT FIX. An Android install is an UPGRADE,
/// keeping save data and settings, when three things hold: the package
/// name matches, the signature matches, and version/code is greater
/// than the installed one. All three are the build's business, not
/// this file's - and the third is why the version now lives in a
/// commit rather than in whoever's working tree built last. What this
/// adds is only the noticing: the client asks a small file on the
/// server what the newest build is, and if it is newer than the one
/// running, says so.
///
/// If a player is told "App not installed" when they try, that is a
/// SIGNATURE mismatch and nothing here will help - two builds were
/// signed with different debug keystores, which happens the moment a
/// second machine builds one. The fix for that is a keystore committed
/// alongside the project, not an updater.
///
/// HOW IT OPENS THE APK. `OS.ShellOpen` on the download URL, which
/// hands it to the browser, which hands the finished file to Android's
/// own package installer. The alternative - download it here and fire
/// the install intent - needs REQUEST_INSTALL_PACKAGES and a
/// FileProvider, because Android 7 and later refuse a file:// URI
/// (FileUriExposedException). One browser tap is cheaper than a
/// permission prompt on every install, and it works on every version.
///
/// It never blocks. A server that is down, a manifest that is
/// malformed, no network at all: the check fails quietly and the
/// client carries on. An updater that can stop you playing is worse
/// than no updater.
/// </summary>
public partial class Updater : Control
{
    /// <summary>
    /// Where the newest build is described. A small JSON file:
    ///
    ///     { "version": "0.8.1",
    ///       "url": "https://meridian59.us/mobile/Meridian59.apk",
    ///       "notes": "What changed, one line." }
    ///
    /// `version` is compared against application/config/version in
    /// project.godot, which is the only version string the game can
    /// read about itself at runtime - Android's version/code cannot be
    /// read from inside the app, which is why the comparison is on the
    /// name and why the two are kept in step.
    /// </summary>
    [Export] public string ManifestUrl = "https://meridian59.us/mobile/latest.json";

    /// <summary>
    /// What the check did, in one sentence, for the player to read.
    ///
    /// Every outcome of this check used to be the same thing on a
    /// phone: nothing. Up to date, a 404 because the file is a folder
    /// too deep, a manifest that will not parse, a server that never
    /// answers - four different problems, one symptom, and no log to
    /// look at. Diagnosing it meant unpacking the APK to find out which
    /// version was even running.
    ///
    /// A player does not need the detail, but they do need to know the
    /// check HAPPENED and what it concluded. One line costs nothing on
    /// a screen that is already showing the connection log, and it is
    /// the difference between "the updater is broken" and "the updater
    /// says it got a 404".
    /// </summary>
    public event Action<string> Said;

    HttpRequest _http;
    Panel _card;
    Label _title, _body;
    Button _later, _now;
    string _url = "", _newest = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        _http = new HttpRequest { UseThreads = true, Timeout = 8 };
        AddChild(_http);
        _http.RequestCompleted += Answered;

        _card = M59Skin.Window();
        AddChild(_card);
        _title = M59Skin.Title("A newer build is out");
        AddChild(_title);
        _body = M59Skin.Empty("");
        _body.HorizontalAlignment = HorizontalAlignment.Left;
        AddChild(_body);

        _later = new Button { Text = "Later", Name = "updateLater" };
        M59Skin.Dress(_later, M59Skin.Kind.Secondary);
        _later.Pressed += () => Show(false);
        AddChild(_later);

        _now = new Button { Text = "Get it", Name = "updateNow" };
        M59Skin.Dress(_now, M59Skin.Kind.Primary);
        _now.Pressed += () =>
        {
            if (_url.Length > 0) OS.ShellOpen(_url);
            Show(false);
        };
        AddChild(_now);

        GetViewport().SizeChanged += Layout;
        Show(false);
    }

    /// <summary>This build's version, as the project declares it.</summary>
    public static string Running
    {
        get
        {
            try { return ProjectSettings.GetSetting("application/config/version", "").AsString(); }
            catch { return ""; }
        }
    }

    bool _asked;

    /// <summary>
    /// Asks once per launch. Called from the login screen rather than
    /// from the world: an update prompt over a fight is an update
    /// prompt that gets dismissed without being read, and the moment a
    /// player will actually act on one is before they have started.
    /// </summary>
    public void Check()
    {
        if (_asked || _http == null) return;
        _asked = true;
        // M59UPDATE points the check somewhere else, which is the only
        // way a scripted run can see the prompt at all: the real
        // manifest describes the newest build, so against it a current
        // client is correctly silent.
        string where = System.Environment.GetEnvironmentVariable("M59UPDATE");
        if (string.IsNullOrWhiteSpace(where)) where = ManifestUrl;
        if (string.IsNullOrWhiteSpace(where)) return;
        Error e = _http.Request(where);
        if (e != Error.Ok)
        {
            GD.Print($"[Updater] no check: {e}");
            Say($"Update check could not start ({e}).");
        }
    }

    void Answered(long result, long code, string[] headers, byte[] body)
    {
        if (result != (long)HttpRequest.Result.Success || code != 200)
        {
            GD.Print($"[Updater] no answer (result {result}, http {code})");
            // The two that actually happen are worth saying apart. A 404
            // is the manifest not being where the client looks, which is
            // a five-second fix once somebody knows; anything else is
            // the request never completing - no network, a name that
            // does not resolve, TLS refused - which is a different
            // five-second fix.
            Say(code == 404
                ? "Update check: nothing at the update address (404)."
                : $"Update check failed to reach the server (result {result}, http {code}).");
            return;
        }

        string version, url, notes;
        try
        {
            // TrimStart on U+FEFF before parsing. A UTF-8 byte order
            // mark decodes to that character, Json.ParseString refuses
            // a document that starts with it, and the catch below then
            // swallows the whole check - so a manifest that is correct
            // in every editor offers nobody an update. Three ways in:
            // PowerShell 5.1's `Set-Content -Encoding UTF8` writes one
            // (which is why make-latest.ps1 uses WriteAllText with a
            // BOM-less encoding instead), Notepad used to, and so does
            // anything that round-trips the file through Excel. The
            // generator is fixed; this is the end that cannot be, since
            // the manifest is a file on a server somebody may edit by
            // hand at three in the morning.
            var json = Json.ParseString(
                System.Text.Encoding.UTF8.GetString(body).TrimStart('\uFEFF'));
            if (json.VariantType != Variant.Type.Dictionary) return;
            var d = json.AsGodotDictionary();
            version = d.TryGetValue("version", out Variant v) ? v.AsString() : "";
            url = d.TryGetValue("url", out Variant u) ? u.AsString() : "";
            notes = d.TryGetValue("notes", out Variant n) ? n.AsString() : "";
        }
        catch (Exception ex)
        {
            GD.Print($"[Updater] bad manifest: {ex.Message}");
            Say("Update check: the update file could not be read.");
            return;
        }

        // Only https, and only a link. The manifest is a file on a
        // server this client was pointed at, but it is still content
        // from the network deciding what the player is sent to, so it
        // does not get to name an arbitrary scheme.
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Say("Update check: the update file does not name an https download.");
            return;
        }
        if (Newer(version, Running) <= 0)
        {
            // SAID, not silent. "You are on the newest build" and "the
            // check never ran" are the two answers a player most needs
            // to tell apart, and they looked the same.
            Say($"You have the newest build ({Running}).");
            return;
        }

        _newest = version;
        _url = url;
        _body.Text = notes.Length > 0
            ? $"You have {Running}. {version} is out:\n{notes}"
            : $"You have {Running}. {version} is out.";
        Say($"A newer build is out: {version}.");
        Show(true);
        Layout();
    }

    void Say(string line)
    {
        try { Said?.Invoke(line); } catch { }
    }

    /// <summary>
    /// Compares two dotted version strings. Positive when the first is
    /// newer. Parts that are not numbers compare as zero, so a build
    /// named "0.8.1-test" is not newer than "0.8.1" and will not nag.
    /// </summary>
    internal static int Newer(string a, string b)
    {
        string[] x = (a ?? "").Split('.'), y = (b ?? "").Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int.TryParse(i < x.Length ? x[i] : "0", out int xi);
            int.TryParse(i < y.Length ? y[i] : "0", out int yi);
            if (xi != yi) return xi > yi ? 1 : -1;
        }
        return 0;
    }

    void Show(bool on)
    {
        Visible = on;
        MouseFilter = on ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _card.Visible = on; _title.Visible = on; _body.Visible = on;
        _later.Visible = on; _now.Visible = on;
        if (on) Panels.ToFront(this);
    }

    void Layout()
    {
        if (!Visible || _card == null) return;
        Vector2 v = GetViewportRect().Size;
        Position = Vector2.Zero;
        Size = v;

        Rect2 card = M59Skin.Frame(v, 96f, true, M59Skin.ListW);
        Rect2 body = M59Skin.Body(card);
        Rect2 foot = M59Skin.Foot(card);

        _card.Position = card.Position;
        _card.Size = card.Size;
        _title.Position = new Vector2(card.Position.X + M59Skin.Pad, card.Position.Y);
        _title.Size = new Vector2(card.Size.X - M59Skin.Pad * 2f, M59Skin.TitleH);
        _body.Position = body.Position;
        _body.Size = body.Size;
        M59Skin.FootRow(foot, _now, _later);
    }
}
