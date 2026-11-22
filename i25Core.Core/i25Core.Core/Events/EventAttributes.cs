namespace i25Core.Core.Events
{
    /// <summary>
    /// Run this listener in the background (Laravel: ShouldQueue) instead of during the request.
    /// The event is serialized to JSON, so it must contain only serializable data.
    /// Needs background jobs (Hangfire) to be enabled; otherwise the listener runs inline.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class QueuedAttribute : Attribute
    {
        /// <summary>Queue name. It must be listed in BackgroundJobs:Queues. Default "default".</summary>
        public string? Queue { get; set; }
    }

    /// <summary>Stops an entity from raising automatic Created / Updated / Deleted events.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class NoModelEventsAttribute : Attribute
    {
    }
}
