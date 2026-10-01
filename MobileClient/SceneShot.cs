using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Screenshots the offline first-person view: the renderer, the textures,
/// the minimap and the touch controls, all of it running inside Godot
/// rather than in a test harness. Nothing else loads this scene.
///
///     godot --path MobileClient SceneShot.tscn -- \
///       --out room.png --res /tmp/res --room barinn.roo --wait 30
///
/// A virtual display is enough; `--headless` renders nothing.
/// </summary>
public partial class SceneShot : Node
{
    public override void _Ready()
    {
        string outPath = Arg("--out", "room.png");
        string res = Arg("--res", "/tmp/res");
        string room = Arg("--room", "barinn.roo");
        int wait = int.TryParse(Arg("--wait", "30"), out int w) ? w : 30;

        int width = int.TryParse(Arg("--width", "480"), out int rw) ? rw : 480;

        // --host turns this into a shot of the live view instead of the
        // offline one, pointed wherever you say - which in practice means
        // Tools/Meridian59.Net8FakeServer on loopback.
        string host = Arg("--host", null);
        if (host != null)
        {
            var live = new GameView
            {
                ResourceDir = res,
                Host = host,
                // --char skips the character picker, which every
                // fixture but the creation one wants: the picker now
                // appears whenever the account has an empty slot,
                // because that is the only way to reach the wizard.
                Character = Arg("--char", ""),
                Port = int.TryParse(Arg("--port", "15999"), out int pt) ? pt : 15999,
                RenderWidth = width,
                AutoConnect = true,
            };
            AddChild(live);
        }
        else
        {
            var view = new FirstPersonView
            {
                ResourceDir = res,
                RoomFile = room,
                RenderWidth = width,
            };
            AddChild(view);
        }

        // --walk <frames> holds the forward key down and photographs the
        // result every few frames, which is how the movement path gets
        // exercised at all: it runs through the same input handling a
        // thumb on the stick does.
        // A flag rather than a value: OS.GetCmdlineUserArgs gives a flat
        // list, so Arg only sees a name followed by something.
        _everyStep = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--shots") >= 0;

        if (int.TryParse(Arg("--walk", "0"), out int walk) && walk > 0)
            Walk(outPath, wait, walk, Arg("--keys", "W"));
        // Anything with a sequence in it - several presses, or a tap or
        // a slot placed among them - goes through the walker.
        else if (Arg("--slot", null) != null
                 || (Arg("--press", null) ?? "").Contains('@')
                 || (Arg("--press", null) ?? "").Contains(','))
            Slot(outPath, wait, Arg("--slot", null), Arg("--press", null), Arg("--tap", null),
                 Arg("--text", null));
        else if (Arg("--tick", null) != null)
            Tick(outPath, wait, Arg("--tick", null), Arg("--press", null));
        else if (Arg("--login", null) != null)
            SignIn(outPath, wait, Arg("--login", null));
        else if (Arg("--drag", null) != null)
            Drag(outPath, wait, Arg("--drag", null), Arg("--press", null));
        else if (Arg("--tap", null) != null || Arg("--press", null) != null)
            Poke(outPath, wait, Arg("--tap", null), Arg("--press", null));
        else
            Shoot(outPath, wait);
    }

    /// <summary>
    /// Waits a set number of frames before reading the screen back. The
    /// view loads its room and builds its textures over several frames,
    /// and a shot taken too early catches a black screen with a status
    /// line on it - which looks exactly like a broken renderer.
    /// </summary>
    async void Shoot(string path, int frames)
    {
        for (int i = 0; i < Math.Max(1, frames); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(path);
        GD.Print(e == Error.Ok ? $"[SceneShot] wrote {path}" : $"[SceneShot] save failed: {e}");
        GetTree().Quit();
    }

    /// <summary>
    /// Holds keys down and takes a numbered shot every few frames. The
    /// keys go in through Godot's own input queue rather than by poking
    /// the view, so what is tested is the path a real press takes.
    /// </summary>
    async void Walk(string path, int settle, int frames, string keys)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var held = new List<Key>();
        foreach (char k in keys.ToUpperInvariant())
            if (Enum.TryParse("Key" + k, out Key parsed) || Enum.TryParse(k.ToString(), out parsed))
                held.Add(parsed);

        foreach (Key k in held)
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });

        int shot = 0;
        for (int i = 0; i <= frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (i % Math.Max(1, frames / 8) != 0) continue;

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image img = GetViewport().GetTexture().GetImage();
            string numbered = path.Replace(".png", $"-{shot:D2}.png");
            img.SavePng(numbered);
            GD.Print($"[SceneShot] wrote {numbered}");
            shot++;
        }

        foreach (Key k in held)
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });

        GetTree().Quit();
    }

    /// <summary>
    /// Taps the screen and then presses a named button, waiting between
    /// the two. The tap goes in as a real touch event so it runs the same
    /// path a thumb does - the touch controls, then the tap-to-target -
    /// rather than poking the view's internals.
    /// </summary>
    async void Poke(string path, int settle, string tap, string press)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (tap != null)
        {
            string[] parts = tap.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], out float tx) && float.TryParse(parts[1], out float ty))
            {
                var at = new Vector2(tx, ty);
                Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = false });
                GD.Print($"[SceneShot] tapped {at}");
            }
        }

        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (press != null)
        {
            Button b = FindButton(GetTree().Root, press);
            if (b != null) { b.EmitSignal(BaseButton.SignalName.Pressed); GD.Print($"[SceneShot] pressed {press}"); }
            else GD.Print($"[SceneShot] no button called {press}");
        }

        for (int i = 0; i < 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print($"[SceneShot] wrote {path}");
        GetTree().Quit();
    }

    /// <summary>
    /// Drags from one point to another with the mouse, which is what
    /// Godot's drag and drop listens for: a press, several motions with
    /// the button held, then a release. One motion is not enough - the
    /// drag only starts once the pointer has moved far enough while
    /// down - so this walks there in steps, a frame apart.
    ///
    /// --press may name a button to hit first, so a panel can be opened
    /// in the same run.
    /// </summary>
    async void Drag(string path, int settle, string spec, string press)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (press != null)
        {
            Button b = FindButton(GetTree().Root, press);
            if (b != null) { b.EmitSignal(BaseButton.SignalName.Pressed); GD.Print($"[SceneShot] pressed {press}"); }
            else GD.Print($"[SceneShot] no button called {press}");
            for (int i = 0; i < 30; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        string[] p = spec.Split(',');
        if (p.Length != 4 ||
            !float.TryParse(p[0], out float x1) || !float.TryParse(p[1], out float y1) ||
            !float.TryParse(p[2], out float x2) || !float.TryParse(p[3], out float y2))
        {
            GD.Print($"[SceneShot] --drag wants x1,y1,x2,y2, got {spec}");
        }
        else
        {
            var a = new Vector2(x1, y1);
            var b = new Vector2(x2, y2);

            Input.WarpMouse(a);
            Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Position = a, GlobalPosition = a, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            const int steps = 12;
            Vector2 last = a;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 at = a.Lerp(b, (float)i / steps);
                Input.ParseInputEvent(new InputEventMouseMotion
                {
                    Position = at, GlobalPosition = at, Relative = at - last,
                    ButtonMask = MouseButtonMask.Left,
                });
                last = at;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Position = b, GlobalPosition = b, Pressed = false });
            GD.Print($"[SceneShot] dragged {a} -> {b}");
        }

        for (int i = 0; i < 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print($"[SceneShot] wrote {path}");
        GetTree().Quit();
    }

    /// <summary>
    /// Types an account and password into the login screen and presses
    /// Connect, so the exported build's own path gets exercised rather
    /// than the environment-variable shortcut the harnesses use.
    /// </summary>
    async void SignIn(string path, int settle, string spec)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        string[] p = spec.Split(',');
        var boxes = new List<LineEdit>();
        Boxes(GetTree().Root, boxes);

        if (p.Length != 2 || boxes.Count < 2)
            GD.Print($"[SceneShot] --login wants user,pass and a login screen; found {boxes.Count} fields");
        else
        {
            boxes[0].Text = p[0];
            boxes[1].Text = p[1];
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Button go = FindButton(GetTree().Root, "Connect");
            if (go != null) { go.EmitSignal(BaseButton.SignalName.Pressed); GD.Print("[SceneShot] pressed Connect"); }
            else GD.Print("[SceneShot] no Connect button");
        }

        for (int i = 0; i < 180; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[SceneShot] wrote {path}");
        GetTree().Quit();
    }

    static void Boxes(Node from, List<LineEdit> into)
    {
        // IsVisibleInTree, not Visible - see Showing. A box inside a
        // hidden panel is flagged visible and would be typed into
        // instead of the one on screen.
        if (from is LineEdit e && Showing(e)) into.Add(e);
        foreach (Node c in from.GetChildren()) Boxes(c, into);
    }

    /// <summary>
    /// Ticks checkboxes by index - the buy list is multi-select, and a
    /// checkbox has no text to find it by. --press opens a panel first.
    /// </summary>
    async void Tick(string path, int settle, string which, string press)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (press != null)
        {
            Button b = FindButton(GetTree().Root, press);
            if (b != null) { b.EmitSignal(BaseButton.SignalName.Pressed); GD.Print($"[SceneShot] pressed {press}"); }
            for (int i = 0; i < 30; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var boxes = new List<CheckBox>();
        Ticks(GetTree().Root, boxes);
        foreach (string part in which.Split(','))
            if (int.TryParse(part, out int n) && n >= 0 && n < boxes.Count)
            {
                boxes[n].ButtonPressed = true;
                boxes[n].EmitSignal(BaseButton.SignalName.Toggled, true);
                GD.Print($"[SceneShot] ticked {n}");
            }
            else GD.Print($"[SceneShot] no checkbox {part} of {boxes.Count}");

        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        foreach (Label l in Labels(GetTree().Root))
            if (l.Visible && l.Text.Contains(" for ")) GD.Print($"[SceneShot] total: {l.Text}");

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[SceneShot] wrote {path}");
        GetTree().Quit();
    }

    static void Collect(Node from, List<InventorySlot> into)
    {
        if (from is InventorySlot s) into.Add(s);
        foreach (Node n in from.GetChildren()) Collect(n, into);
    }

    static void Ticks(Node from, List<CheckBox> into)
    {
        if (from is CheckBox c) into.Add(c);
        foreach (Node n in from.GetChildren()) Ticks(n, into);
    }

    static List<Label> Labels(Node from)
    {
        var all = new List<Label>();
        if (from is Label l) all.Add(l);
        foreach (Node n in from.GetChildren()) all.AddRange(Labels(n));
        return all;
    }

    /// <summary>
    /// Taps an inventory slot by index, then presses buttons in order.
    /// --press takes a comma-separated list here, so a whole path -
    /// open the bag, pick the third thing, drop it - runs in one go.
    /// </summary>
    async void Slot(string path, int settle, string which, string press, string spot = null,
                    string typed = null)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // The list is walked in order and "@slot" is where the tap
        // goes, so a path that needs two presses before it - open a
        // trade, ask to add, pick a thing, offer it - can say so.
        // Without an @slot the tap happens after the first press, which
        // is what the shorter paths want.
        string[] names = (press ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool placed = false;
        foreach (string raw in names)
        { string t = raw.Trim(); if (t == "@slot" || t == "@tap" || t.StartsWith("@tap:")) placed = true; }

        int shot = 0;
        var steps = new List<string>();
        if (placed) steps.AddRange(names);
        else { if (names.Length > 0) steps.Add(names[0]); steps.Add("@slot");
               for (int k = 1; k < names.Length; k++) steps.Add(names[k]); }

        foreach (string raw in steps)
        {
            string step = raw.Trim();
            if (step == "@tap" || step.StartsWith("@tap:"))
            {
                // A tap at a point, placed in the sequence rather than
                // before it - Poke taps first, which is no use when the
                // thing to tap only exists after a button is pressed.
                //
                // "@tap" uses --tap; "@tap:640x1050" carries its own
                // point, so one run can tap two different places - which
                // is the only way to script targeting something and then
                // targeting something else. The separator is an x rather
                // than a comma because the press list is comma
                // separated.
                string where = step.StartsWith("@tap:")
                    ? step.Substring(5).Replace('x', ',')
                    : spot;
                string[] xy = (where ?? "").Split(',');
                if (xy.Length == 2 &&
                    float.TryParse(xy[0], out float tx) && float.TryParse(xy[1], out float ty))
                {
                    var at = new Vector2(tx, ty);
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = true });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = false });
                    GD.Print($"[SceneShot] tapped {at}");
                }
                else GD.Print($"[SceneShot] @tap wants --tap x,y or @tap:XxY, got {where}");
            }
            else if (step.StartsWith("@drag:"))
            {
                // A finger put down, moved, HELD, and lifted:
                // "@drag:300x1400>300x1150@60" presses at the first
                // point, slides to the second over ten frames, holds
                // there for sixty, then lifts.
                //
                // Taps could already be scripted and drags could not,
                // which left the one interaction a phone client is FOR
                // untested: the movement stick and the look drag are
                // both holds, and a stick that is never held reports
                // nothing. Everything about walking around - the move
                // gate, the speed byte, whether the avatar turns - was
                // reachable only by hand.
                string body = step.Substring(6);
                int at = body.IndexOf('@');
                int frames = 60;
                if (at >= 0 && int.TryParse(body.Substring(at + 1), out int f)) { frames = f; body = body.Substring(0, at); }
                string[] ends = body.Split('>');
                if (ends.Length == 2 && Point(ends[0], out Vector2 from) && Point(ends[1], out Vector2 to))
                {
                    // A drag that starts on a button or a panel is eaten
                    // by that control and never reaches the touch layer,
                    // and the run then looks exactly like a client that
                    // cannot walk: no movement, nothing on the wire, no
                    // error. Cost one investigation before it was said out
                    // loud, so now it is said out loud.
                    Control eater = Swallower(GetTree().Root, from);
                    if (eater != null)
                        GD.Print($"[SceneShot] WARNING drag starts on '{eater.Name}' ({eater.GetType().Name}), which will take the touch instead of the world");

                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = from, Pressed = true });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                    Vector2 last = from;
                    const int slide = 10;
                    for (int i = 1; i <= frames; i++)
                    {
                        Vector2 now = i <= slide ? from.Lerp(to, (float)i / slide) : to;
                        // Relative matters as much as Position: the look
                        // half turns by the DELTA, so a drag that only
                        // sets Position turns the camera once and then
                        // sits still.
                        Input.ParseInputEvent(new InputEventScreenDrag
                        { Index = 0, Position = now, Relative = now - last });
                        last = now;
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }

                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = to, Pressed = false });
                    GD.Print($"[SceneShot] dragged {from} -> {to} held {frames}");
                }
                else GD.Print($"[SceneShot] bad drag step: {step}");
            }
            else if (step == "@type")
            {
                // Puts --text into the first visible text box. A form
                // that has to be filled in before its button does
                // anything - the character wizard's name - cannot be
                // tested by pressing buttons alone.
                var boxes = new List<LineEdit>();
                Boxes(GetTree().Root, boxes);
                if (boxes.Count > 0) { boxes[0].Text = typed ?? ""; GD.Print($"[SceneShot] typed \"{typed}\""); }
                else GD.Print("[SceneShot] @type found no text box");
            }
            else if (step == "@submit")
            {
                // Enter in the first visible text box. Typing into a
                // box and pressing a button tests the button; the
                // things that only happen on submit - a say being sent,
                // a line entering the command history - need this.
                var boxes = new List<LineEdit>();
                Boxes(GetTree().Root, boxes);
                if (boxes.Count > 0)
                {
                    // Read before emitting: the handler clears the box,
                    // so printing afterwards always reported an empty
                    // submission and made a working send look broken.
                    string sent = boxes[0].Text;
                    boxes[0].EmitSignal(LineEdit.SignalName.TextSubmitted, sent);
                    GD.Print($"[SceneShot] submitted \"{sent}\"");
                }
                else GD.Print("[SceneShot] @submit found no text box");
            }
            else if (step.StartsWith("@hold:"))
            {
                // A press held down. Several things are bound to a hold
                // rather than a tap - clearing a hotbar button,
                // describing a row instead of ticking it - and none of
                // them could be tested at all: emitting the signal a
                // tap raises skips the press-down that starts the clock,
                // so the harness could only ever tap.
                string want = step.Substring(6);
                Button b = FindNamed(GetTree().Root, want);
                if (b == null)
                {
                    // Say what IS there. A bare "no node called hot9" sent
                    // me hunting through the client for a button that was
                    // on screen the whole time under another name, because
                    // Godot renames a node whose name a sibling already
                    // holds. The names are the only handle a scripted run
                    // has; when one misses, the list is the answer.
                    GD.Print($"[SceneShot] no node called {want}; visible buttons: {Names()}");
                }
                else
                {
                    b.EmitSignal(BaseButton.SignalName.ButtonDown);
                    // Real time, not frames: the code being tested reads
                    // the clock. Long enough for any sensible threshold.
                    for (int i = 0; i < 60; i++)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Hit(b);
                    GD.Print($"[SceneShot] held {want}");
                }
            }
            else if (step.StartsWith("@obj:"))
            {
                // Tap a thing by NAME rather than by guessing where it
                // is. "@obj:duskrat" asks the view for a screen point
                // the renderer's own picker answers with that object,
                // then taps it - the same touch a finger sends, not a
                // shortcut into TargetID.
                //
                // Guessing cost three runs in one session: a tap meant
                // for a player sprite hit the floor twice and the panel
                // under test never opened, which looks exactly like a
                // panel that does not open.
                string want = step.Substring(5);
                GameView view = FindView(GetTree().Root);
                if (view == null) GD.Print("[SceneShot] @obj needs the live view (--host)");
                else if (!view.ScreenPointOf(want, out Vector2 at))
                    GD.Print($"[SceneShot] nothing called \"{want}\" is visible from here");
                else
                {
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = true });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = at, Pressed = false });
                    GD.Print($"[SceneShot] tapped {want} at {at}");
                }
            }
            else if (step.StartsWith("@name:"))
            {
                string want = step.Substring(6);
                Button b = FindNamed(GetTree().Root, want);
                if (b != null) { Hit(b); GD.Print($"[SceneShot] pressed node {want}"); }
                else GD.Print($"[SceneShot] no node called {want}; visible buttons: {Names()}");
            }
            else if (step == "@slot")
            {
                if (!int.TryParse(which, out int n)) continue;
                var slots = new List<InventorySlot>();
                Collect(GetTree().Root, slots);
                if (n >= 0 && n < slots.Count && slots[n].Item != null)
                {
                    Vector2 at = slots[n].GetGlobalRect().GetCenter();
                    Input.ParseInputEvent(new InputEventMouseButton
                    { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = true });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Input.ParseInputEvent(new InputEventMouseButton
                    { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = false });
                    GD.Print($"[SceneShot] tapped slot {n}: {slots[n].Item.Name}");
                }
                else GD.Print($"[SceneShot] no filled slot {n} of {slots.Count}");
            }
            else
            {
                Button b = FindButton(GetTree().Root, step);
                if (b != null) { b.EmitSignal(BaseButton.SignalName.Pressed); GD.Print($"[SceneShot] pressed {step}"); }
                else GD.Print($"[SceneShot] no button called {step}");
            }

            for (int i = 0; i < 30; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // --shots photographs after every step, not only at the end.
            // One run then shows what each press did rather than what
            // the last one left behind, which is the difference between
            // "the panel is wrong" and "the panel was fine until the
            // third tap".
            if (_everyStep)
            {
                shot++;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string each = System.IO.Path.ChangeExtension(path, null) + "-" + shot + ".png";
                GetViewport().GetTexture().GetImage().SavePng(each);
                GD.Print($"[SceneShot] wrote {each} after {raw.Trim()}");
            }
        }

        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[SceneShot] wrote {path}");
        GetTree().Quit();
    }

    /// <summary>
    /// Presses a button, or flips it if it is a checkbox.
    ///
    /// Emitting Pressed on a toggle does nothing useful: its state
    /// changes through Toggled, and a settings switch wired to Toggled
    /// stayed exactly where it was while the harness reported pressing
    /// it.
    /// </summary>
    static void Hit(Button b)
    {
        // Setting the property raises Toggled by itself; emitting it as
        // well fires every handler twice, which showed up as two
        // preference messages for one switch.
        if (b.ToggleMode) b.ButtonPressed = !b.ButtonPressed;
        else b.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>
    /// A button found by node name rather than by its text - list rows
    /// keep their text in a child label, so there is nothing else to
    /// find them by.
    /// </summary>
    /// <summary>Every visible button's node name, for a miss report.</summary>
    string Names()
    {
        var found = new System.Collections.Generic.List<string>();
        Walk(GetTree().Root, found);
        found.Sort();
        return found.Count == 0 ? "(none)" : string.Join(", ", found);
    }

    static void Walk(Node n, System.Collections.Generic.List<string> into)
    {
        if (n is Button b && Showing(b)) into.Add(b.Name);
        foreach (Node c in n.GetChildren()) Walk(c, into);
    }

    /// <summary>
    /// The visible control under this point that would consume a touch,
    /// or null when the point is over the world.
    ///
    /// Deepest match wins, because a child is drawn over its parent and
    /// gets the event first. Controls set to Ignore are skipped: that is
    /// exactly what Ignore means, and every full-screen panel root uses
    /// it so the world stays reachable around its children.
    /// </summary>
    static Control Swallower(Node n, Vector2 p)
    {
        Control hit = null;
        if (n is Control c && Showing(c)
            && c.MouseFilter != Control.MouseFilterEnum.Ignore
            && c.GetGlobalRect().HasPoint(p))
            hit = c;
        foreach (Node child in n.GetChildren())
        {
            Control deeper = Swallower(child, p);
            if (deeper != null) hit = deeper;
        }
        return hit;
    }

    /// <summary>"300x1400" -> a point. False when it is not one.</summary>
    static bool Point(string text, out Vector2 at)
    {
        at = Vector2.Zero;
        string[] xy = text.Split('x');
        if (xy.Length != 2) return false;
        if (!float.TryParse(xy[0], out float x) || !float.TryParse(xy[1], out float y)) return false;
        at = new Vector2(x, y);
        return true;
    }

    /// <summary>
    /// Whether a button is actually on screen.
    ///
    /// Not <c>Visible</c>, which is the node's own flag and says nothing
    /// about its parents. Several panels hide themselves by hiding a
    /// parent Control and leave the buttons inside it flagged visible -
    /// the target row does exactly that - so a search on Visible alone
    /// finds and presses buttons nobody can see. That is how a scripted
    /// "Get" went to the hidden target row instead of the loot window
    /// and quietly did nothing, which looked like the loot window being
    /// broken.
    /// </summary>
    static bool Showing(Control c) => c.IsVisibleInTree();

    static Button FindNamed(Node from, string name)
    {
        if (from is Button b && Showing(b) && b.Name == name) return b;
        foreach (Node child in from.GetChildren())
        {
            Button found = FindNamed(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static Button FindButton(Node from, string text)
    {
        if (from is Button b && Showing(b) && b.Text == text) return b;
        foreach (Node child in from.GetChildren())
        {
            Button found = FindButton(child, text);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>--shots: photograph after every press step.</summary>
    static bool _everyStep;

    static string Arg(string name, string fallback)
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == name) return a[i + 1];
        return fallback;
    }

    /// <summary>The live view, when there is one (--host).</summary>
    static GameView FindView(Node n)
    {
        if (n is GameView v) return v;
        foreach (Node c in n.GetChildren())
        {
            GameView found = FindView(c);
            if (found != null) return found;
        }
        return null;
    }
}
