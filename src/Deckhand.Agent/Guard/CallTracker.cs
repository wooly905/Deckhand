
namespace Deckhand.Agent.Guard;

/// <summary>Repeated calls and consecutive errors in the current request of one session.</summary>
public sealed class CallTracker
{
    private string? _lastSignature;
    private int _repeat;
    private int _errors;

    /// <summary>Returns how many times in a row this signature was called, including this call.</summary>
    public int RecordCall(string signature)
    {
        _repeat = signature == _lastSignature ? _repeat + 1 : 1;
        _lastSignature = signature;
        return _repeat;
    }

    /// <summary>Returns the number of consecutive failed calls.</summary>
    public int RecordResult(bool success) => _errors = success ? 0 : _errors + 1;

    public void Reset()
    {
        _lastSignature = null;
        _repeat = 0;
        _errors = 0;
    }
}
