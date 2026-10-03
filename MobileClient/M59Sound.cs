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
        /// <summary>Where it sits across you, -1 hard left to +1 hard right.</summary>
        public float Pan;
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

    /// <summary>
    /// Puts one voice's gain on its player.
    ///
    /// It used to give up on a player that was not already
    /// <c>Playing</c>, which meant the level set on the way INTO a play
    /// never landed: a new voice started at 0 dB and a recycled one at
    /// whatever the last sound through that slot was using, and only the
    /// next Follow() put it right. Measured, that was the whole distance
    /// model gone - every distance came out at +0.0 dB with no Follow,
    /// against -6.0/-9.5/-14.0 dB at 500/1000/2000 units with it - and
    /// for a sound that arrives before the avatar does, FollowSounds
    /// returns early (`GameView.cs:3085-3087`) and it is loud for its
    /// whole length. Setting VolumeDb on an idle player costs nothing and
    /// is remembered, so there is no reason to skip it.
    ///
    /// The pan boost is taken back out here. Godot's law (see Mix) lifts
    /// the near channel above unity as it silences the far one, which
    /// would clip a hard-panned sound at full volume; dividing by
    /// 1 + |pan| leaves the near ear at exactly the level the sound would
    /// have had dead ahead and fades the far ear to nothing, which is how
    /// the reference's 3D mix sounds.
    /// </summary>
    void Level(Voice v)
    {
        if (v?.Player == null) return;
        float gain = v.Shape * _volume / (1f + MathF.Abs(v.Pan));
        v.Player.VolumeDb = Mathf.LinearToDb(MathF.Max(0.0001f, gain));
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
                //
                // A ONE-SHOT is left there, as the reference has it. A
                // LOOP is started there too but does not stay: Follow
                // drops a loop whose object is not in the room, on
                // purpose and against the reference - see the long
                // comment in Follow before changing either.
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
        // No volume gate. The reference's StartSound has none - it plays
        // the sound and then calls setVolume
        // (`ControllerSound.cpp:374-480`), so at volume 0 the loop exists
        // and is silent, and AdjustSoundVolume (:206-227) brings it back
        // the instant the slider moves. Returning early here instead cost
        // the room its ambients for good: the slider can be stepped to 0
        // and is persisted, so muting for a phone call and unmuting left
        // the room silent until the next doorway. The gate belongs to
        // MUSIC only (`:488`), which PlayMusic mirrors.

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
        // Started, then levelled, then let go - the reference's order,
        // and for its reason: play3D(..., startPaused = true, ...) then
        // setVolume then setIsPaused(false)
        // (`ControllerSound.cpp:455-479`, music :495-518), so a sound is
        // never audible at a level nobody has worked out yet. Mixing
        // first and playing after cannot do that, because a player has
        // no position and no level to set until it is running.
        if (!alreadyRunning)
        {
            player.StreamPaused = true;
            player.Play();
        }
        Mix(voice, listenerX, listenerY, listenerH, facing);
        if (!alreadyRunning) player.StreamPaused = false;
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
        v.Pan = pan;
        Steer(v.Player, pan);
    }

    /// <summary>
    /// Puts a voice at <paramref name="pan"/>, -1 hard left to +1 hard
    /// right, given what Godot's own panning actually does.
    ///
    /// AudioStreamPlayer2D has no pan property: it derives one from where
    /// the node sits relative to the listener, RELATIVE TO THE VIEWPORT.
    /// The law was measured off mixed PCM, sweeping a steady tone past a
    /// listener and reading per-channel RMS (39 points, a 2.0s 440Hz ogg,
    /// AudioEffectCapture on Master). It is, with w the viewport's
    /// visible width and g the two strengths multiplied:
    ///
    ///     t = clamp(x / w, -1, 1)
    ///     g = ProjectSettings "audio/general/2d_panning_strength"
    ///         * node PanningStrength * t
    ///     left = 1 - g,  right = 1 + g
    ///
    /// Every measured point fits that to a tenth of a dB: at g = 0.125,
    /// 0.25, 0.375 and 0.5 the channels part by 2.2, 4.4, 6.8 and 9.5 dB,
    /// and at g = 1 the far channel goes to true silence.
    ///
    /// Two things follow. The clamp is on x/w, so no offset past one
    /// viewport width buys anything - 1920px and 2880px measured
    /// identically. And the project's global strength defaults to 0.5, so
    /// with an untouched node the most any position can give is g = 0.5,
    /// which is 9.5 dB and not full separation.
    ///
    /// That is why the old `pan * 400f` was inaudible: it is 21% of a
    /// 1920-wide viewport, which is g = 0.104 and 1.8 dB between the ears
    /// - and, being a bare pixel count, it got WEAKER on a wider window,
    /// which is the wrong way round for a client that runs at whatever
    /// size the phone is. Sweeping it proved the causation: 400 gave
    /// 1.8 dB, 960 gave 4.4, 1920 gave 9.5.
    ///
    /// So: the offset is one full viewport width at full pan, which is
    /// where the clamp is, and the node's strength undoes the global one
    /// so g comes out equal to pan itself whatever either is set to.
    /// Nothing here is a magic number - both terms are read from the
    /// engine, so a different window or a changed project setting cannot
    /// quietly flatten the mix again. Level() takes the resulting boost
    /// back off the near channel.
    ///
    /// The DIRECTION was already right and is untouched: it has to agree
    /// with the picture, and `dy * cos - dx * sin` is literally what
    /// Renderer.cs:1129-1135 puts on screen-right.
    /// </summary>
    void Steer(AudioStreamPlayer2D player, float pan)
    {
        float width = 1920f;
        Viewport vp = GetViewport();
        if (vp != null)
        {
            float w = vp.GetVisibleRect().Size.X;
            if (w > 1f) width = w;
        }

        // Guarded because a project may set the global strength to zero,
        // and a division would then be an infinity on a player property.
        float global = 1f;
        try
        {
            global = (float)ProjectSettings.GetSetting("audio/general/2d_panning_strength", 0.5f);
        }
        catch { }
        if (!(global > 0.001f)) global = 0.5f;

        player.PanningStrength = 1f / global;
        player.Position = new Vector2(pan * width, 0f);
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
                    // DELIBERATE DIVERGENCE - do not "fix" by matching.
                    // A loop whose object is not in the room is dropped
                    // here, whether the object LEFT (the case this
                    // exists for) or NEVER WAS (a "ghost" id). The
                    // reference keeps the ghost: with no object to
                    // find, `StartSound` leaves x, y, z at 0, attaches
                    // nothing, files the sound in the global list and
                    // plays it at the origin until a StopWave or the
                    // next Player message (`ControllerSound.cpp:393-411`,
                    // `:472-476`, `:326-335`, `:341-357`). Verified
                    // here (M59_SNDGHOST; before this comment): the
                    // loop starts at gain 0.33, is dropped the next
                    // frame, and a StopWave for the id logs "loops 0"
                    // and does nothing.
                    //
                    // Why that is right, and matching would not be:
                    //  - The reference's ghost is not a design, it is
                    //    a null check falling through. Its design is
                    //    that a sound lives and dies with its object
                    //    (`RemoteNode.cpp:112-125` stops them in the
                    //    node's destructor), and an id that is not in
                    //    the room names an object that is already
                    //    dead. This client has no node destructor, so
                    //    this branch IS that destructor; ghost and
                    //    departed are the same case to it.
                    //  - Server-104 never sends a loop with an object
                    //    id (`room.kod:684-689,715-719`: row and col
                    //    only), so a ghost can only be a race (the
                    //    object left between the two messages) or a
                    //    bug. Matching would turn either into a
                    //    permanent hum from the room's corner: at the
                    //    origin the distance is that of the avatar from
                    //    (0,0), ~1000 units in barinn, so gain ~0.33 -
                    //    and the clamp at 2000 (Mix) leaves it never
                    //    below 0.2. It is NOT inaudible: measured on
                    //    the Master bus, music alone 0.0355 RMS, with
                    //    the pinned loop 0.041, and nothing ends it
                    //    short of a stop that names this exact id or
                    //    the next room. On a phone a hum that cannot
                    //    be placed is worse than a missing ambient.
                    //  - What the drop costs is nothing visible: a
                    //    StopWave that finds nothing is the same no-op
                    //    the reference gives for an unknown sound
                    //    (`ControllerSound.cpp:272-337` falls out of
                    //    all three lists; Stop below does likewise)
                    //    and raises no UI. Measured: stop for a never-
                    //    played id leaves the bus flat.
                    //
                    // What else depends on this branch, and was checked
                    // so the next audit need not:
                    //  - It only runs once the avatar exists
                    //    (GameView.FollowSounds returns first
                    //    otherwise), and the library adds the avatar
                    //    and every other object inside ONE
                    //    RoomContents handler, which `ProcessQueues`
                    //    drains before the frame
                    //    (`DataController.cs:2147-2215`,
                    //    `BaseClient.cs:333-349`). So a loop that
                    //    arrives BEFORE RoomContents (the ordering of
                    //    the 2cc4f2f bug) is never dropped for its
                    //    object being late: it plays, and binds to
                    //    the object the first frame it exists
                    //    (measured, id of a rat in the contents:
                    //    0.0724 RMS at the rat, stop by id finds it).
                    //    The reference does not even do that - it
                    //    pins that sound at the origin for good
                    //    (0.0412 under the matching variant) - so
                    //    matching would REGRESS this case.
                    //  - A loop whose object is added AFTER the
                    //    avatar is in, in a later frame than the
                    //    sound, is dropped. The stream is ordered and
                    //    a server adds an object before it speaks for
                    //    it, so that is a server bug, and silence is
                    //    the safe way to meet one.
                    //  - One-shots never come through here: a ghost
                    //    one-shot plays once at the origin as the
                    //    reference does (it ends by itself, and a stop
                    //    for the id cuts it - 0.0552 RMS, stopped
                    //    after 4 s), and one whose object leaves
                    //    first is left to finish (see below).
                    //  - Objects that leave (the case this exists
                    //    for, M59_SNDOWNER) are dropped as the
                    //    reference's destructor would, whether they
                    //    go a second or a frame after the sound
                    //    starts.
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
        Release(v);
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
    /// Lets go of the music's private stream. The copy is a RefCounted
    /// the player and this wrapper both hold; Godot drops the player's
    /// reference when the node is freed, and the C# wrapper's only when
    /// the finalizer runs - which at exit it does not, so every run that
    /// had played music ended with "4 ObjectDB instances were leaked"
    /// (the stream, its packet sequence, the playback, its playback).
    /// Dispose releases the wrapper's reference now.
    /// </summary>
    void ReleaseMusic()
    {
        if (_music == null) return;
        _music.Stop();
        AudioStream had = _music.Stream;
        _music.Stream = null;
        had?.Dispose();
    }

    /// <summary>
    /// Everything this node holds goes before it does: the players
    /// stop (a playing one keeps its playback alive in the mixer past
    /// the tree), the loop copies and the music copy are disposed, and
    /// the cache of masters with them.
    /// </summary>
    public override void _ExitTree() => Silence();

    /// <summary>Everything stopped and every stream let go; see _ExitTree.</summary>
    public void Silence()
    {
        try
        {
            ReleaseMusic();
            _musicPlaying = "";
            foreach (var kv in _loops) Release(kv.Value);
            _loops.Clear();
            foreach (Voice v in _pool) Release(v);
            _pool.Clear();
            foreach (var kv in _streams) kv.Value?.Dispose();
            _streams.Clear();
        }
        catch (Exception e) { GD.PrintErr($"[M59Sound] exit: {e.Message}"); }
    }

    /// <summary>A voice's player stopped and its own stream copy let go.</summary>
    static void Release(Voice v)
    {
        AudioStreamPlayer2D p = v?.Player;
        if (p == null || !GodotObject.IsInstanceValid(p)) return;
        p.Stop();
        AudioStream had = p.Stream;
        p.Stream = null;
        // One-shots share the cached master, which _ExitTree disposes
        // once; only a loop's copy is this voice's own.
        if (had is AudioStreamOggVorbis o && o.Loop) had.Dispose();
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
        // runs until the room changes it. On a COPY: Load's cache is
        // shared with the one-shots, and setting Loop on the master
        // made every later one-shot of the same file loop for ever.
        ReleaseMusic();
        AudioStream own = (AudioStream)stream.Duplicate();
        if (own is AudioStreamOggVorbis ogg) ogg.Loop = true;
        _music.Stream = own;
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

    /// <summary>
    /// Every folder this has had to look in, lowercased file name to the
    /// name as the filesystem actually spells it. Built once per folder.
    /// </summary>
    readonly Dictionary<string, Dictionary<string, string>> _byFolder =
        new Dictionary<string, Dictionary<string, string>>();
    /// <summary>Names already complained about, so a missing file costs one line.</summary>
    readonly HashSet<string> _moaned = new HashSet<string>();

    /// <summary>
    /// The file the server means, spelled the way the disk spells it.
    ///
    /// The string table and the files disagree about case. Measured
    /// against this repo's own rsc0000.rsb and Resources/{sounds,music}:
    /// of the 495 names the table asks for that exist on disk, 282 match
    /// case-exactly and 213 do not - `ambcave.ogg` for `AMBCave.ogg`,
    /// `LogIn.ogg` for `Login.ogg`, `KILLED.ogg` for `Killed.ogg`. The
    /// library hands us a path built from the CALLER's spelling
    /// (`ResourceManager.GetWavFile` combines WavFolder with the name it
    /// was asked for, `ResourceManager.cs:400`; music at :431), so on
    /// Windows all of them resolve anyway and on Android none of them
    /// do. Worse, that method writes the bad path back into a
    /// case-INSENSITIVE dictionary (`Wavs.TryUpdate(File, filename,
    /// null)`, :403), so one mis-cased request poisons the file for the
    /// session and asking later with the disk's own spelling fails too.
    ///
    /// This is not a bug in this port: the reference asks for
    /// "nec02.ogg" for its own login music (`OgreClient.cpp:1201`) while
    /// the file is `Nec02.ogg`, and only NTFS hides it.
    ///
    /// M59Client.Init closes the same hole at registration so the
    /// library never gets the chance (see RegisterAudio there). This is
    /// the backstop, and it is also the half that registration cannot
    /// reach: a path that came from somewhere else entirely.
    /// </summary>
    string OnDisk(string path)
    {
        if (System.IO.File.Exists(path)) return path;

        string folder = System.IO.Path.GetDirectoryName(path);
        string name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name)) return null;

        if (!_byFolder.TryGetValue(folder, out Dictionary<string, string> index))
        {
            index = new Dictionary<string, string>();
            try
            {
                // "*" and not "*.ogg": the pattern itself is
                // case-sensitive here, which is the second half of the
                // same bug - `Directory.GetFiles(folder, "*.ogg")`
                // returns a.ogg and silently drops B.OGG, measured.
                foreach (string f in System.IO.Directory.GetFiles(folder))
                    index[System.IO.Path.GetFileName(f).ToLowerInvariant()] =
                        System.IO.Path.GetFileName(f);
            }
            catch (Exception e) { GD.PrintErr($"[M59Sound] cannot list {folder}: {e.Message}"); }
            _byFolder[folder] = index;
        }

        return index.TryGetValue(name.ToLowerInvariant(), out string real)
             ? System.IO.Path.Combine(folder, real)
             : null;
    }

    AudioStream Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Keyed on the name the filesystem uses, not the one that was
        // asked for, so two spellings of one file are one stream and
        // neither can poison the other.
        string real = OnDisk(path);
        if (real == null)
        {
            if (_moaned.Add(path)) GD.PrintErr($"[M59Sound] no such file: {path}");
            return null;
        }
        if (_streams.TryGetValue(real, out AudioStream cached)) return cached;

        AudioStream stream = null;
        try
        {
            // Despite the library calling them wavs, the files are Ogg
            // Vorbis - PlaySound changes the extension itself.
            if (real.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                stream = AudioStreamOggVorbis.LoadFromFile(real);
            else if (real.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                stream = AudioStreamWav.LoadFromFile(real);
        }
        catch (Exception e) { GD.PrintErr($"[M59Sound] {real}: {e.Message}"); }

        if (stream == null) GD.PrintErr($"[M59Sound] unreadable: {real}");
        // The master copy never loops. Looping is per-play in the
        // reference (an argument to play3D/play2D,
        // `ControllerSound.cpp:457`, :496) and per-COPY here: both the
        // loop path and PlayMusic duplicate before setting it, so
        // nothing can turn a shared stream into a looping one behind a
        // later caller's back.
        if (stream is AudioStreamOggVorbis o) o.Loop = false;
        _streams[real] = stream;
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
