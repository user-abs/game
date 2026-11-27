using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ApexDrift;

public sealed class GameForm : Form
{
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    private readonly Game game = new();
    private readonly HashSet<Keys> held = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTime;
    private bool timerResolutionSet;

    private bool fullscreen;
    private FormBorderStyle savedStyle;
    private FormWindowState savedState;

    public GameForm()
    {
        Text = "Apex Drift";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DoubleBuffered = true;

        try
        {
            // Makes the WinForms timer tick at ~8ms instead of the default ~15.6ms.
            timeBeginPeriod(1);
            timerResolutionSet = true;
        }
        catch
        {
            // Not fatal: the game just runs at ~64 fps.
        }

        timer.Tick += (_, _) => Frame();
        timer.Start();
    }

    // ------------------------------------------------------------- game loop

    private void Frame()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - lastTime, 0.1);
        lastTime = now;

        var inp = game.Input;
        inp.Throttle = Held(Keys.W, Keys.Up) ? 1f : 0f;
        inp.Brake = Held(Keys.S, Keys.Down) ? 1f : 0f;
        inp.Steer = (Held(Keys.D, Keys.Right) ? 1f : 0f) - (Held(Keys.A, Keys.Left) ? 1f : 0f);
        inp.Handbrake = held.Contains(Keys.Space);
        inp.Nitro = held.Contains(Keys.ShiftKey) || held.Contains(Keys.LShiftKey) || held.Contains(Keys.RShiftKey);

        game.Update(dt);
        Invalidate();
    }

    private bool Held(Keys a, Keys b) => held.Contains(a) || held.Contains(b);

    // ----------------------------------------------------------------- input

    private void HandleKey(Keys k)
    {
        held.Add(k);

        if (k == Keys.F11)
        {
            ToggleFullscreen();
            return;
        }

        if (game.Screen == AppScreen.Menu)
        {
            switch (k)
            {
                case Keys.Left: game.CycleTrack(-1); break;
                case Keys.Right: game.CycleTrack(1); break;
                case Keys.Up: game.ChangeLaps(1); break;
                case Keys.Down: game.ChangeLaps(-1); break;
                case Keys.Tab: game.CycleDifficulty(); break;
                case Keys.Enter: game.StartRace(); break;
                case Keys.Escape: Close(); break;
            }
        }
        else
        {
            switch (k)
            {
                case Keys.Escape: game.BackToMenu(); break;
                case Keys.P: game.TogglePause(); break;
                case Keys.R: game.Input.Respawn = true; break;
                case Keys.C: game.RotateCamera = !game.RotateCamera; break;
                case Keys.Enter:
                    if (game.Race?.State == RaceState.Finished) game.StartRace();
                    break;
            }
        }
    }

    // Arrow keys and Tab are normally swallowed by WinForms for focus navigation.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Tab)
        {
            HandleKey(key);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        HandleKey(e.KeyCode);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        held.Remove(e.KeyCode);
        e.Handled = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        held.Clear();
        game.PauseIfRacing();
        base.OnDeactivate(e);
    }

    private void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            savedStyle = FormBorderStyle;
            savedState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = savedStyle;
            WindowState = savedState;
        }
        fullscreen = !fullscreen;
    }

    // --------------------------------------------------------------- drawing

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is redrawn in OnPaint; skipping this avoids flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Renderer.Render(e.Graphics, ClientSize.Width, ClientSize.Height, game);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            game.Dispose();
        }
        if (timerResolutionSet)
        {
            try { timeEndPeriod(1); } catch { }
            timerResolutionSet = false;
        }
        base.Dispose(disposing);
    }
}
