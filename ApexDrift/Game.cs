using System.Numerics;

namespace ApexDrift;

public enum AppScreen { Menu, Racing }

public sealed class Game : IDisposable
{
    public AppScreen Screen { get; private set; } = AppScreen.Menu;
    public int TrackIndex { get; private set; }
    public int Laps { get; private set; } = 3;
    public Difficulty Difficulty { get; private set; } = Difficulty.Medium;
    public Race? Race { get; private set; }
    public bool Paused { get; private set; }

    public readonly PlayerInput Input = new();

    // Camera
    public Vector2 CamPos;
    public float CamZoom = 0.9f;
    public float CamAngle;
    public bool RotateCamera;

    private readonly Track?[] tracks = new Track?[TrackLibrary.All.Length];

    public Track GetTrack(int index) => tracks[index] ??= new Track(TrackLibrary.All[index]);
    public Track CurrentTrack => GetTrack(TrackIndex);

    // ----------------------------------------------------------- menu actions

    public void CycleTrack(int dir)
    {
        int n = TrackLibrary.All.Length;
        TrackIndex = (TrackIndex + dir + n) % n;
    }

    public void ChangeLaps(int delta) => Laps = Math.Clamp(Laps + delta, 1, 10);

    public void CycleDifficulty() => Difficulty = (Difficulty)(((int)Difficulty + 1) % 3);

    public void StartRace()
    {
        Race = new Race(CurrentTrack, Laps, Difficulty);
        Screen = AppScreen.Racing;
        Paused = false;
        CamPos = Race.Player.Pos;
        CamZoom = 0.9f;
        CamAngle = RotateCamera ? -(Race.Player.Heading + MathF.PI / 2f) : 0f;
    }

    public void BackToMenu()
    {
        Race = null;
        Screen = AppScreen.Menu;
        Paused = false;
    }

    public void TogglePause()
    {
        if (Screen == AppScreen.Racing) Paused = !Paused;
    }

    public void PauseIfRacing()
    {
        if (Screen == AppScreen.Racing) Paused = true;
    }

    // ----------------------------------------------------------------- update

    public void Update(float dt)
    {
        if (Screen != AppScreen.Racing || Race == null || Paused) return;

        Race.Update(dt, Input);
        UpdateCamera(dt, Race.Player);
    }

    private void UpdateCamera(float dt, Car p)
    {
        float spd = p.Vel.Length();
        Vector2 target = p.Pos + p.Vel * 0.25f;     // look ahead of the car
        CamPos = Vector2.Lerp(CamPos, target, 1f - MathF.Exp(-7f * dt));

        float zoomTarget = 1.0f - 0.35f * Math.Clamp(spd / 620f, 0f, 1f);   // zoom out with speed
        CamZoom += (zoomTarget - CamZoom) * (1f - MathF.Exp(-2.5f * dt));

        float angleTarget = RotateCamera ? -(p.Heading + MathF.PI / 2f) : 0f;
        CamAngle += MathUtil.NormalizeAngle(angleTarget - CamAngle) * (1f - MathF.Exp(-5f * dt));
    }

    public void Dispose()
    {
        foreach (var t in tracks) t?.Dispose();
    }
}
