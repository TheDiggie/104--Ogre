using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Files.ROO;

/// <summary>
/// The sounds the server asks for.
///
/// Nothing in the library plays anything - it only notices when a sound
/// is an "ouch" and sets a health status from it - because playing is
/// the engine's job. The Ogre client's `ControllerSound` hooks the
/// message stream for the same reason, and this follows what it does:
///
///  - a sound with a source object plays at that object
///  - otherwise, one with a row and column plays at the middle of that
///    grid square: `(column - 1) * 1024 + 512`, and the same for the row,
///    with the height taken from the room
///  - otherwise it plays at you
///  - the loop flag loops it, and a StopWave stops that loop
///
/// The game mixes these in 3D with a maximum distance of 2000 server
/// units. There is no 3D scene here - the world is a raycaster drawing
/// into a texture - so this approximates it: volume falls off with
/// distance to that same limit, and the sound is panned by where the
/// source sits across the screen, which the renderer can work out
/// because it already projects points for the name labels.
///
/// Sounds are .ogg on disk despite the library calling them wavs, and
/// Godot can load one at runtime without an import step.
/// </summary>
public partial class M59Sound : Node
{
    /// <summary>
    /// 0 to 1, as the game's own slider is 0 to 10.
    ///
    /// Setting it reaches what is already playing, as the reference's
    /// AdjustSoundVolume does (`ControllerSound.cpp:198-227`, called
    /// from the options panel at UIOptions.cpp:2274). Before, moving
    /// the slider did nothing at all until the next sound started.
    /// </summary>
    [Export] public float Volume
    {
        get => _volume;
        set { _volume = value; Reheard(); }
    }
    float _volume = 0.7f;
    /// <summary>
    /// Music has its own level, as it does in the game - two sliders,
    /// not one - and a room's ambience is not the same nuisance as a
    /// fountain three doors away.
    /// </summary>
    [Export] public float MusicLevel
    {
        get => _musicLevel;
        set
        {
            _musicLevel = value;
            if (_music != null && _music.Playing)
                _music.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(_musicLevel, 0.0001f, 1f));
        }
    }
    float _musicLevel = 0.5f;
    /// <summary>
    /// Off silences the looping sounds only, which is what
    /// Config->DisableLoopSounds does: a fountain stops, a sword does
    /// not.
    /// </summary>
    [Export] public bool Loops = true;
    /// <summary>
    /// Server units past which a sound stops getting quieter. The
    /// game's own `setDefault3DSoundMaxDistance(2000.0f)`
    /// (ControllerSound.cpp:45).
    /// </summary>
    [Export] public float MaxDistance = 2000f;

    /// <summary>
    /// The fall-off, `setRolloffFactor(0.002f)` (ControllerSound.cpp:47):
    /// a sound is heard at 1/(1 + 0.002 * distance) of its level.
    /// </summary>
    [Export] public float Rolloff = 0.002f;
    /// <summary>How many one-shot sounds may overlap.</summary>
    [Export] public int Voices = 12;
    /// <summary>Prints what it plays, for the test harnesses.</summary>
    [Export] public bool Verbose = false;

    readonly Dictionary<string, AudioStream> _streams = new Dictionary<string, AudioStream>();
    readonly List<AudioStreamPlayer2D> _pool = new List<AudioStreamPlayer2D>();
    readonly Dictionary<string, AudioStreamPlayer2D> _loops = new Dictionary<string, AudioStreamPlayer2D>();
    AudioStreamPlayer _music;
    string _musicPlaying = "";

    /// <summary>
    /// What a 2D sound is heard from. Without one, Godot listens from
    /// the middle of the viewport in world coordinates - and this node
    /// sits at the world origin, which is the top-left CORNER of the
    /// screen. Every sound in the game was therefore panned hard left
    /// and, because AudioStreamPlayer2D also applies its own distance
    /// fall-off from that point, quieter than the gain worked out for
    /// it. One listener at the origin puts the ear where the
    /// arithmetic already assumed it was.
    /// </summary>
    AudioListener2D _ear;

    public override void _Ready()
    {
        _music = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_music);

        _ear = new AudioListener2D();
        AddChild(_ear);
        _ear.MakeCurrent();
    }

    /// <summary>
    /// Gives every voice a chance to hear a volume change. Only the
    /// loops need their position kept - a one-shot is over before a
    /// slider can move - so the pan is left alone and only the level
    /// is re-applied, from the gain each voice was started with.
    /// </summary>
    void Reheard()
    {
        foreach (var kv in _loops)
            if (kv.Value != null && kv.Value.Playing && _gain.TryGetValue(kv.Value, out float g))
                kv.Value.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, g * _volume));
        foreach (var p in _pool)
            if (p.Playing && _gain.TryGetValue(p, out float g))
                p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, g * _volume));
    }

    /// <summary>Each voice's gain before the master level, so a slider can be re-applied.</summary>
    readonly Dictionary<AudioStreamPlayer2D, float> _gain =
        new Dictionary<AudioStreamPlayer2D, float>();

    /// <summary>
    /// Plays one. <paramref name="listener"/> is where you are and
    /// <paramref name="facing"/> which way, both in the server's units,
    /// so the fall-off and the panning have something to measure from.
    /// </summary>
    public void Play(PlaySound info, RooFile room, float listenerX, float listenerY, float facing)
    {
        if (info == null || string.IsNullOrEmpty(info.Resource)) return;

        AudioStream stream = Load(info.Resource);
        if (stream == null) return;

        if (!Where(info, room, listenerX, listenerY, out float sx, out float sy))
        { sx = listenerX; sy = listenerY; }

        Emit(info, stream, sx, sy, listenerX, listenerY, facing);
    }

    void Emit(PlaySound info, AudioStream stream, float sx, float sy,
              float listenerX, float listenerY, float facing)
    {
        float dx = sx - listenerX, dy = sy - listenerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        // The game's own fall-off, which is inverse-distance rather than
        // linear: `setRolloffFactor(0.002f)` with a maximum distance of
        // 2000 and a minimum of 0 (`ControllerSound.cpp:45-47`). Past the
        // maximum the sound does not vanish, it stops getting quieter -
        // so a distant bell is faint and still there, where a linear
        // ramp cut it off dead. A linear ramp was also much too loud in
        // the middle: at 500 units it gave 0.75 of full where the game
        // gives 0.50.
        float d = MathF.Min(distance, MaxDistance);
        float shape = 1f / (1f + Rolloff * d);
        float gain = Volume * shape;
        if (gain <= 0.001f) return;

        // Panned by which side of you it is on: the component of the
        // direction across your facing, -1 hard left to +1 hard right.
        float pan = 0f;
        if (distance > 1f)
        {
            float c = MathF.Cos(facing), s = MathF.Sin(facing);
            pan = Mathf.Clamp((dx * s - dy * c) / distance, -1f, 1f);
        }

        bool loop = info.PlayFlags != null && info.PlayFlags.IsLoop;
        // Asked for before the player is made, not after: creating the
        // loop player first left an idle node behind for every distinct
        // ambient while looping sounds were switched off.
        if (loop && !Loops) return;
        AudioStreamPlayer2D player = loop ? Loop(Key(info.ResourceName)) : Idle();
        if (player == null) return;

        // Looping is a property of the stream in Godot, not of the player,
        // and the streams are shared between callers - so a looping sound
        // gets its own copy rather than making every later one-shot of the
        // same file loop as well.
        bool alreadyRunning = false;
        if (loop)
        {
            if (player.Stream == null || !player.Playing)
            {
                AudioStream own = (AudioStream)stream.Duplicate();
                if (own is AudioStreamOggVorbis o) o.Loop = true;
                player.Stream = own;
            }
            else alreadyRunning = true;   // keep the voice, move the ear
        }
        else player.Stream = stream;

        // The reference follows a sound as its object moves -
        // `RemoteNode::RefreshPosition` walks the node's sound list and
        // calls setPosition on each (`RemoteNode.cpp:510-535`). A loop
        // here used to keep for ever the gain and pan it had the instant
        // it started, so a fountain was as loud behind you as in front.
        // It cannot follow every frame from here, but it can at least be
        // re-placed whenever the server mentions it again.
        _gain[player] = shape;
        player.VolumeDb = Mathf.LinearToDb(gain);
        // Distance and panning are worked out above, in the game's own
        // units, so Godot must not apply a second fall-off of its own on
        // top: attenuation off, and a distance no sound will reach.
        player.Attenuation = 0f;
        player.MaxDistance = 1e6f;
        player.Position = new Vector2(pan * 400f, 0f);
        if (!alreadyRunning) player.Play();
        if (Verbose) GD.Print($"[M59Sound] {info.ResourceName} gain {gain:0.00} pan {pan:0.00} loop {loop}");
    }

    /// <summary>
    /// Plays one at a known place, for the case where the caller has
    /// already resolved the source object's position.
    /// </summary>
    public void PlayAt(PlaySound info, float sx, float sy,
                       float listenerX, float listenerY, float facing)
    {
        if (info == null || string.IsNullOrEmpty(info.Resource))
        { if (Verbose) GD.Print($"[M59Sound] no file for {info?.ResourceName}"); return; }
        AudioStream stream = Load(info.Resource);
        if (stream == null) { if (Verbose) GD.Print($"[M59Sound] unreadable {info.Resource}"); return; }
        Emit(info, stream, sx, sy, listenerX, listenerY, facing);
    }

    /// <summary>Stops a looping sound the server has finished with.</summary>
    public void Stop(StopSound info)
    {
        if (info?.ResourceName == null) return;

        // StopSound keeps the name the server sent, which ends in .wav;
        // PlaySound swaps it for the .ogg that is actually on disk. The
        // two therefore never match on the raw name, so both go through
        // the same normalisation before being looked up.
        string key = Key(info.ResourceName);
        if (Verbose) GD.Print($"[M59Sound] stop asked for '{key}', loops {_loops.Count}");
        if (_loops.TryGetValue(key, out AudioStreamPlayer2D p) && p != null)
        {
            p.Stop();
            _loops.Remove(key);
            p.QueueFree();
            if (Verbose) GD.Print($"[M59Sound] stopped {key}");
        }
    }

    /// <summary>
    /// Silences everything that is still going, which is what a room
    /// change does: the reference stops and drops every sound in its
    /// global list when a Player message arrives
    /// (`ControllerSound.cpp:341-357`). Without it a fountain from a
    /// room three doors back plays for the rest of the session, and
    /// another one joins it at every doorway. Music is left alone -
    /// the reference changes that only when the server says to.
    /// </summary>
    public void StopAll()
    {
        foreach (var kv in _loops)
            if (kv.Value != null) { kv.Value.Stop(); kv.Value.QueueFree(); }
        _loops.Clear();
        foreach (var p in _pool) p.Stop();
        _gain.Clear();
    }

    /// <summary>
    /// The room's background music. Changed only when the track changes,
    /// so walking around does not restart it.
    /// </summary>
    public void PlayMusic(PlayMusic info)
    {
        string path = info?.Resource;
        // A name that resolves to nothing leaves what is playing alone.
        // The reference returns early (`ControllerSound.cpp:485-486`)
        // rather than treating a missing file as an instruction to fall
        // silent, and so does this now.
        if (string.IsNullOrEmpty(path))
        { if (Verbose) GD.Print($"[M59Sound] no music file for {info?.ResourceName}"); return; }
        // Music turned all the way down is not started at all, as
        // StartMusic refuses on MusicVolume == 0 (`:488`).
        if (MusicLevel <= 0f) return;
        if (path == _musicPlaying && _music.Playing) return;

        AudioStream stream = Load(path);
        if (stream == null) return;

        _musicPlaying = path;
        // The game plays music with play2D(..., looped = true), so it
        // runs until the room changes it.
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
        _music.Stream = stream;
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(MusicLevel, 0.0001f, 1f));
        _music.Play();
        if (Verbose) GD.Print($"[M59Sound] music {info.ResourceName}");
    }

    /// <summary>
    /// Where a sound comes from, in server units. False when the message
    /// says nothing about a place, which means "at you".
    /// </summary>
    static bool Where(PlaySound info, RooFile room, float listenerX, float listenerY,
                      out float x, out float y)
    {
        x = y = 0f;

        // A sound with a source object plays at that object.
        if (info.ID > 0)
        {
            // The caller owns the object list; the position comes in
            // through the room object it names, which GameView resolves
            // before calling. Nothing to do here.
            return false;
        }

        // The middle of a grid square, which is what the game does with a
        // row and column: (column - 1) * 1024 + 512 in room units.
        if (info.Row > 0 && info.Column > 0)
        {
            float roomX = (info.Column - 1) * 1024f + 512f;
            float roomY = (info.Row - 1) * 1024f + 512f;
            x = M59Geo.WorldToKod(roomX);
            y = M59Geo.WorldToKod(roomY);
            return true;
        }

        return false;
    }

    AudioStream Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_streams.TryGetValue(path, out AudioStream cached)) return cached;

        AudioStream stream = null;
        try
        {
            // Despite the library calling them wavs, the files are Ogg
            // Vorbis - PlaySound changes the extension itself.
            if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                stream = AudioStreamOggVorbis.LoadFromFile(path);
            else if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                stream = AudioStreamWav.LoadFromFile(path);
        }
        catch (Exception e) { GD.PrintErr($"[M59Sound] {path}: {e.Message}"); }

        _streams[path] = stream;
        return stream;
    }

    AudioStreamPlayer2D Idle()
    {
        foreach (AudioStreamPlayer2D p in _pool)
            if (!p.Playing) return p;

        if (_pool.Count >= Voices) return null;

        var made = new AudioStreamPlayer2D();
        AddChild(made);
        _pool.Add(made);
        return made;
    }

    /// <summary>
    /// One spelling for a sound's name, because the library gives two:
    /// PlaySound changes the extension to .ogg and StopSound does not.
    /// </summary>
    static string Key(string name)
        => string.IsNullOrEmpty(name) ? name
         : System.IO.Path.ChangeExtension(name, ".ogg").ToLowerInvariant();

    AudioStreamPlayer2D Loop(string name)
    {
        if (name != null && _loops.TryGetValue(name, out AudioStreamPlayer2D p) && p != null)
            return p;

        var made = new AudioStreamPlayer2D();
        AddChild(made);
        if (name != null) _loops[name] = made;
        return made;
    }
}
