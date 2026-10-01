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
    ///
    /// Full, because the reference's own default is full:
    /// DEFAULTVAL_ENGINE_SOUNDVOLUME is 10 on a 0..10 scale
    /// (`OgreClientConfig.h:57`). 0.7 was a quieter game than the one
    /// this is meant to sound like.
    /// </summary>
    [Export] public float Volume
    {
        get => _volume;
        set { _volume = value; Reheard(); }
    }
    float _volume = 1f;
    /// <summary>
    /// Music has its own level, as it does in the game - two sliders,
    /// not one - and a room's ambience is not the same nuisance as a
    /// fountain three doors away.
    ///
    /// Four tenths, which is DEFAULTVAL_ENGINE_MUSICVOLUME
    /// (`OgreClientConfig.h:56`): the game ships its music well under
    /// its sounds on purpose, and 0.5 here was neither that nor
    /// anything else in particular.
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
    float _musicLevel = 0.4f;
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
    /// <summary>
    /// How many one-shot sounds may overlap.
    ///
    /// The reference has no cap at all: `StartSound` hands every wave to
    /// irrklang and keeps the ISound it gets back
    /// (`ControllerSound.cpp:455-480`), and the mixer decides how many
    /// it can carry. Twelve here, with the thirteenth DROPPED, was the
    /// wrong end to give way at: a melee in a crowded room filled the
    /// pool with other people's swings and then swallowed your own,
    /// which is the one sound in the room you are listening for. So the
    /// number is higher - a phone mixer will not notice two dozen
    /// short oggs - and when it is reached the QUIETEST voice gives way
    /// rather than the newest, since the quietest is by construction
    /// the one furthest away and the least likely to be missed.
    /// </summary>
    [Export] public int Voices = 24;
    /// <summary>Prints what it plays, for the test harnesses.</summary>
    [Export] public bool Verbose = false;

    readonly Dictionary<string, AudioStream> _streams = new Dictionary<string, AudioStream>();
    /// <summary>
    /// One voice: the player, which object it belongs to, and where that
    /// object was the last time it was heard from.
    ///
    /// The reference attaches a sound to the object's scene node
    /// (`ControllerSound.cpp:471-472`) and moves it whenever the object
    /// moves (`RemoteNode.cpp:510-535`), and throws it away with the
    /// node (:112-125). Keyed on the filename alone, as this once was,
    /// two fountains in one room share one voice and a StopWave naming
    /// the file silences both - so what identifies the sound is part of
    /// the key. See LoopKey.
    /// </summary>
    sealed class Voice
    {
        public AudioStreamPlayer2D Player;
        public uint Owner;          // 0 when the sound has no object
        /// <summary>
        /// An id-less, place-less sound: the reference attaches this one
        /// to the AVATAR's node (`ControllerSound.cpp:431-447`) and
        /// `RemoteNode::RefreshPosition` carries it along as you walk
        /// (`RemoteNode.cpp:510-535`). Emitting it at wherever you were
        /// standing, as this did, meant a looping sound played on you
        /// stayed behind at that spot and got quieter and more
        /// off-centre the further you walked from it. Flagged rather
        /// than given co-ordinates, because its co-ordinates are always
        /// the listener's.
        /// </summary>
        public bool OnListener;
        public float X, Y, H;       // where it is, in the server's units - H is height
        public string File;
        /// <summary>Its gain before the master level, so a slider can be re-applied.</summary>
        public float Shape;
    }

    /// <summary>The one-shot voices, re-used as they fall silent.</summary>
    readonly List<Voice> _pool = new List<Voice>();
    readonly Dictionary<string, Voice> _loops = new Dictionary<string, Voice>();
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
    /// Gives every voice a chance to hear a volume change, from the gain
    /// it was last mixed with. The reference walks its own list and
    /// every node's list for the same reason
    /// (`ControllerSound.cpp:206-227`), and it makes no distinction
    /// between a loop and a one-shot while doing it.
    /// </summary>
    void Reheard()
    {
        foreach (var kv in _loops) Level(kv.Value);
        foreach (Voice v in _pool) Level(v);
    }

    void Level(Voice v)
    {
        if (v?.Player == null || !v.Player.Playing) return;
        v.Player.VolumeDb = Mathf.LinearToDb(MathF.Max(0.0001f, v.Shape * _volume));
    }

    /// <summary>
    /// Plays one. <paramref name="listenerX"/>, <paramref name="listenerY"/>
    /// and <paramref name="listenerH"/> are where you are and
    /// <paramref name="facing"/> which way, all in the server's units,
    /// so the fall-off and the panning have something to measure from.
    /// </summary>
    public void Play(PlaySound info, RooFile room,
                     float listenerX, float listenerY, float listenerH, float facing)
    {
        if (info == null || string.IsNullOrEmpty(info.Resource)) return;

        AudioStream stream = Load(info.Resource);
        if (stream == null) return;

        bool onListener = false;
        if (!Where(info, room, out float sx, out float sy, out float sh))
        {
            if (info.ID > 0)
            {
                // A sound on an object nobody can find. The reference
                // never assigns a position in that case: x, y and z are
                // initialised to zero and the `if (source && ...)`
                // inside the ID branch simply does not fire
                // (`ControllerSound.cpp:388-411`), so the sound plays at
                // the room's origin and is heard faintly from wherever
                // you are. Playing it at the listener instead - full
                // volume, dead centre - turned every stale object id
                // into a sound in your ear.
                sx = sy = sh = 0f;
            }
            else
            {
                // No id and no grid square: the reference plays this one
                // on the avatar's own node (:431-447), which is to say
                // on you, and keeps it there.
                onListener = true;
                sx = listenerX; sy = listenerY; sh = listenerH;
            }
        }

        Emit(info, stream, sx, sy, sh, onListener, listenerX, listenerY, listenerH, facing);
    }

    void Emit(PlaySound info, AudioStream stream, float sx, float sy, float sh, bool onListener,
              float listenerX, float listenerY, float listenerH, float facing)
    {
        bool loop = info.PlayFlags != null && info.PlayFlags.IsLoop;
        // Asked for before the player is made, not after: creating the
        // loop player first left an idle node behind for every distinct
        // ambient while looping sounds were switched off.
        if (loop && !Loops) return;
        if (_volume <= 0.001f) return;

        string key = loop ? LoopKey(info) : null;
        Voice voice = loop ? Voice3D(key, info, sx, sy, sh, onListener) : Idle(info);
        if (voice?.Player == null) return;
        AudioStreamPlayer2D player = voice.Player;

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
        // Follow does that job frame by frame now; this just records
        // where the server says the sound is.
        voice.OnListener = onListener;
        voice.X = sx; voice.Y = sy; voice.H = sh;
        // Distance and panning are worked out here, in the game's own
        // units, so Godot must not apply a second fall-off of its own on
        // top: attenuation off, and a distance no sound will reach.
        player.Attenuation = 0f;
        player.MaxDistance = 1e6f;
        Mix(voice, listenerX, listenerY, listenerH, facing);
        if (!alreadyRunning) player.Play();
        if (Verbose)
            GD.Print($"[M59Sound] {info.ResourceName} gain {voice.Shape * _volume:0.00} loop {loop}");
    }

    /// <summary>
    /// Plays one at a known place, for the case where the caller has
    /// already resolved the source object's position.
    /// </summary>
    public void PlayAt(PlaySound info, float sx, float sy, float sh,
                       float listenerX, float listenerY, float listenerH, float facing)
    {
        if (info == null || string.IsNullOrEmpty(info.Resource))
        { if (Verbose) GD.Print($"[M59Sound] no file for {info?.ResourceName}"); return; }
        AudioStream stream = Load(info.Resource);
        if (stream == null) { if (Verbose) GD.Print($"[M59Sound] unreadable {info.Resource}"); return; }
        Emit(info, stream, sx, sy, sh, false, listenerX, listenerY, listenerH, facing);
    }

    /// <summary>
    /// Works out one voice's level and panning from where it is and
    /// where you are.
    ///
    /// The distance is the full three-dimensional one, as the game's is:
    /// `StartSound` fills in all three components of the vec3df it hands
    /// to play3D - including the room's floor height at the grid square,
    /// from GetHeightAt (`ControllerSound.cpp:420-427`) - and
    /// `UpdateListener` gives irrklang the listener's own height as well
    /// (:140-142). Measuring the distance flat made a fountain in the
    /// cellar as loud as one at your feet, which in a game of stacked
    /// rooms and balconies is most of them.
    /// </summary>
    void Mix(Voice v, float listenerX, float listenerY, float listenerH, float facing)
    {
        float dx = 0f, dy = 0f, dh = 0f;
        if (!v.OnListener)
        { dx = v.X - listenerX; dy = v.Y - listenerY; dh = v.H - listenerH; }

        float distance = MathF.Sqrt(dx * dx + dy * dy + dh * dh);

        // The game's own fall-off, which is inverse-distance rather than
        // linear: `setRolloffFactor(0.002f)` with a maximum distance of
        // 2000 and a minimum of 0 (`ControllerSound.cpp:45-47`). Past the
        // maximum the sound does not vanish, it stops getting quieter -
        // so a distant bell is faint and still there, where a linear
        // ramp cut it off dead. A linear ramp was also much too loud in
        // the middle: at 500 units it gave 0.75 of full where the game
        // gives 0.50.
        v.Shape = 1f / (1f + Rolloff * MathF.Min(distance, MaxDistance));
        Level(v);

        // Panned by which side of you it is on: the component of the
        // direction across your facing, -1 hard left to +1 hard right.
        //
        // Which way round that component runs is not a matter of taste -
        // it has to agree with the picture. The renderer puts a world
        // point at `W/2 + lateral * scale` with
        // `lateral = dx * sin(-angle) + dy * cos(-angle)`, that is
        // `dy * cos(angle) - dx * sin(angle)` (`Renderer.cs:1129-1135`,
        // and the same expression for the sprites at :941), so THAT is
        // screen-right; strafing right moves you along (-sin, cos) for
        // the same reason. This used to compute `dx * s - dy * c`, the
        // exact negative, so every sound in the game came out of the
        // wrong speaker: a footstep you could see on your right was
        // heard on your left.
        float pan = 0f;
        if (distance > 1f)
        {
            float c = MathF.Cos(facing), s = MathF.Sin(facing);
            pan = Mathf.Clamp((dy * c - dx * s) / distance, -1f, 1f);
        }
        v.Player.Position = new Vector2(pan * 400f, 0f);
    }

    /// <summary>
    /// Keeps the voices where their objects are, and stops the looping
    /// ones whose object has gone.
    ///
    /// The reference does both without being asked: the sound hangs off
    /// the object's scene node, `RefreshPosition` moves every sound in
    /// that node's list whenever the object moves
    /// (`RemoteNode.cpp:510-535`), and the node's destructor stops and
    /// drops them (:112-125). Here the voices are not attached to
    /// anything, so once a frame they are told where to be. Without
    /// this a fountain was exactly as loud behind you as in front, for
    /// as long as the room lasted.
    ///
    /// The one-shots are walked too. They were not, on the grounds that
    /// a one-shot is over before anything can move - but irrklang
    /// re-mixes every playing sound against the new listener position on
    /// each `setListenerPosition` (`ControllerSound.cpp:149`), one-shots
    /// included, and a wave in this game is often a second or more of
    /// scream or crash. Frozen at the pan they started with, those swung
    /// to the wrong side of you as you turned and stayed there.
    ///
    /// <paramref name="present"/> answers "is this object still in the
    /// room, and where"; a sound with no object (id 0) is left where the
    /// server put it, since it belongs to the room rather than to
    /// anything in it - unless it is one of the avatar's own, which
    /// Mix keeps on the listener.
    /// </summary>
    public void Follow(Func<uint, (bool Here, float X, float Y, float H)> present,
                       float listenerX, float listenerY, float listenerH, float facing)
    {
        List<string> gone = null;
        foreach (var kv in _loops)
        {
            Voice l = kv.Value;
            if (l?.Player == null) continue;

            if (l.Owner != 0 && present != null)
            {
                (bool here, float x, float y, float h) = present(l.Owner);
                if (!here)
                {
                    (gone ??= new List<string>()).Add(kv.Key);
                    continue;
                }
                l.X = x; l.Y = y; l.H = h;
            }

            Mix(l, listenerX, listenerY, listenerH, facing);
        }

        foreach (Voice v in _pool)
        {
            if (!v.Player.Playing) continue;
            if (v.Owner != 0 && present != null)
            {
                (bool here, float x, float y, float h) = present(v.Owner);
                // A one-shot outlives its object without ceremony: the
                // reference's node destructor stops looping and
                // non-looping sounds alike, but a wave from a monster
                // that has just died is half the point of it dying, so
                // it is left to finish where it last was.
                if (here) { v.X = x; v.Y = y; v.H = h; }
            }
            Mix(v, listenerX, listenerY, listenerH, facing);
        }

        if (gone == null) return;
        foreach (string k in gone)
        {
            Drop(_loops[k]);
            _loops.Remove(k);
            if (Verbose) GD.Print($"[M59Sound] dropped {k}: its object left");
        }
    }

    static void Drop(Voice v)
    {
        v?.Player?.Stop();
        v?.Player?.QueueFree();
    }

    /// <summary>
    /// Stops a sound the server has finished with.
    ///
    /// The reference looks in three places, in order, and stops the
    /// FIRST sound it finds whose source is that file: the named
    /// object's own list, then the avatar's, then its global list -
    /// returning out of the whole handler the moment it stops one
    /// (`ControllerSound.cpp:272-337`). Two things follow from that
    /// which this did not do. One: with no object id it stopped EVERY
    /// copy of the file, so one guard going quiet silenced the torches
    /// of all the others. Two: it only ever searched the loops, while
    /// the reference's lists hold non-looping sounds too - a long wave
    /// the server wanted cut short played on to its end.
    /// </summary>
    public void Stop(StopSound info)
    {
        if (info?.ResourceName == null) return;

        // StopSound keeps the name the server sent, which ends in .wav;
        // PlaySound swaps it for the .ogg that is actually on disk. The
        // two therefore never match on the raw name, so both go through
        // the same normalisation before being looked up.
        string file = Key(info.ResourceName);
        if (Verbose) GD.Print($"[M59Sound] stop asked for '{file}' id {info.ID}, loops {_loops.Count}");

        // The reference's three passes, in its order. The last one takes
        // anything, which is how a sound whose object has since been
        // forgotten still stops.
        Func<Voice, bool>[] passes =
        {
            v => info.ID != 0 && v.Owner == info.ID,
            v => v.OnListener,
            v => true,
        };

        foreach (Func<Voice, bool> pass in passes)
        {
            foreach (var kv in _loops)
            {
                Voice l = kv.Value;
                if (l == null || l.File != file || !pass(l)) continue;
                Drop(l);
                _loops.Remove(kv.Key);
                if (Verbose) GD.Print($"[M59Sound] stopped {kv.Key}");
                return;
            }

            foreach (Voice v in _pool)
            {
                if (!v.Player.Playing || v.File != file || !pass(v)) continue;
                v.Player.Stop();
                if (Verbose) GD.Print($"[M59Sound] stopped one-shot {file}");
                return;
            }
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
        foreach (var kv in _loops) Drop(kv.Value);
        _loops.Clear();
        foreach (Voice v in _pool) { v.Player.Stop(); v.Shape = 0f; v.File = null; v.Owner = 0; }
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
    /// says nothing about a place, which the caller has to read two ways
    /// - see Play.
    /// </summary>
    static bool Where(PlaySound info, RooFile room,
                      out float x, out float y, out float h)
    {
        x = y = h = 0f;

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
            // And the height of the floor there, which the reference
            // asks the room for in exactly this place -
            // `CurrentRoom->GetHeightAt(x, z, out, true, false)` and then
            // the same 0.0625 scaling as the other two components
            // (`ControllerSound.cpp:420-428`). A grid square outside the
            // BSP tree answers -1, which is not a height; the floor of
            // the room is the honest guess there.
            if (room != null)
            {
                float height = room.GetHeightAt(roomX, roomY, out RooSubSector _, true, false);
                if (height > 0f) h = M59Geo.XYHeightToKod(height);
            }
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

    /// <summary>
    /// A one-shot voice: a silent one if there is one, a new one while
    /// there is room, and otherwise the quietest of those playing. See
    /// Voices for why it is the quietest that gives way.
    /// </summary>
    Voice Idle(PlaySound info)
    {
        Voice free = null;
        foreach (Voice v in _pool)
            if (!v.Player.Playing) { free = v; break; }

        if (free == null && _pool.Count < Voices)
        {
            free = new Voice { Player = new AudioStreamPlayer2D() };
            AddChild(free.Player);
            _pool.Add(free);
        }

        if (free == null)
            foreach (Voice v in _pool)
                if (free == null || v.Shape < free.Shape) free = v;

        if (free == null) return null;
        free.Player.Stop();
        free.Owner = info.ID;
        free.File = Key(info.ResourceName);
        return free;
    }

    /// <summary>
    /// One spelling for a sound's name, because the library gives two:
    /// PlaySound changes the extension to .ogg and StopSound does not.
    /// </summary>
    static string Key(string name)
        => string.IsNullOrEmpty(name) ? name
         : System.IO.Path.ChangeExtension(name, ".ogg").ToLowerInvariant();

    /// <summary>
    /// The voice for one looping sound, made if it does not exist.
    /// </summary>
    Voice Voice3D(string key, PlaySound info, float sx, float sy, float sh, bool onListener)
    {
        if (key == null) return null;
        if (_loops.TryGetValue(key, out Voice had) && had?.Player != null)
        {
            had.X = sx; had.Y = sy; had.H = sh; had.OnListener = onListener;
            return had;
        }

        var made = new Voice
        {
            Player = new AudioStreamPlayer2D(),
            Owner = info.ID,
            OnListener = onListener,
            File = Key(info.ResourceName),
            X = sx, Y = sy, H = sh,
        };
        AddChild(made.Player);
        _loops[key] = made;
        return made;
    }

    /// <summary>
    /// What identifies a looping voice.
    ///
    /// The object it belongs to and the file, so two of the same
    /// fountain in one room are two sounds and stopping one does not
    /// stop the other - and the GRID SQUARE as well, because a sound
    /// placed by row and column has no object and its id is therefore
    /// always 0. Keyed on id and file alone, two fountains using the
    /// same wav at opposite ends of a room collided on one key: the
    /// second one found the first one's voice already running, moved it
    /// to its own square, and one of the two fountains was silent while
    /// the other was heard from the wrong place. The reference cannot
    /// have this problem because each play3D hands back its own ISound
    /// and it keeps them all (`ControllerSound.cpp:414-429`, :455-462,
    /// :474-476).
    /// </summary>
    static string LoopKey(PlaySound info)
        => info.ID + "|" + info.Row + "," + info.Column + "|" + Key(info.ResourceName);
}
