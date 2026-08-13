using System.Diagnostics;

namespace WebWindowUI.Tests.Macos;

/// <summary>
/// 顺次跑桥测试场景的 runner：每个场景一个独立超时，PASS/FAIL 逐条打印，结束后置
/// <see cref="Completed"/> 让主线程泵退出。全部在主线程执行（场景经 SC 投递回主队列）。
/// </summary>
public sealed class MacOSTestRunner
{
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(120);
    private readonly List<(string Name, Func<Task> Run)> _scenarios = [];

    public bool Completed { get; private set; }
    public int FailedCount { get; private set; }

    public void Register(string name, Func<Task> run) => _scenarios.Add((name, run));

    public async Task RunAllAsync()
    {
        int passed = 0;
        foreach (var (name, run) in _scenarios)
        {
            Console.WriteLine($"\n== {name} ==");
            var sw = Stopwatch.StartNew();
            try
            {
                await run().WaitAsync(ScenarioTimeout);
                Console.WriteLine($"PASS {name} ({sw.Elapsed.TotalSeconds:F1}s)");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
                FailedCount++;
            }
        }
        Console.WriteLine($"\n{passed}/{_scenarios.Count} passed, {FailedCount} failed");
        Completed = true;
    }
}
