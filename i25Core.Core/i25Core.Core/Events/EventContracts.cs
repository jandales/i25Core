namespace i25Core.Core.Events
{
    /// <summary>
    /// Marker for an event: something that happened in the application
    /// (Laravel: an Event class). Use a small class with the data listeners need.
    /// Keep it serializable (simple properties, ids) if a listener is [Queued].
    /// </summary>
    public interface IEvent
    {
    }

    /// <summary>
    /// Reacts to an event (Laravel: a Listener). Implement it for each event you care about;
    /// listeners are found automatically by AddEventListeners(assembly).
    /// A class may implement IEventListener for several events.
    /// </summary>
    public interface IEventListener<TEvent> where TEvent : IEvent
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
    }

    /// <summary>Raises events (Laravel: event(new Something) / Event::dispatch).</summary>
    public interface IEventDispatcher
    {
        /// <summary>
        /// Runs every listener registered for the event's type and its base types.
        /// Listeners marked [Queued] are sent to the background job queue instead of running inline.
        /// </summary>
        Task DispatchAsync(IEvent @event, CancellationToken cancellationToken = default);
    }
}
