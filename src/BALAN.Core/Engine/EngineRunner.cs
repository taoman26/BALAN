namespace BALAN.Core.Engine;

/// <summary>一定周期で <see cref="BalanEngine.Tick"/> を呼ぶ。アプリの存続中、常時動かす。</summary>
public sealed class EngineRunner : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);

    private readonly BalanEngine _engine;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public EngineRunner(BalanEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void Start()
    {
        _loop ??= Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(Interval);
            try
            {
                while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        _engine.Tick();
                    }
                    catch
                    {
                        // 購読者(UI)の例外で巡航ループを止めない。
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _engine.Reset();
        _cts.Dispose();
    }
}
