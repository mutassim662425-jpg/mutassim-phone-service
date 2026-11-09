namespace MUTASSIM_SERVER.Core.Assist;

/// <summary>In-memory record of assist sessions and the results the tool reports back.</summary>
public sealed class AssistSessionStore
{
    private const int MaxSessions = 2000;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, AssistSessionRecord> _sessions = new();
    private readonly Queue<Guid> _order = new();

    public void Add(AssistRequest request, AssistResponse response)
    {
        lock (_sync)
        {
            _sessions[response.SessionId] = new AssistSessionRecord(request, response, DateTime.UtcNow, []);
            _order.Enqueue(response.SessionId);
            while (_order.Count > MaxSessions)
                _sessions.Remove(_order.Dequeue());
        }
    }

    public enum FeedbackOutcome { Recorded, SessionNotFound, ActionNotInSession }

    public FeedbackOutcome AddFeedback(AssistFeedback feedback)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(feedback.SessionId, out var record))
                return FeedbackOutcome.SessionNotFound;

            if (record.Response.Steps.All(s => s.ActionId != feedback.ActionId))
                return FeedbackOutcome.ActionNotInSession;

            var message = feedback.Message is { Length: > 4000 } m ? m[..4000] : feedback.Message;
            _sessions[feedback.SessionId] = record with
            {
                Feedback = [.. record.Feedback, new AssistFeedbackEntry(feedback.ActionId, feedback.Success, message, DateTime.UtcNow)]
            };
            return FeedbackOutcome.Recorded;
        }
    }

    public AssistSessionRecord? Get(Guid id)
    {
        lock (_sync) return _sessions.TryGetValue(id, out var r) ? r : null;
    }

    public IReadOnlyList<AssistSessionRecord> Recent(int max = 50)
    {
        lock (_sync)
            return _order.Reverse().Take(max).Select(id => _sessions[id]).ToList();
    }
}
