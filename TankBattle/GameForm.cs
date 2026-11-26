using System.Diagnostics;
using System.Numerics;

namespace TankBattle;

public sealed class GameForm : Form
{
    private readonly Game game = new();
    private readonly InputState input = new();
    private readonly HashSet<Keys> held = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private double lastTime;
    private bool mouseFire;

    public GameForm()
    {
        Text = "Tank Battle";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(Game.Width, Game.Height);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Cursor = Cursors.Cross;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DoubleBuffered = true;

        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    // -------------------------------------------------------------- game loop

    private void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - lastTime, 0.05);   // clamp so a hitch can't tunnel bullets
        lastTime = now;

        float x = (Held(Keys.D, Keys.Right) ? 1 : 0) - (Held(Keys.A, Keys.Left) ? 1 : 0);
        float y = (Held(Keys.S, Keys.Down) ? 1 : 0) - (Held(Keys.W, Keys.Up) ? 1 : 0);
        input.Move = new Vector2(x, y);
        input.Fire = mouseFire || held.Contains(Keys.Space);

        var mouse = PointToClient(Cursor.Position);
        input.Aim = new Vector2(mouse.X, mouse.Y);

        game.Update(dt, input);
        Invalidate();
    }

    private bool Held(Keys a, Keys b) => held.Contains(a) || held.Contains(b);

    // ------------------------------------------------------------------ input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        held.Add(e.KeyCode);

        switch (e.KeyCode)
        {
            case Keys.Enter when game.State == GameState.GameOver:
                game.Reset();
                break;
            case Keys.P:
                game.TogglePause();
                break;
            case Keys.Escape:
                Close();
                break;
        }

        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        held.Remove(e.KeyCode);
        base.OnKeyUp(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) mouseFire = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) mouseFire = false;
        base.OnMouseUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // Alt-tabbing away: release keys (we'd miss the KeyUp) and pause.
        held.Clear();
        mouseFire = false;
        game.PauseIfPlaying();
        base.OnDeactivate(e);
    }

    // ---------------------------------------------------------------- drawing

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Intentionally empty: OnPaint redraws everything, which avoids flicker.
    }

    protected override void OnPaint(PaintEventArgs e) => Renderer.Draw(e.Graphics, game);

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
