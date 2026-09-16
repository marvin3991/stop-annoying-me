namespace StopAnnoyingMe.App.Services;

/// <summary>
/// 確保同一個登入工作階段只跑一份。重複啟動時，
/// 新行程會通知既有行程把視窗叫出來，然後自己結束。
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\StopAnnoyingMe.SingleInstance";
    private const string SignalName = @"Local\StopAnnoyingMe.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listener;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    public bool IsFirstInstance { get; }

    /// <summary>另一個行程嘗試啟動時觸發，用來把主視窗叫回前景。</summary>
    public event Action? ActivationRequested;

    /// <summary>通知既有行程顯示視窗。只有第二份行程會呼叫。</summary>
    public void SignalExistingInstance() => _signal.Set();

    /// <summary>開始監聽其他行程的啟動通知。只有第一份行程需要呼叫。</summary>
    public void StartListening()
    {
        if (!IsFirstInstance || _listener is not null)
        {
            return;
        }

        _listener = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };
        _listener.Start();
    }

    private void ListenLoop()
    {
        // 用逾時輪詢而不是無限等待，才能在關閉程式時乾淨地收掉這條執行緒。
        while (!_cancellation.IsCancellationRequested)
        {
            if (_signal.WaitOne(TimeSpan.FromMilliseconds(500)))
            {
                ActivationRequested?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener?.Join(TimeSpan.FromSeconds(1));

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 已經被釋放過，忽略。
            }
        }

        _mutex.Dispose();
        _signal.Dispose();
        _cancellation.Dispose();
    }
}
