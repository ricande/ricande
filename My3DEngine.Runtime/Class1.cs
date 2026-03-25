using System;
using System.Diagnostics;

namespace My3DEngine.Runtime;

/// <summary>
/// Small game-loop runner: update + render with a fixed time step for simulation.
/// </summary>
public static class GameLoop
{
    public static void Run(
        Func<bool> shouldContinue,
        Action pumpEvents,
        Action<float> update,
        Action render,
        int targetFps = 60)
    {
        if (targetFps <= 0) { throw new ArgumentOutOfRangeException(nameof(targetFps)); }

        var sw = Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds;
        double accumulator = 0;
        double dt = 1.0 / targetFps;

        try
        {
            while (shouldContinue())
            {
                pumpEvents();

                double now = sw.Elapsed.TotalSeconds;
                double frame = now - last;
                last = now;

                // Clamp to avoid spiral-of-death on breakpoint/hiccup.
                if (frame > 0.25) { frame = 0.25; }

                accumulator += frame;
                while (accumulator >= dt)
                {
                    update((float)dt);
                    accumulator -= dt;
                }

                render();
            }
        }
        catch (Exception ex)
        {
            // Minimal bootstrap error handling; caller can add logging later.
            Console.Error.WriteLine(ex);
            throw;
        }
    }
}
