using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DungeonDelve;

public sealed class GameForm : Form
{
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    private readonly World world = new();
    private readonly HashSet<Keys> held = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTime;
    private bool timerResolutionSet;
    private bool mouseLeft;
    private bool mouseRight;

    private bool fullscreen;
    private FormBorderStyle savedStyle;
    private FormWindowState savedState;

    public GameForm()
    {
        Text = "Dungeon Delve";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DoubleBuffered = true;

        try
        {
            timeBeginPeriod(1);       // finer timer resolution -> smoother frame pacing
            timerResolutionSet = true;
        }
        catch
        {
            // Not fatal.
        }

        timer.Tick += (_, _) => Frame();
        timer.Start();
    }

    // ------------------------------------------------------------- game loop

    private void Frame()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - lastTime, 0.05);
        lastTime = now;

        var inp = world.Input;
        float mx = (Held(Keys.D, Keys.Right) ? 1f : 0f) - (Held(Keys.A, Keys.Left) ? 1f : 0f);
        float my = (Held(Keys.S, Keys.Down) ? 1f : 0f) - (Held(Keys.W, Keys.Up) ? 1f : 0f);
        inp.Move = new Vector2(mx, my);
        inp.Melee = mouseLeft;
        inp.Fire = mouseRight;

        Point mp = PointToClient(Cursor.Position);
        inp.AimScreen = new Vector2(mp.X, mp.Y);

        world.ViewW = ClientSize.Width;
        world.ViewH = ClientSize.Height;
        world.Update(dt);
        Invalidate();
    }

    private bool Held(Keys a, Keys b) => held.Contains(a) || held.Contains(b);

    // ----------------------------------------------------------------- input

    private void HandleKey(Keys k)
    {
        held.Add(k);
        var inp = world.Input;

        if (k == Keys.F11)
        {
            ToggleFullscreen();
            return;
        }

        switch (world.Phase)
        {
            case Phase.Title:
                if (k == Keys.Enter) world.NewGame();
                else if (k == Keys.Escape) Close();
                break;

            case Phase.Playing:
                switch (k)
                {
                    case Keys.Space: inp.Dash = true; break;
                    case Keys.Q: inp.UseHealth = true; break;
                    case Keys.F: inp.UseMana = true; break;
                    case Keys.E: inp.Interact = true; break;
                    case Keys.Escape:
                    case Keys.P: world.TogglePause(); break;
                }
                break;

            case Phase.Paused:
                if (k == Keys.Escape || k == Keys.P) world.TogglePause();
                else if (k == Keys.Enter) world.ToTitle();
                break;

            case Phase.LevelUp:
                if (k == Keys.D1 || k == Keys.NumPad1) world.ChoosePerk(0);
                else if (k == Keys.D2 || k == Keys.NumPad2) world.ChoosePerk(1);
                else if (k == Keys.D3 || k == Keys.NumPad3) world.ChoosePerk(2);
                break;

            case Phase.Dead:
                if (k == Keys.Enter) world.NewGame();
                else if (k == Keys.Escape) world.ToTitle();
                break;
        }
    }

    // Arrow keys are normally swallowed by WinForms for focus navigation.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
        {
            held.Add(key);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Alt) HandleKey(e.KeyCode);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        held.Remove(e.KeyCode);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) mouseLeft = true;
        if (e.Button == MouseButtons.Right) mouseRight = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) mouseLeft = false;
        if (e.Button == MouseButtons.Right) mouseRight = false;
        base.OnMouseUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        held.Clear();
        mouseLeft = false;
        mouseRight = false;
        world.PauseIfPlaying();
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
        Renderer.Render(e.Graphics, ClientSize.Width, ClientSize.Height, world);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        if (timerResolutionSet)
        {
            try { timeEndPeriod(1); } catch { }
            timerResolutionSet = false;
        }
        base.Dispose(disposing);
    }
}
